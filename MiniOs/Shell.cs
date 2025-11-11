
using System;
using System.Collections.Generic;
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
        private readonly ProcessInputRouter _inputs;

        private DirectoryNode _cwd;

        public Shell(Vfs vfs, Scheduler sched, Terminal term, ProgramLoader loader, ProcessInputRouter inputs)
        {
            _vfs = vfs; _sched = sched; _term = term; _loader = loader; _inputs = inputs;
            _cwd = vfs.GetCwd("/home/user");
            Kernel.Sys.AttachRunner(_loader);
        }

        public async Task RunAsync()
        {
            while (true)
            {
                _term.Prompt(_cwd.Path);
                var line = await _term.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                bool bg = line.EndsWith("&");
                if (bg) line = line[..^1].TrimEnd();

                var parts = SplitArgs(line).ToArray();
                if (parts.Length == 0) continue;
                var cmd = parts[0];
                var args = parts.Skip(1).ToArray();

                try
                {
                    if (cmd == "exit") return;
                    if (HandleBuiltin(cmd, args, bg)) continue;
                    if (await TryLaunchExternalAsync(cmd, args, bg)) continue;
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
                    _term.WriteLine("Builtins: cd, fg, compile, exit");
                    _term.WriteLine("System commands live in /bin (pwd, ls, cat, echo, touch, mkdir, rm, mv, cp, ps, kill, sleep, write). Run any .c file directly.");
                    return true;
                case "cd":
                    _cwd = _vfs.GetCwd(args.Length == 0 ? "/" : Resolve(args[0])); return true;
                case "compile":
                    {
                        if (args.Length < 1) { _term.WriteLine("compile <in.c>"); return true; }
                        var inPath = Resolve(args[0]);
                        var csrc = _vfs.ReadAllText(inPath);
                        var options = new MiniCCompilationOptions
                        {
                            IncludeResolver = MiniCIncludeResolver.ForVfs(_vfs),
                            SourcePath = inPath
                        };
                        MiniCCompiler.Compile(csrc, options);
                        _term.WriteLine("MiniC compilation succeeded");
                        return true;
                    }
                case "fg":
                    {
                        if (args.Length == 0 || !int.TryParse(args[0], out var fgPid))
                        {
                            _term.WriteLine("fg <pid>");
                            return true;
                        }
                        if (!_sched.List().Any(p => p.Pid == fgPid))
                        {
                            _term.WriteLine("no such pid");
                            return true;
                        }
                        if (!_inputs.BringToForeground(fgPid))
                        {
                            _term.WriteLine("pid is not attached to the terminal");
                            return true;
                        }
                        _term.WriteLine($"[fg] attached to {fgPid}");
                        _sched.WaitAsync(fgPid).GetAwaiter().GetResult();
                        return true;
                    }
            }
            return false;
        }

        private async Task<bool> TryLaunchExternalAsync(string cmd, string[] args, bool background)
        {
            var path = ResolveProgramPath(cmd);
            if (path is null) return false;
            var start = new ProcessStartOptions
            {
                InputMode = background ? InputAttachMode.Background : InputAttachMode.Foreground,
                WorkingDirectory = _cwd,
                Arguments = BuildArgumentVector(cmd, args)
            };
            var pid = _loader.SpawnByPath(path, start);
            if (background)
            {
                _term.WriteLine($"[{pid}] background (fg {pid} to attach)");
            }
            else
            {
                await _sched.WaitAsync(pid);
            }
            return true;
        }

        private static IReadOnlyList<string> BuildArgumentVector(string cmd, string[] args)
        {
            var list = new string[args.Length + 1];
            list[0] = cmd;
            Array.Copy(args, 0, list, 1, args.Length);
            return list;
        }

        private string? ResolveProgramPath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            if (command.Contains('/'))
            {
                var path = Resolve(command);
                return _vfs.Exists(path) ? path : null;
            }
            var candidates = new[]
            {
                Resolve(command),
                Resolve(command + ".c"),
                "/bin/" + command,
                "/bin/" + command + ".c"
            };
            foreach (var candidate in candidates)
            {
                if (_vfs.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static IEnumerable<string> SplitArgs(string s)
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
