
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MiniOS
{
    public class ProgramLoader : IProgramRunner
    {
        private readonly Vfs _vfs;
        private readonly Scheduler _sched;
        private readonly Terminal _term;
        private readonly ISysApi _sys;
        private readonly IMiniCIncludeResolver _includeResolver;
        private readonly Dictionary<string, CachedProgram> _programCache = new(StringComparer.Ordinal);
        private readonly object _cacheLock = new();

        public ProgramLoader(Vfs vfs, Scheduler sched, Terminal term, ISysApi sys)
        {
            _vfs = vfs; _sched = sched; _term = term; _sys = sys;
            _includeResolver = MiniCIncludeResolver.ForVfs(vfs);
        }

        public int SpawnProgram(string path, ProcessStartOptions? options = null) => SpawnByPath(path, options);

        public async Task<int> WaitAsync(int pid) => await _sched.WaitAsync(pid);

        public int SpawnByPath(string path, ProcessStartOptions? options = null)
        {
            options ??= new ProcessStartOptions { InputMode = InputAttachMode.Foreground };
            if (path.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            {
                var program = GetOrCompileProgram(path);
                var memory = new MiniCMemory();
                var runtime = new MiniCRuntime(program, _sys, memory);
                var startOptions = new ProcessStartOptions
                {
                    InputMode = options.InputMode == InputAttachMode.None ? InputAttachMode.Foreground : options.InputMode,
                    WorkingDirectory = options.WorkingDirectory,
                    Arguments = NormalizeArguments(path, options.Arguments),
                    IoPipes = options.IoPipes
                };
                var pid = _sched.Spawn($"c:{path}", async ct =>
                {
                    try
                    {
                        return await runtime.RunAsync(ct);
                    }
                    catch (MiniCRuntimeException ex)
                    {
                        _term.WriteLine($"[MiniC] runtime error: {ex.Message}");
                        return 1;
                    }
                    catch (Exception ex)
                    {
                        _term.WriteLine($"[MiniC] fatal error: {ex.Message}");
                        return 1;
                    }
                }, startOptions, memory);
                return pid;
            }
            else
            {
                throw new InvalidOperationException("unknown program type");
            }
        }

        private static IReadOnlyList<string> NormalizeArguments(string path, IReadOnlyList<string>? args)
        {
            if (args is { Count: > 0 })
            {
                var copy = new string[args.Count];
                for (int i = 0; i < args.Count; i++)
                    copy[i] = args[i];
                return copy;
            }
            return new[] { path };
        }

        private MiniCProgram GetOrCompileProgram(string path)
        {
            var csrc = _vfs.ReadAllText(path);
            var hash = ComputeHash(csrc);
            lock (_cacheLock)
            {
                if (_programCache.TryGetValue(path, out var cached) && cached.Hash == hash)
                    return cached.Program;
            }

            var compileOptions = new MiniCCompilationOptions
            {
                IncludeResolver = _includeResolver,
                SourcePath = path
            };
            var program = MiniCCompiler.Compile(csrc, compileOptions);

            lock (_cacheLock)
            {
                _programCache[path] = new CachedProgram(hash, program);
            }

            return program;
        }

        private static string ComputeHash(string source)
        {
            var data = Encoding.UTF8.GetBytes(source);
            var hash = SHA256.HashData(data);
            return Convert.ToHexString(hash);
        }

        private sealed record CachedProgram(string Hash, MiniCProgram Program);
    }
}
