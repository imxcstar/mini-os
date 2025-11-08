using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MiniOS
{
    /// <summary>
    /// High-level system API that user-space runtimes (MiniC, shell helpers, etc.) can rely on
    /// without depending directly on the console/VFS implementations.
    /// </summary>
    public interface ISysApi
    {
        int Open(string path, FileOpenFlags flags);
        int Close(int fd);
        int Read(int fd, Span<byte> buffer);
        int Write(int fd, ReadOnlySpan<byte> buffer);
        int Seek(int fd, int offset, SeekOrigin origin);
        int OpenDirectory(string path);
        bool ReadDirectoryEntry(int dirFd, out SysDirectoryEntry entry);
        void RewindDirectory(int dirFd);
        void Print(string text);
        void PrintLine(string text = "");
        int ReadChar();
        string ReadLine();
        string Input(string prompt = "");
        string GetCwd();
        void SetCwd(string path);
        int ReadKey();
        void ClearConsole();
        void SetCursorPosition(int column, int row);
        int GetCursorColumn();
        int GetCursorRow();
        int GetConsoleWidth();
        int GetConsoleHeight();
        void SetCursorVisible(bool visible);
        string ReadAllText(string path);
        byte[] ReadAllBytes(string path);
        void WriteAllText(string path, string text);
        void WriteAllBytes(string path, byte[] data);
        IEnumerable<(string name, bool isDir, long size)> ListEntries(string path);
        void Remove(string path);
        void Mkdir(string path);
        bool Exists(string path);
        void Rename(string source, string destination);
        FsNodeInfo Stat(string path);
        IEnumerable<ProcessInfo> ListProcesses();
        bool Kill(int pid);
        int Spawn(string path);
        int Wait(int pid);
        int ArgumentCount();
        string GetArgument(int index);
        uint TimeMilliseconds();
        void Sleep(int milliseconds, CancellationToken ct);
    }

    public readonly record struct ProcessInfo(int Pid, string Name, ProcState State, int MemoryBytes);
    public readonly record struct FsNodeInfo(bool Exists, bool IsDir, long Size);
}
