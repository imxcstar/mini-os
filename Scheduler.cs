
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public enum ProcState { Ready, Running, Stopped, Exited }

    public sealed class ProcessControlBlock
    {
        public int Pid { get; init; }
        public string Name { get; init; } = "proc";
        public ProcState State { get; set; }
        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        public Task<int>? Task { get; set; }
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
    }

    public class Scheduler
    {
        private readonly ConcurrentDictionary<int, ProcessControlBlock> _procs = new();
        private int _nextPid = 100;

        public IEnumerable<ProcessControlBlock> List() => _procs.Values.OrderBy(p => p.Pid);

        public int Spawn(string name, Func<CancellationToken, Task<int>> entry)
        {
            var pid = Interlocked.Increment(ref _nextPid);
            var pcb = new ProcessControlBlock { Pid = pid, Name = name, State = ProcState.Ready };
            pcb.Task = Task.Run(async () =>
            {
                pcb.State = ProcState.Running;
                try
                {
                    var rc = await entry(pcb.Cts.Token);
                    pcb.State = ProcState.Exited;
                    pcb.EndedAt = DateTime.UtcNow;
                    return rc;
                }
                catch (OperationCanceledException)
                {
                    pcb.State = ProcState.Stopped;
                    pcb.EndedAt = DateTime.UtcNow;
                    return 130;
                }
                catch (Exception)
                {
                    pcb.State = ProcState.Exited;
                    pcb.EndedAt = DateTime.UtcNow;
                    return 1;
                }
                finally
                {
                    _procs.TryRemove(pid, out _);
                }
            });
            _procs[pid] = pcb;
            return pid;
        }

        public bool Kill(int pid)
        {
            if (_procs.TryGetValue(pid, out var pcb))
            {
                pcb.Cts.Cancel();
                return true;
            }
            return false;
        }

        public async Task<int> WaitAsync(int pid)
        {
            if (_procs.TryGetValue(pid, out var pcb) && pcb.Task != null)
            {
                return await pcb.Task;
            }
            return -1;
        }
    }
}
