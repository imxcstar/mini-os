using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MiniOS
{
    [Flags]
    public enum FileOpenFlags
    {
        Read = 1,
        Write = 2,
        Create = 4,
        Truncate = 8,
        Append = 16
    }

    public readonly record struct SysDirectoryEntry(string Name, bool IsDirectory, long Size);

    internal interface IFileHandle : IDisposable
    {
        bool CanRead { get; }
        bool CanWrite { get; }
        int Read(Span<byte> buffer);
        int Write(ReadOnlySpan<byte> buffer);
        long Seek(long offset, SeekOrigin origin);
        FsNodeInfo Stat();
        bool TryReadDirectoryEntry(out SysDirectoryEntry entry);
        void RewindDirectory();
    }

    public sealed class FileDescriptorTable : IDisposable
    {
        private readonly Dictionary<int, IFileHandle> _handles = new();
        private readonly object _lock = new();
        private int _nextFd = 3;

        private FileDescriptorTable() { }

        public static FileDescriptorTable Create(ProcessIoPipes pipes)
        {
            var table = new FileDescriptorTable();
            table._handles[0] = new StdInHandle(pipes.Input);
            table._handles[1] = new StdOutHandle(pipes.StdOut);
            table._handles[2] = new StdOutHandle(pipes.StdErr);
            return table;
        }

        internal int Open(IFileHandle handle)
        {
            if (handle is null) throw new ArgumentNullException(nameof(handle));
            lock (_lock)
            {
                var fd = _nextFd++;
                _handles[fd] = handle;
                return fd;
            }
        }

        internal bool TryGet(int fd, out IFileHandle handle)
        {
            lock (_lock)
                return _handles.TryGetValue(fd, out handle!);
        }

        internal bool Close(int fd)
        {
            lock (_lock)
            {
                if (!_handles.TryGetValue(fd, out var handle)) return false;
                _handles.Remove(fd);
                handle.Dispose();
                return true;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var handle in _handles.Values.ToList())
                    handle.Dispose();
                _handles.Clear();
            }
        }
    }

    internal sealed class StdInHandle : IFileHandle
    {
        private readonly IInputPipe _pipe;
        public StdInHandle(IInputPipe pipe) => _pipe = pipe;
        public bool CanRead => true;
        public bool CanWrite => false;
        public int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0) return 0;
            var ch = _pipe.ReadChar();
            if (ch < 0) return 0;
            buffer[0] = (byte)ch;
            return 1;
        }
        public int Write(ReadOnlySpan<byte> buffer) => -1;
        public long Seek(long offset, SeekOrigin origin) => -1;
        public FsNodeInfo Stat() => new(true, false, 0);
        public bool TryReadDirectoryEntry(out SysDirectoryEntry entry) { entry = default; return false; }
        public void RewindDirectory() { }
        public void Dispose() { }
    }

    internal sealed class StdOutHandle : IFileHandle
    {
        private readonly IOutputPipe _pipe;
        public StdOutHandle(IOutputPipe pipe) => _pipe = pipe;
        public bool CanRead => false;
        public bool CanWrite => true;
        public int Read(Span<byte> buffer) => -1;
        public int Write(ReadOnlySpan<byte> buffer)
        {
            var text = System.Text.Encoding.UTF8.GetString(buffer);
            _pipe.Write(text);
            return buffer.Length;
        }
        public long Seek(long offset, SeekOrigin origin) => -1;
        public FsNodeInfo Stat() => new(true, false, 0);
        public bool TryReadDirectoryEntry(out SysDirectoryEntry entry) { entry = default; return false; }
        public void RewindDirectory() { }
        public void Dispose() { }
    }

    internal sealed class VfsFileHandle : IFileHandle
    {
        private readonly FileNode _node;
        private readonly bool _canRead;
        private readonly bool _canWrite;
        private int _position;

        public VfsFileHandle(FileNode node, bool canRead, bool canWrite, bool append)
        {
            _node = node;
            _canRead = canRead;
            _canWrite = canWrite;
            _position = append ? node.Data.Length : 0;
        }

        public bool CanRead => _canRead;
        public bool CanWrite => _canWrite;

        public int Read(Span<byte> buffer)
        {
            if (!_canRead) return -1;
            var remaining = _node.Data.Length - _position;
            if (remaining <= 0) return 0;
            var count = Math.Min(remaining, buffer.Length);
            new ReadOnlySpan<byte>(_node.Data, _position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public int Write(ReadOnlySpan<byte> buffer)
        {
            if (!_canWrite) return -1;
            var required = _position + buffer.Length;
            if (required > _node.Data.Length)
            {
                var expanded = new byte[required];
                Array.Copy(_node.Data, expanded, _node.Data.Length);
                _node.Data = expanded;
            }
            buffer.CopyTo(_node.Data.AsSpan(_position));
            _position += buffer.Length;
            return buffer.Length;
        }

        public long Seek(long offset, SeekOrigin origin)
        {
            int target = origin switch
            {
                SeekOrigin.Begin => (int)offset,
                SeekOrigin.Current => _position + (int)offset,
                SeekOrigin.End => _node.Data.Length + (int)offset,
                _ => _position
            };
            if (target < 0) target = 0;
            if (target > _node.Data.Length) target = _node.Data.Length;
            _position = target;
            return _position;
        }

        public FsNodeInfo Stat() => new(true, false, _node.Data.LongLength);
        public bool TryReadDirectoryEntry(out SysDirectoryEntry entry) { entry = default; return false; }
        public void RewindDirectory() { }
        public void Dispose() { }
    }

    internal sealed class VfsDirectoryHandle : IFileHandle
    {
        private readonly List<SysDirectoryEntry> _entries;
        private int _index;

        public VfsDirectoryHandle(IEnumerable<(string name, bool isDir, long size)> entries)
        {
            _entries = entries.Select(e => new SysDirectoryEntry(e.name, e.isDir, e.size)).ToList();
        }

        public bool CanRead => false;
        public bool CanWrite => false;
        public int Read(Span<byte> buffer) => -1;
        public int Write(ReadOnlySpan<byte> buffer) => -1;
        public long Seek(long offset, SeekOrigin origin) => -1;
        public FsNodeInfo Stat() => new(true, true, 0);

        public bool TryReadDirectoryEntry(out SysDirectoryEntry entry)
        {
            if (_index >= _entries.Count)
            {
                entry = default;
                return false;
            }
            entry = _entries[_index++];
            return true;
        }

        public void RewindDirectory() => _index = 0;
        public void Dispose() { }
    }
}
