using System.Collections.Generic;
using System.Threading;

namespace MiniOS
{
    /// <summary>
    /// High-level system API that user-space runtimes (MiniC, shell helpers, etc.) can rely on
    /// without depending directly on the console/VFS implementations.
    /// </summary>
    public interface ISysApi
    {
        void Print(string text);
        void PrintLine(string text = "");
        int ReadChar();
        string ReadLine();
        string Input(string prompt = "");
        string GetCwd();
        void SetCwd(string path);
        string ReadAllText(string path);
        byte[] ReadAllBytes(string path);
        void WriteAllText(string path, string text);
        void WriteAllBytes(string path, byte[] data);
        IEnumerable<(string name, bool isDir, long size)> ListEntries(string path);
        void Remove(string path);
        void Mkdir(string path);
        bool Exists(string path);
        void Rename(string path, string newName);
        void Copy(string source, string destination);
        void Move(string source, string destination);
        IEnumerable<ProcessInfo> ListProcesses();
        bool Kill(int pid);
        int Spawn(string path);
        int Wait(int pid);
        int ArgumentCount();
        string GetArgument(int index);
        uint TimeMilliseconds();
        void Sleep(int milliseconds, CancellationToken ct);
    }

    public readonly record struct ProcessInfo(int Pid, string Name, ProcState State);
}
