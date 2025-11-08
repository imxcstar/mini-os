using System;
using System.Threading.Tasks;
using MiniOS;
using MiniOs.BlazorWasm.Terminal;

namespace MiniOs.BlazorWasm.Services
{
    public sealed class MiniOsTerminalHost
    {
        private readonly BlazorTerminalPlatform _platform = new();
        private readonly object _sync = new();
        private Task? _kernelTask;

        public event Action? OutputChanged
        {
            add => _platform.BufferChanged += value;
            remove => _platform.BufferChanged -= value;
        }

        public bool IsRunning => _kernelTask != null && !_kernelTask.IsCompleted;

        public void EnsureStarted()
        {
            lock (_sync)
            {
                if (_kernelTask != null && !_kernelTask.IsCompleted)
                {
                    return;
                }

                Kernel.UseTerminalPlatform(_platform);
                _kernelTask = Task.Run(async () =>
                {
                    try
                    {
                        await Kernel.BootAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _platform.WriteLine($"Kernel halted: {ex.Message}");
                        _platform.WriteLine("Refresh the page to restart the session.");
                    }
                });
            }
        }

        public string GetBufferSnapshot() => _platform.GetBufferSnapshot();

        public void SubmitLine(string? text) => _platform.SubmitLine(text);
    }
}
