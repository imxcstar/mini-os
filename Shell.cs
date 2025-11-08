
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniOS
{
    public class Shell
    {
        private readonly Vfs _vfs;
        private readonly Scheduler _sched;
        private readonly Terminal _term;
        private readonly ProgramLoader _loader;

        private DirectoryNode _cwd;

        public Shell(Vfs vfs, Scheduler sched, Terminal term, ProgramLoader loader)
        {
            _vfs = vfs; _sched = sched; _term = term; _loader = loader;
            _cwd = vfs.GetCwd("/home/user");
            Kernel.Sys.AttachRunner(_loader);
        }

        public async Task RunAsync()
        {
            while (true)
            {
                _term.Prompt(_cwd.Path);
                var line = _term.ReadLine();
                if (line is null) break;
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                bool bg = line.EndsWith("&");
                if (bg) line = line[..^1].TrimEnd();

                var parts = SplitArgs(line).ToArray();
                var cmd = parts.First();
                var args = parts.Skip(1).ToArray();

                try
                {
                    if (cmd == "exit") return;
                    if (HandleBuiltin(cmd, args, bg)) continue;

                    if (cmd == "run")
                    {
                        if (args.Length == 0) { _term.WriteLine("run <path>"); continue; }
                        var path = Resolve(args[0]);
                        var pid = _loader.SpawnByPath(path);
                        if (!bg) await _sched.WaitAsync(pid);
                        continue;
                    }
                    if (cmd == "compile")
                    {
                        if (args.Length < 1) { _term.WriteLine("compile <in.c>"); continue; }
                        var inPath = Resolve(args[0]);
                        var csrc = _vfs.ReadAllText(inPath);
                        MiniCCompiler.Compile(csrc);
                        _term.WriteLine("MiniC compilation succeeded");
                        continue;
                    }

                    _term.WriteLine($"Unknown command: {cmd}");
                }
                catch (Exception ex)
                {
                    _term.WriteLine($"error: {ex.Message}");
                }
            }
        }

        private bool HandleBuiltin(string cmd, string[] args, bool bg)
        {
            switch (cmd)
            {
                case "help":
                    _term.WriteLine("Builtins: pwd, cd, ls, cat, echo, write, touch, mkdir, rm, mv, cp, rename, ps, kill, sleep, run, compile, exit");
                    _term.WriteLine("Run C .c programs inside the virtual system.");
                    return true;
                case "pwd":
                    _term.WriteLine(_cwd.Path); return true;
                case "cd":
                    _cwd = _vfs.GetCwd(args.Length == 0 ? "/" : Resolve(args[0])); return true;
                case "ls":
                    {
                        var p = args.Length == 0 ? "." : args[0];
                        foreach (var (name, isDir, size) in _vfs.List(Resolve(p)))
                            _term.WriteLine(isDir ? $"{name}/" : $"{name}\t{size}");
                        return true;
                    }
                case "cat":
                    {
                        if (args.Length == 0) { _term.WriteLine("cat <path>"); return true; }
                        _term.WriteLine(_vfs.ReadAllText(Resolve(args[0])));
                        return true;
                    }
                case "echo":
                    _term.WriteLine(string.Join(" ", args)); return true;
                case "write":
                    {
                        if (args.Length < 2) { _term.WriteLine("write <path> <text>"); return true; }
                        var path = Resolve(args[0]);
                        var text = string.Join(" ", args.Skip(1));
                        _vfs.WriteAllText(path, text); return true;
                    }
                case "touch":
                    if (args.Length == 0) { _term.WriteLine("touch <path>"); return true; }
                    _vfs.Touch(Resolve(args[0])); return true;
                case "mkdir":
                    if (args.Length == 0) { _term.WriteLine("mkdir <path>"); return true; }
                    _vfs.Mkdir(Resolve(args[0])); return true;
                case "rm":
                    if (args.Length == 0) { _term.WriteLine("rm <path>"); return true; }
                    _vfs.Remove(Resolve(args[0])); return true;
                case "mv":
                    if (args.Length < 2) { _term.WriteLine("mv <source> <destination>"); return true; }
                    _vfs.Move(Resolve(args[0]), Resolve(args[1])); return true;
                case "cp":
                    if (args.Length < 2) { _term.WriteLine("cp <source> <destination>"); return true; }
                    _vfs.Copy(Resolve(args[0]), Resolve(args[1])); return true;
                case "rename":
                    if (args.Length < 2) { _term.WriteLine("rename <path> <new-name>"); return true; }
                    _vfs.Rename(Resolve(args[0]), args[1]); return true;
                case "ps":
                    foreach (var p in _sched.List()) _term.WriteLine($"{p.Pid}\t{p.State}\t{p.Name}");
                    return true;
                case "kill":
                    if (args.Length == 0 || !int.TryParse(args[0], out var pid)) { _term.WriteLine("kill <pid>"); return true; }
                    if (!_sched.Kill(pid)) _term.WriteLine("no such pid"); return true;
                case "sleep":
                    {
                        var sec = 1; if (args.Length>0 && int.TryParse(args[0], out var s)) sec = s;
                        var pid2 = _sched.Spawn("sleep", async ct => { await Task.Delay(TimeSpan.FromSeconds(sec), ct); return 0; });
                        if (!bg) _sched.WaitAsync(pid2).GetAwaiter().GetResult();
                        return true;
                    }
            }
            return false;
        }

        private static System.Collections.Generic.IEnumerable<string> SplitArgs(string s)
        {
            bool inQuote=false; var cur=new System.Text.StringBuilder();
            foreach (var ch in s)
            {
                if (ch=='"'){ inQuote=!inQuote; continue; }
                if (char.IsWhiteSpace(ch)&&!inQuote){ if (cur.Length>0){ yield return cur.ToString(); cur.Clear(); } }
                else cur.Append(ch);
            }
            if (cur.Length>0) yield return cur.ToString();
        }

        private string Resolve(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return _cwd.Path;
            if (p.StartsWith("/")) return p;
            if (p == ".") return _cwd.Path;
            if (p == "..") return _cwd.Parent?.Path ?? "/";
            return _cwd.Path.TrimEnd('/') + "/" + p;
        }
    }
}
