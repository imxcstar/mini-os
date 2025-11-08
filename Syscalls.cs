
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public interface IProgramRunner
    {
        int SpawnProgram(string path);
        Task<int> WaitAsync(int pid);
    }

    public class Syscalls
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

        public void WriteConsole(string text) => _term.Write(text);
        public void WriteConsoleLine(string text) => _term.WriteLine(text);
        public string ReadText(string path) => _vfs.ReadAllText(path);
        public byte[] ReadBytes(string path) => _vfs.ReadAllBytes(path);
        public void WriteAllText(string path, string text) => _vfs.WriteAllText(path, text);
        public void WriteAllBytes(string path, byte[] data) => _vfs.WriteAllBytes(path, data);
        public IEnumerable<(string name, bool isDir, long size)> ListEntries(string path) => _vfs.List(path);
        public void RemovePath(string path) => _vfs.Remove(path);
        public void MakeDirectory(string path) => _vfs.Mkdir(path);
        public int SpawnProgram(string path)
        {
            if (_runner is null) throw new InvalidOperationException("No program runner attached");
            return _runner.SpawnProgram(path);
        }
        public int Wait(int pid)
        {
            if (_runner is null) throw new InvalidOperationException("No program runner attached");
            return _runner.WaitAsync(pid).GetAwaiter().GetResult();
        }
        public uint ClockMilliseconds() => (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0xFFFFFFFF);
        public void Sleep(int milliseconds, CancellationToken ct)
        {
            if (milliseconds <= 0) return;
            Task.Delay(milliseconds, ct).GetAwaiter().GetResult();
        }
        public int ReadConsoleChar() => _term.ReadChar();
        public string ReadConsoleLine() => _term.ReadLine() ?? string.Empty;

        private static ushort U16(byte lo, byte hi) => (ushort)(lo | (hi << 8));

        public byte Invoke(byte[] mem, int ptr, CancellationToken ct, out ushort result)
        {
            byte id = mem[ptr];
            ushort a1 = U16(mem[(ptr+1)&0xFFFF], mem[(ptr+2)&0xFFFF]);
            ushort a2 = U16(mem[(ptr+3)&0xFFFF], mem[(ptr+4)&0xFFFF]);
            ushort a3 = U16(mem[(ptr+5)&0xFFFF], mem[(ptr+6)&0xFFFF]);
            ushort a4 = U16(mem[(ptr+7)&0xFFFF], mem[(ptr+8)&0xFFFF]);
            result = 0;

            try
            {
                switch (id)
                {
                    case 1: // write_console(a1=addr, a2=len)
                    {
                        var s = Encoding.UTF8.GetString(mem, a1, a2);
                        WriteConsole(s);
                        return 0;
                    }
                    case 2: // read_file(pathAddr, pathLen, outAddr, outMax) -> result=len
                    {
                        var path = Encoding.UTF8.GetString(mem, a1, a2);
                        var data = ReadBytes(path);
                        var n = Math.Min(data.Length, a4);
                        Array.Copy(data, 0, mem, a3, n);
                        result = (ushort)n;
                        return 0;
                    }
                    case 3: // write_file(pathAddr, pathLen, dataAddr, dataLen)
                    {
                        var path = Encoding.UTF8.GetString(mem, a1, a2);
                        var data = new byte[a4];
                        Array.Copy(mem, a3, data, 0, a4);
                        WriteAllBytes(path, data);
                        return 0;
                    }
                    case 4: // ls(dirAddr, dirLen, outAddr, outMax) -> result=len
                    {
                        var dir = Encoding.UTF8.GetString(mem, a1, a2);
                        var sb = new StringBuilder();
                        foreach (var (name,isDir,size) in ListEntries(dir))
                        {
                            sb.Append(name);
                            if (isDir) sb.Append("/");
                            sb.AppendLine();
                        }
                        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                        var n = Math.Min(bytes.Length, a4);
                        Array.Copy(bytes, 0, mem, a3, n);
                        result = (ushort)n;
                        return 0;
                    }
                    case 5: // spawn_program(pathAddr, pathLen) -> result=pid
                    {
                        if (_runner is null) return 2;
                        var path = Encoding.UTF8.GetString(mem, a1, a2);
                        var pid = _runner.SpawnProgram(path);
                        result = (ushort)pid;
                        return 0;
                    }
                    case 6: // wait(pid) -> result=exit
                    {
                        if (_runner is null) return 2;
                        var exit = _runner.WaitAsync(a1).GetAwaiter().GetResult();
                        result = (ushort)exit;
                        return 0;
                    }
                    case 7: // time_ms(outAddr)
                    {
                        var ms = ClockMilliseconds();
                        mem[a1+0] = (byte)(ms & 0xFF);
                        mem[a1+1] = (byte)((ms >> 8) & 0xFF);
                        mem[a1+2] = (byte)((ms >> 16) & 0xFF);
                        mem[a1+3] = (byte)((ms >> 24) & 0xFF);
                        return 0;
                    }
                    case 8: // read_console(outAddr, outMax) -> result=len
                    {
                        var input = ReadConsoleLine() + "\n";
                        var bytes = Encoding.UTF8.GetBytes(input);
                        var n = Math.Min(bytes.Length, a2);
                        Array.Copy(bytes, 0, mem, a1, n);
                        result = (ushort)n;
                        return 0;
                    }
                    case 9: // remove_path(pathAddr, pathLen)
                    {
                        var path = Encoding.UTF8.GetString(mem, a1, a2);
                        RemovePath(path);
                        return 0;
                    }
                    case 10: // mkdir(pathAddr, pathLen)
                    {
                        var path = Encoding.UTF8.GetString(mem, a1, a2);
                        MakeDirectory(path);
                        return 0;
                    }
                    case 11: // sleep_ms(milliseconds)
                    {
                        Sleep(a1, ct);
                        return 0;
                    }
                    case 12: // read_char() -> result=char or 0xFFFF if eof
                    {
                        int ch = ReadConsoleChar();
                        result = ch < 0 ? (ushort)0xFFFF : (ushort)(ch & 0xFFFF);
                        return 0;
                    }
                    default: return 1; // ENOSYS
                }
            }
            catch (Exception)
            {
                return 1; // generic error
            }
        }
    }
}
