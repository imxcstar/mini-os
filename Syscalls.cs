
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public interface IProgramRunner
    {
        int SpawnProgram(string path, ProcessStartOptions? options = null);
        Task<int> WaitAsync(int pid);
    }

    public class Syscalls : ISysApi
    {
        private readonly Vfs _vfs;
        private readonly Scheduler _sched;
        private readonly Terminal _term;
        private readonly DirectoryNode _rootDir;
        private DirectoryNode _hostWorkingDirectory;
        private readonly IOutputPipe _terminalOutput;
        private readonly IInputPipe _terminalInput;
        private readonly FileDescriptorTable _hostFileTable;
        private IProgramRunner? _runner;

        public Syscalls(Vfs vfs, Scheduler sched, Terminal term)
        {
            _vfs = vfs;
            _sched = sched;
            _term = term;
            _rootDir = vfs.GetCwd("/");
            _terminalOutput = new TerminalOutputPipe(term);
            _terminalInput = new TerminalPassthroughInput(term);
            _hostFileTable = FileDescriptorTable.Create(new ProcessIoPipes(_terminalInput, _terminalOutput, _terminalOutput));
            _hostWorkingDirectory = _rootDir;
        }

        public void AttachRunner(IProgramRunner runner) => _runner = runner;

        private FileDescriptorTable CurrentFileTable => ProcessContext.Current?.FileTable ?? _hostFileTable;

        private bool TryGetHandle(int fd, out IFileHandle handle) => CurrentFileTable.TryGet(fd, out handle!);

        public int Open(string path, FileOpenFlags flags)
        {
            try
            {
                var cwd = GetWorkingDirectory();
                var node = _vfs.OpenFile(path, cwd, flags.HasFlag(FileOpenFlags.Create), flags.HasFlag(FileOpenFlags.Truncate));
                var canRead = flags.HasFlag(FileOpenFlags.Read) || !flags.HasFlag(FileOpenFlags.Write);
                var canWrite = flags.HasFlag(FileOpenFlags.Write) || flags.HasFlag(FileOpenFlags.Append);
                var handle = new VfsFileHandle(node, canRead, canWrite, flags.HasFlag(FileOpenFlags.Append));
                return CurrentFileTable.Open(handle);
            }
            catch
            {
                return -1;
            }
        }

        public int Close(int fd)
        {
            return CurrentFileTable.Close(fd) ? 0 : -1;
        }

        public int Read(int fd, Span<byte> buffer)
        {
            if (!TryGetHandle(fd, out var handle) || !handle.CanRead) return -1;
            return handle.Read(buffer);
        }

        public int Write(int fd, ReadOnlySpan<byte> buffer)
        {
            if (!TryGetHandle(fd, out var handle) || !handle.CanWrite) return -1;
            return handle.Write(buffer);
        }

        public int Seek(int fd, int offset, SeekOrigin origin)
        {
            if (!TryGetHandle(fd, out var handle)) return -1;
            var result = handle.Seek(offset, origin);
            return result < 0 ? -1 : (int)result;
        }

        public int OpenDirectory(string path)
        {
            try
            {
                var entries = _vfs.List(path, GetWorkingDirectory());
                var handle = new VfsDirectoryHandle(entries);
                return CurrentFileTable.Open(handle);
            }
            catch
            {
                return -1;
            }
        }

        public bool ReadDirectoryEntry(int dirFd, out SysDirectoryEntry entry)
        {
            if (!TryGetHandle(dirFd, out var handle)) { entry = default; return false; }
            return handle.TryReadDirectoryEntry(out entry);
        }

        public void RewindDirectory(int dirFd)
        {
            if (TryGetHandle(dirFd, out var handle))
                handle.RewindDirectory();
        }

        public void Print(string text) => GetStdOut().Write(text);
        public void PrintLine(string text = "") => GetStdOut().WriteLine(text);
        public int ReadChar() => GetStdIn().ReadChar();
        public string ReadLine() => GetStdIn().ReadLine();
        public int ReadKey() => GetStdIn().ReadKey();
        public void ClearConsole() => _term.Clear();
        public void SetCursorPosition(int column, int row) => _term.SetCursorPosition(column, row);
        public int GetCursorColumn() => _term.CursorColumn;
        public int GetCursorRow() => _term.CursorRow;
        public int GetConsoleWidth() => _term.ConsoleWidth;
        public int GetConsoleHeight() => _term.ConsoleHeight;
        public void SetCursorVisible(bool visible) => _term.SetCursorVisible(visible);
        public string Input(string prompt = "")
        {
            if (!string.IsNullOrEmpty(prompt))
                GetStdOut().Write(prompt);
            return GetStdIn().ReadLine();
        }
        public string GetCwd() => GetWorkingDirectory().Path;
        public void SetCwd(string path)
        {
            var pcb = ProcessContext.Current;
            var baseDir = pcb?.WorkingDirectory ?? _hostWorkingDirectory;
            var next = _vfs.GetCwd(path, baseDir);
            if (pcb is null) _hostWorkingDirectory = next;
            else pcb.WorkingDirectory = next;
        }
        public string ReadAllText(string path) => _vfs.ReadAllText(path, GetWorkingDirectory());
        public byte[] ReadAllBytes(string path) => _vfs.ReadAllBytes(path, GetWorkingDirectory());
        public void WriteAllText(string path, string text) => _vfs.WriteAllText(path, text, GetWorkingDirectory());
        public void WriteAllBytes(string path, byte[] data) => _vfs.WriteAllBytes(path, data, GetWorkingDirectory());
        public IEnumerable<(string name, bool isDir, long size)> ListEntries(string path) => _vfs.List(path, GetWorkingDirectory());
        public void Remove(string path) => _vfs.Remove(path, GetWorkingDirectory());
        public void Mkdir(string path) => _vfs.Mkdir(path, GetWorkingDirectory());
        public bool Exists(string path) => _vfs.Exists(path, GetWorkingDirectory());
        public void Rename(string source, string destination) => _vfs.Rename(source, destination, GetWorkingDirectory());
        public FsNodeInfo Stat(string path) => _vfs.Stat(path, GetWorkingDirectory());
        public IEnumerable<ProcessInfo> ListProcesses()
        {
            foreach (var proc in _sched.List())
            {
                var usedBytes = proc.Memory?.AllocatedBytes ?? 0;
                yield return new ProcessInfo(proc.Pid, proc.Name, proc.State, usedBytes);
            }
        }
        public bool Kill(int pid) => _sched.Kill(pid);
        public int Spawn(string path)
        {
            if (_runner is null) throw new InvalidOperationException("No program runner attached");
            var pcb = ProcessContext.Current;
            var options = new ProcessStartOptions
            {
                WorkingDirectory = pcb?.WorkingDirectory ?? _rootDir,
                InputMode = InputAttachMode.Background,
                Arguments = new[] { path }
            };
            return _runner.SpawnProgram(path, options);
        }
        public int Wait(int pid)
        {
            if (_runner is null) throw new InvalidOperationException("No program runner attached");
            return _runner.WaitAsync(pid).GetAwaiter().GetResult();
        }
        public int ArgumentCount()
        {
            var args = ProcessContext.Current?.Arguments;
            return args?.Count ?? 0;
        }
        public string GetArgument(int index)
        {
            var args = ProcessContext.Current?.Arguments;
            if (args is null || index < 0 || index >= args.Count) return string.Empty;
            return args[index];
        }
        public uint TimeMilliseconds() => (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0xFFFFFFFF);
        public void Sleep(int milliseconds, CancellationToken ct)
        {
            if (milliseconds <= 0) return;
            Task.Delay(milliseconds, ct).GetAwaiter().GetResult();
        }

        private IOutputPipe GetStdOut()
        {
            var pcb = ProcessContext.Current;
            return pcb?.Io.StdOut ?? _terminalOutput;
        }

        private IInputPipe GetStdIn()
        {
            var pcb = ProcessContext.Current;
            if (pcb is null) return _terminalInput;
            return pcb.Io.Input;
        }

        private DirectoryNode GetWorkingDirectory()
        {
            var pcb = ProcessContext.Current;
            return pcb?.WorkingDirectory ?? _hostWorkingDirectory;
        }

        private sealed class TerminalPassthroughInput : IInputPipe
        {
            private readonly Terminal _terminal;

            public TerminalPassthroughInput(Terminal terminal) => _terminal = terminal;

            public int ReadChar() => _terminal.ReadChar();
            public string ReadLine() => _terminal.ReadLine() ?? string.Empty;
            public int ReadKey() => _terminal.ReadKey();
        }
    }
}
