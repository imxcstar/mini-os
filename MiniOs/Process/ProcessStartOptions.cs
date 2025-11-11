using System.Collections.Generic;

namespace MiniOS
{
    public sealed class ProcessStartOptions
    {
        public InputAttachMode InputMode { get; init; } = InputAttachMode.None;
        public DirectoryNode? WorkingDirectory { get; init; }
        public IReadOnlyList<string>? Arguments { get; init; }
        public ProcessIoPipes? IoPipes { get; init; }
    }
}
