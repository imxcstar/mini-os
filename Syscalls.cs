
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public interface IProgramRunner
    {
        int SpawnBfByPath(string path);
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
                        _term.Write(s);
                        return 0;
                    }
                    case 2: // read_file(pathAddr, pathLen, outAddr, outMax) -> result=len
                    {
                        var path = Encoding.UTF8.GetString(mem, a1, a2);
                        var data = _vfs.ReadAllBytes(path);
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
                        _vfs.WriteAllBytes(path, data);
                        return 0;
                    }
                    case 4: // ls(dirAddr, dirLen, outAddr, outMax) -> result=len
                    {
                        var dir = Encoding.UTF8.GetString(mem, a1, a2);
                        var sb = new StringBuilder();
                        foreach (var (name,isDir,size) in _vfs.List(dir))
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
                    case 5: // spawn_bf(pathAddr, pathLen) -> result=pid
                    {
                        if (_runner is null) return 2;
                        var path = Encoding.UTF8.GetString(mem, a1, a2);
                        var pid = _runner.SpawnBfByPath(path);
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
                        var ms = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0xFFFFFFFF);
                        mem[a1+0] = (byte)(ms & 0xFF);
                        mem[a1+1] = (byte)((ms >> 8) & 0xFF);
                        mem[a1+2] = (byte)((ms >> 16) & 0xFF);
                        mem[a1+3] = (byte)((ms >> 24) & 0xFF);
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
