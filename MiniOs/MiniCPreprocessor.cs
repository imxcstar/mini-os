using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MiniOS
{
    public sealed class MiniCCompilationOptions
    {
        public IMiniCIncludeResolver IncludeResolver { get; init; } = MiniCIncludeResolver.Host;
        public string? SourcePath { get; init; }
    }

    public interface IMiniCIncludeResolver
    {
        bool TryResolve(string include, bool isSystem, string? includingFile, out MiniCIncludeFile file);
    }

    public sealed record MiniCIncludeFile(string Path, string Content);

    internal sealed class MiniCPreprocessor
    {
        private const int MaxIncludeDepth = 64;
        private readonly IMiniCIncludeResolver _resolver;
        private readonly Stack<string> _includeStack = new();
        private readonly HashSet<string> _active = new(StringComparer.Ordinal);

        public MiniCPreprocessor(IMiniCIncludeResolver resolver) => _resolver = resolver;

        public string Process(string source, string? sourcePath)
        {
            var builder = new StringBuilder();
            ProcessInternal(source, Canonicalize(sourcePath), builder, 0);
            return builder.ToString();
        }

        private void ProcessInternal(string source, string currentPath, StringBuilder output, int depth)
        {
            if (depth > MaxIncludeDepth)
                throw new MiniCCompileException("Maximum include depth exceeded");

            bool pushed = false;
            if (!_active.Contains(currentPath))
            {
                _includeStack.Push(currentPath);
                _active.Add(currentPath);
                pushed = true;
            }
            else
            {
                var cycle = string.Join(" -> ", _includeStack.Reverse());
                throw new MiniCCompileException($"Include cycle detected while loading '{currentPath}' ({cycle})");
            }

            try
            {
                using var reader = new StringReader(source);
                string? line;
                int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("#include", StringComparison.Ordinal))
                    {
                        if (!TryParseInclude(trimmed, out var target, out var isSystem))
                            throw BuildIncludeError("Malformed include directive", currentPath, lineNumber);
                        if (!_resolver.TryResolve(target, isSystem, currentPath, out var includeFile))
                            throw BuildIncludeError($"Unable to resolve include '{target}'", currentPath, lineNumber);
                        ProcessInternal(includeFile.Content, Canonicalize(includeFile.Path), output, depth + 1);
                        continue;
                    }
                    if (trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        output.AppendLine();
                        continue;
                    }
                    output.AppendLine(line);
                }
            }
            finally
            {
                if (pushed)
                {
                    _active.Remove(currentPath);
                    _includeStack.Pop();
                }
            }
        }

        private static bool TryParseInclude(string directive, out string path, out bool isSystem)
        {
            path = string.Empty;
            isSystem = false;
            var payload = directive.Substring("#include".Length).Trim();
            if (string.IsNullOrEmpty(payload))
                return false;
            if (payload[0] == '<')
            {
                var end = payload.IndexOf('>');
                if (end <= 1) return false;
                path = payload[1..end].Trim();
                isSystem = true;
                return true;
            }
            if (payload[0] == '"')
            {
                var end = payload.IndexOf('"', 1);
                if (end <= 1) return false;
                path = payload[1..end].Trim();
                isSystem = false;
                return true;
            }
            return false;
        }

        private static MiniCCompileException BuildIncludeError(string message, string currentPath, int line)
        {
            var location = string.IsNullOrEmpty(currentPath) ? "<input>" : currentPath;
            return new MiniCCompileException($"{message} at {location}:{line}");
        }

        private static string Canonicalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "<input>";
            return path.Replace('\\', '/');
        }
    }

    public static class MiniCIncludeResolver
    {
        private static readonly Lazy<IMiniCIncludeResolver> _hostResolver =
            new(() => new HostIncludeResolver(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static IMiniCIncludeResolver Host => _hostResolver.Value;

        public static IMiniCIncludeResolver ForVfs(Vfs vfs, params string[] systemIncludeDirs) =>
            new VfsIncludeResolver(vfs, systemIncludeDirs);

        private sealed class HostIncludeResolver : IMiniCIncludeResolver
        {
            private readonly string[] _includeDirs;
            private readonly string _rootDir = string.Empty;

            public HostIncludeResolver()
            {
                var dirs = new List<string>();
                try
                {
                    _rootDir = Rootfs.LocateOnHost();
                    var include = Path.Combine(_rootDir, "include");
                    if (Directory.Exists(include)) dirs.Add(include);
                    var usrInclude = Path.Combine(_rootDir, Path.Combine("usr", "include"));
                    if (Directory.Exists(usrInclude)) dirs.Add(usrInclude);
                }
                catch
                {
                }
                _includeDirs = dirs.ToArray();
            }

            public bool TryResolve(string include, bool isSystem, string? includingFile, out MiniCIncludeFile file)
            {
                var candidates = new List<string>();
                var normalizedInclude = include.Replace('/', Path.DirectorySeparatorChar);
                if (!string.IsNullOrEmpty(_rootDir) && include.StartsWith("/", StringComparison.Ordinal))
                {
                    var rel = include.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    candidates.Add(Path.Combine(_rootDir, rel));
                }
                else if (Path.IsPathRooted(include))
                {
                    candidates.Add(Path.GetFullPath(include));
                }
                if (!isSystem && !string.IsNullOrEmpty(includingFile))
                {
                    var baseDir = Path.GetDirectoryName(includingFile);
                    if (!string.IsNullOrEmpty(baseDir))
                        candidates.Add(Path.Combine(baseDir, normalizedInclude));
                }

                foreach (var dir in _includeDirs)
                {
                    candidates.Add(Path.Combine(dir, normalizedInclude));
                }

                foreach (var candidate in candidates)
                {
                    if (!File.Exists(candidate)) continue;
                    var content = File.ReadAllText(candidate);
                    file = new MiniCIncludeFile(candidate, content);
                    return true;
                }

                file = default!;
                return false;
            }
        }

        private sealed class VfsIncludeResolver : IMiniCIncludeResolver
        {
            private readonly Vfs _vfs;
            private readonly string[] _systemIncludeDirs;

            public VfsIncludeResolver(Vfs vfs, params string[]? includeDirs)
            {
                _vfs = vfs;
                _systemIncludeDirs = (includeDirs is { Length: > 0 } ? includeDirs : new[] { "/include", "/usr/include" })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            public bool TryResolve(string include, bool isSystem, string? includingFile, out MiniCIncludeFile file)
            {
                var candidates = new List<string>();
                var normalizedInclude = (include ?? string.Empty).Replace('\\', '/');
                var includeIsAbsolute = normalizedInclude.StartsWith("/", StringComparison.Ordinal);
                if (!isSystem && !string.IsNullOrWhiteSpace(includingFile) && !includeIsAbsolute)
                {
                    var baseDir = NormalizeDirectory(includingFile!);
                    if (!string.IsNullOrEmpty(baseDir))
                        candidates.Add(NormalizePath($"{baseDir}/{normalizedInclude}"));
                }
                if (includeIsAbsolute)
                {
                    candidates.Add(NormalizePath(normalizedInclude));
                }
                else
                {
                    foreach (var dir in _systemIncludeDirs)
                    {
                        if (string.IsNullOrWhiteSpace(dir)) continue;
                        candidates.Add(NormalizePath($"{dir.TrimEnd('/')}/{normalizedInclude}"));
                    }
                }

                foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
                {
                    if (!_vfs.Exists(candidate)) continue;
                    try
                    {
                        var content = _vfs.ReadAllText(candidate);
                        file = new MiniCIncludeFile(candidate, content);
                        return true;
                    }
                    catch
                    {
                    }
                }

                file = default!;
                return false;
            }

            private static string NormalizeDirectory(string path)
            {
                var normalized = NormalizePath(path);
                var lastSlash = normalized.LastIndexOf('/');
                if (lastSlash <= 0) return "/";
                return normalized[..lastSlash];
            }

            private static string NormalizePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return "/";
                var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var stack = new Stack<string>();
                foreach (var part in parts)
                {
                    if (part == ".") continue;
                    if (part == "..") { if (stack.Count > 0) stack.Pop(); continue; }
                    stack.Push(part);
                }
                return "/" + string.Join('/', stack.Reverse());
            }
        }
    }
}
