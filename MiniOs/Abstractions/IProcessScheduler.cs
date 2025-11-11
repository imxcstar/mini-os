using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    /// <summary>
    /// Scheduler abstraction to allow alternative process schedulers to be swapped in.
    /// </summary>
    public interface IProcessScheduler
    {
        IEnumerable<ProcessControlBlock> List();
        int Spawn(string name, Func<CancellationToken, Task<int>> entry, ProcessStartOptions? options = null, MiniCMemory? memory = null);
        bool Kill(int pid);
        Task<int> WaitAsync(int pid);
    }
}
