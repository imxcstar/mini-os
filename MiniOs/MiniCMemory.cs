using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace MiniOS
{
    /// <summary>
    /// Simple per-process heap used by the MiniC runtime. Implements a bump allocator with a basic free list.
    /// </summary>
    public sealed class MiniCMemory
    {
        private readonly byte[] _buffer;
        private readonly SortedDictionary<int, int> _freeList = new();
        private readonly Dictionary<int, int> _allocations = new();
        private int _brk;
        private int _allocatedBytes;

        public MiniCMemory(int capacity = 256 * 1024)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new byte[capacity];
        }

        public int Capacity => _buffer.Length;

        public int AllocatedBytes
        {
            get
            {
                lock (_buffer)
                {
                    return _allocatedBytes;
                }
            }
        }

        public MiniCPointer Allocate(int size)
        {
            size = Math.Max(1, size);
            lock (_buffer)
            {
                foreach (var entry in _freeList)
                {
                    if (entry.Value < size) continue;
                    var address = entry.Key;
                    _freeList.Remove(entry.Key);
                    if (entry.Value > size)
                        _freeList[address + size] = entry.Value - size;
                    _allocations[address] = size;
                    _allocatedBytes += size;
                    return new MiniCPointer(this, address);
                }
                if (_brk + size > _buffer.Length)
                    throw new MiniCRuntimeException("MiniC heap exhausted");
                var ptr = new MiniCPointer(this, _brk);
                _allocations[_brk] = size;
                _allocatedBytes += size;
                _brk += size;
                return ptr;
            }
        }

        public void Free(MiniCPointer pointer)
        {
            if (pointer.IsNull) return;
            lock (_buffer)
            {
                if (!_allocations.TryGetValue(pointer.Address, out var size))
                    throw new MiniCRuntimeException("MiniC double free detected");
                _allocations.Remove(pointer.Address);
                _allocatedBytes -= size;
                _freeList[pointer.Address] = size;
            }
        }

        public void WriteBytes(int address, ReadOnlySpan<byte> data)
        {
            lock (_buffer)
            {
                EnsureRange(address, data.Length);
                data.CopyTo(_buffer.AsSpan(address));
            }
        }

        public void ReadBytes(int address, Span<byte> destination)
        {
            lock (_buffer)
            {
                EnsureRange(address, destination.Length);
                _buffer.AsSpan(address, destination.Length).CopyTo(destination);
            }
        }

        public void WriteInt32(int address, int value)
        {
            lock (_buffer)
            {
                EnsureRange(address, 4);
                BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(address, 4), value);
            }
        }

        public int ReadInt32(int address)
        {
            lock (_buffer)
            {
                EnsureRange(address, 4);
                return BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(address, 4));
            }
        }

        public void SetBytes(int address, byte value, int count)
        {
            lock (_buffer)
            {
                EnsureRange(address, count);
                _buffer.AsSpan(address, count).Fill(value);
            }
        }

        public void Copy(MiniCPointer destination, MiniCPointer source, int length)
        {
            if (destination.Memory != this || source.Memory != this)
                throw new MiniCRuntimeException("Memory copy requires matching heaps");
            lock (_buffer)
            {
                EnsureRange(destination.Address, length);
                EnsureRange(source.Address, length);
                _buffer.AsSpan(source.Address, length).CopyTo(_buffer.AsSpan(destination.Address, length));
            }
        }

        public byte ReadByte(int address)
        {
            lock (_buffer)
            {
                EnsureRange(address, 1);
                return _buffer[address];
            }
        }

        public void WriteByte(int address, byte value)
        {
            lock (_buffer)
            {
                EnsureRange(address, 1);
                _buffer[address] = value;
            }
        }

        public string ReadString(MiniCPointer pointer)
        {
            if (pointer.IsNull) return string.Empty;
            lock (_buffer)
            {
                var builder = new StringBuilder();
                int offset = pointer.Address;
                while (offset < _buffer.Length)
                {
                    var b = _buffer[offset++];
                    if (b == 0) break;
                    builder.Append((char)b);
                }
                return builder.ToString();
            }
        }

        public MiniCPointer StoreString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            var ptr = Allocate(bytes.Length + 1);
            WriteBytes(ptr.Address, bytes);
            WriteByte(ptr.Address + bytes.Length, 0);
            return ptr;
        }

        private void EnsureRange(int address, int size)
        {
            if (address < 0 || address + size > _buffer.Length)
                throw new MiniCRuntimeException("MiniC memory access out of range");
        }
    }

    public readonly struct MiniCPointer
    {
        public MiniCMemory? Memory { get; }
        public int Address { get; }

        public bool IsNull => Memory is null || Address < 0;

        public MiniCPointer(MiniCMemory? memory, int address)
        {
            if (memory is null && address >= 0)
                throw new ArgumentNullException(nameof(memory));
            Memory = memory;
            Address = address;
        }

        public static MiniCPointer Null => new(null, -1);

        public MiniCPointer Offset(int delta)
        {
            if (Memory is null) return this;
            return new MiniCPointer(Memory, Address + delta);
        }

        public override string ToString() => IsNull ? "NULL" : $"0x{Address:X}";
    }
}
