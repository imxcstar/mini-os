
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
        public MiniCMemory? Memory { get; init; }
        public ProcessIoPipes Io { get; set; } = ProcessIoPipes.CreateNull();
        public DirectoryNode WorkingDirectory { get; set; } = null!;
        public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
        public FileDescriptorTable FileTable { get; set; } = null!;
    }

    public class Scheduler
    {
        private readonly ConcurrentDictionary<int, ProcessControlBlock> _procs = new();
        private readonly ProcessInputRouter _inputRouter;
        private readonly Terminal _terminal;
        private readonly DirectoryNode _defaultWorkingDirectory;
        private int _nextPid = 100;

        public Scheduler(ProcessInputRouter inputRouter, Terminal terminal, DirectoryNode defaultWorkingDirectory)
        {
            _inputRouter = inputRouter;
            _terminal = terminal;
            _defaultWorkingDirectory = defaultWorkingDirectory;
        }

        public IEnumerable<ProcessControlBlock> List() => _procs.Values.OrderBy(p => p.Pid);

        public int Spawn(string name, Func<CancellationToken, Task<int>> entry, ProcessStartOptions? options = null, MiniCMemory? memory = null)
        {
            options ??= new ProcessStartOptions();
            var workingDirectory = options.WorkingDirectory ?? _defaultWorkingDirectory;
            var args = options.Arguments?.ToArray() ?? Array.Empty<string>();
            var inputMode = options.InputMode;
            var pid = Interlocked.Increment(ref _nextPid);
            var pcb = new ProcessControlBlock
            {
                Pid = pid,
                Name = name,
                State = ProcState.Ready,
                WorkingDirectory = workingDirectory,
                Arguments = args,
                Memory = memory
            };
            pcb.Io = options.IoPipes ?? ProcessIoPipes.CreateTerminalPipes(pcb, _terminal, _inputRouter, inputMode);
            pcb.FileTable = FileDescriptorTable.Create(pcb.Io);
            _procs[pid] = pcb;
            _inputRouter.Register(pid, inputMode);
            pcb.Task = Task.Run(async () =>
            {
                var previous = ProcessContext.Current;
                ProcessContext.Current = pcb;
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
                    _inputRouter.Unregister(pid);
                    pcb.FileTable.Dispose();
                    ProcessContext.Current = previous;
                }
            });
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
