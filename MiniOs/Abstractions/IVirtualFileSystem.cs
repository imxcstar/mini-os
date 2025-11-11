using System.Collections.Generic;

namespace MiniOS
{
    /// <summary>
    /// Abstraction over the virtual file system so different back-ends can be plugged in.
    /// </summary>
    public interface IVirtualFileSystem
    {
        DirectoryNode Root { get; }

        void Mkdir(string path, DirectoryNode? cwd = null);
        void Touch(string path, DirectoryNode? cwd = null);
        void Remove(string path, DirectoryNode? cwd = null);
        void Rename(string sourcePath, string destinationPath, DirectoryNode? cwd = null);
        void Move(string source, string destination, DirectoryNode? cwd = null);
        void Copy(string source, string destination, DirectoryNode? cwd = null);
        void WriteAllText(string path, string text, DirectoryNode? cwd = null);
        void WriteAllBytes(string path, byte[] data, DirectoryNode? cwd = null);
        string ReadAllText(string path, DirectoryNode? cwd = null);
        byte[] ReadAllBytes(string path, DirectoryNode? cwd = null);
        FileNode OpenFile(string path, DirectoryNode? cwd = null, bool create = false, bool truncate = false);
        IEnumerable<(string name, bool isDir, long size)> List(string path, DirectoryNode? cwd = null);
        bool Exists(string path, DirectoryNode? cwd = null);
        FsNodeInfo Stat(string path, DirectoryNode? cwd = null);
        DirectoryNode GetCwd(string path, DirectoryNode? cwd = null);
        DirectoryNode EnsureDirectory(string path, DirectoryNode? cwd = null);
    }
}
