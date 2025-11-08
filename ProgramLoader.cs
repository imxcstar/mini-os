
using System;
using System.Threading.Tasks;

namespace MiniOS
{
    public class ProgramLoader : IProgramRunner
    {
        private readonly Vfs _vfs;
        private readonly Scheduler _sched;
        private readonly Terminal _term;

        public ProgramLoader(Vfs vfs, Scheduler sched, Terminal term)
        {
            _vfs = vfs; _sched = sched; _term = term;
        }

        public int SpawnBfByPath(string path)
        {
            var src = _vfs.ReadAllText(path);
            var vm = new BfVm(_term, Kernel.Sys);
            var pid = _sched.Spawn($"bf:{path}", async ct => await vm.RunAsync(src, ct));
            return pid;
        }

        public async Task<int> WaitAsync(int pid) => await _sched.WaitAsync(pid);

        public int SpawnByPath(string path)
        {
            if (path.EndsWith(".bf", StringComparison.OrdinalIgnoreCase))
            {
                return SpawnBfByPath(path);
            }
            else if (path.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            {
                var csrc = _vfs.ReadAllText(path);
                var bf = MiniCCompiler.Compile(csrc);
                var tmp = "/tmp/a.bf";
                try { _vfs.Mkdir("/tmp"); } catch {}
                _vfs.WriteAllText(tmp, bf);
                return SpawnBfByPath(tmp);
            }
            else
            {
                throw new InvalidOperationException("unknown program type");
            }
        }
    }
}
