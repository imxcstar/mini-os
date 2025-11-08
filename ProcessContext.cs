using System.Threading;

namespace MiniOS
{
    /// <summary>
    /// Tracks the currently executing scheduled process so shared services
    /// (like the syscall layer) can access per-process metadata.
    /// </summary>
    public static class ProcessContext
    {
        private static readonly AsyncLocal<ProcessControlBlock?> _current = new();

        public static ProcessControlBlock? Current
        {
            get => _current.Value;
            internal set => _current.Value = value;
        }
    }
}
