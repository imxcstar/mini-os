
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public interface IProgramRunner
    {
        int SpawnProgram(string path);
        Task<int> WaitAsync(int pid);
    }

    public class Syscalls : ISysApi
    {
        private readonly Vfs _vfs;
        private readonly Scheduler _sched;
        private readonly Terminal _term;
        private IProgramRunner? _runner;

        public Syscalls(Vfs vfs, Scheduler sched, Terminal term)
        {
            _vfs = vfs; _sched = sched; _term = term;
        }

        public void AttachRunner(IProgramRunner runner) => _runner = runner;

        public void Print(string text) => _term.Write(text);
        public void PrintLine(string text = "") => _term.WriteLine(text);
        public int ReadChar() => _term.ReadChar();
        public string ReadLine() => _term.ReadLine() ?? string.Empty;
        public string Input(string prompt = "")
        {
            if (!string.IsNullOrEmpty(prompt))
                _term.Write(prompt);
            return ReadLine();
        }
        public string ReadAllText(string path) => _vfs.ReadAllText(path);
        public byte[] ReadAllBytes(string path) => _vfs.ReadAllBytes(path);
        public void WriteAllText(string path, string text) => _vfs.WriteAllText(path, text);
        public void WriteAllBytes(string path, byte[] data) => _vfs.WriteAllBytes(path, data);
        public IEnumerable<(string name, bool isDir, long size)> ListEntries(string path) => _vfs.List(path);
        public void Remove(string path) => _vfs.Remove(path);
        public void Mkdir(string path) => _vfs.Mkdir(path);
        public bool Exists(string path) => _vfs.Exists(path);
        public void Rename(string path, string newName) => _vfs.Rename(path, newName);
        public void Move(string source, string destination) => _vfs.Move(source, destination);
        public void Copy(string source, string destination) => _vfs.Copy(source, destination);
        public int Spawn(string path)
        {
            if (_runner is null) throw new InvalidOperationException("No program runner attached");
            return _runner.SpawnProgram(path);
        }
        public int Wait(int pid)
        {
            if (_runner is null) throw new InvalidOperationException("No program runner attached");
            return _runner.WaitAsync(pid).GetAwaiter().GetResult();
        }
        public uint TimeMilliseconds() => (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0xFFFFFFFF);
        public void Sleep(int milliseconds, CancellationToken ct)
        {
            if (milliseconds <= 0) return;
            Task.Delay(milliseconds, ct).GetAwaiter().GetResult();
        }
    }
}
