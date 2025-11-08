using System.Collections.Generic;
using System.Threading;

namespace MiniOS
{
    /// <summary>
    /// High-level system API that user-space runtimes (MiniC, shell helpers, etc.) can rely on
    /// without having to speak the raw Brainfuck syscall protocol.
    /// </summary>
    public class SysApi
    {
        private readonly Syscalls _sys;

        public SysApi(Syscalls sys) => _sys = sys;

        public void Print(string text) => _sys.WriteConsole(text);
        public void PrintLine(string text = "") => _sys.WriteConsoleLine(text);
        public int ReadChar() => _sys.ReadConsoleChar();
        public string ReadLine() => _sys.ReadConsoleLine();
        public string ReadAllText(string path) => _sys.ReadText(path);
        public byte[] ReadAllBytes(string path) => _sys.ReadBytes(path);
        public void WriteAllText(string path, string text) => _sys.WriteAllText(path, text);
        public void WriteAllBytes(string path, byte[] data) => _sys.WriteAllBytes(path, data);
        public IEnumerable<(string name, bool isDir, long size)> ListEntries(string path) => _sys.ListEntries(path);
        public void Remove(string path) => _sys.RemovePath(path);
        public void Mkdir(string path) => _sys.MakeDirectory(path);
        public int Spawn(string path) => _sys.SpawnProgram(path);
        public int Wait(int pid) => _sys.Wait(pid);
        public uint TimeMilliseconds() => _sys.ClockMilliseconds();
        public void Sleep(int milliseconds, CancellationToken ct) => _sys.Sleep(milliseconds, ct);
    }
}
