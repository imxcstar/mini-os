using System;
using System.IO;
using System.Linq;

namespace MiniOS
{
    public static class Rootfs
    {
        public static void Mount(Vfs vfs)
        {
            var hostPath = LocateOnHost();
            Populate(vfs, hostPath);
        }

        public static string LocateOnHost()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "rootfs"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "rootfs"))
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }
            throw new InvalidOperationException("rootfs directory not found. Build artifacts may be missing.");
        }

        public static void Populate(Vfs vfs, string hostRoot)
        {
            if (!Directory.Exists(hostRoot))
                throw new InvalidOperationException($"rootfs '{hostRoot}' does not exist");

            vfs.EnsureDirectory("/");

            var directories = Directory.GetDirectories(hostRoot, "*", SearchOption.AllDirectories)
                .OrderBy(d => d, StringComparer.Ordinal);
            foreach (var dir in directories)
            {
                var rel = Path.GetRelativePath(hostRoot, dir);
                var targetPath = Normalize(rel);
                if (string.IsNullOrEmpty(targetPath)) continue;
                vfs.EnsureDirectory("/" + targetPath);
            }

            var files = Directory.GetFiles(hostRoot, "*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(hostRoot, file);
                var targetFile = Normalize(rel);
                if (string.IsNullOrEmpty(targetFile)) continue;
                var bytes = File.ReadAllBytes(file);
                vfs.WriteAllBytes("/" + targetFile, bytes);
            }
        }

        public static string ResolveHostPath(string relativePath)
        {
            var root = LocateOnHost();
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(root, normalized);
        }

        private static string Normalize(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
                return string.Empty;
            var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            if (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized[2..];
            return normalized.TrimStart('/');
        }
    }
}
