
using System;
using System.Threading.Tasks;

namespace MiniOS
{
    public class ProgramLoader : IProgramRunner
    {
        private readonly Vfs _vfs;
        private readonly Scheduler _sched;
        private readonly Terminal _term;
        private readonly ISysApi _sys;

        public ProgramLoader(Vfs vfs, Scheduler sched, Terminal term, ISysApi sys)
        {
            _vfs = vfs; _sched = sched; _term = term; _sys = sys;
        }

        public int SpawnProgram(string path) => SpawnByPath(path);

        public async Task<int> WaitAsync(int pid) => await _sched.WaitAsync(pid);

        public int SpawnByPath(string path)
        {
            if (path.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            {
                var csrc = _vfs.ReadAllText(path);
                var program = MiniCCompiler.Compile(csrc);
                var runtime = new MiniCRuntime(program, _sys);
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
                });
                return pid;
            }
            else
            {
                throw new InvalidOperationException("unknown program type");
            }
        }
    }
}
