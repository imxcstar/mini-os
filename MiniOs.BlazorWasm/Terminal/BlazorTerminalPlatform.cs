using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MiniOS;

namespace MiniOs.BlazorWasm.Terminal
{
    /// <summary>
    /// Bridges the synchronous MiniOS terminal contract with the asynchronous Blazor UI.
    /// Uses channels so that the kernel can block on a background thread without relying on unsupported browser APIs.
    /// </summary>
    public sealed class BlazorTerminalPlatform : ITerminalPlatform, IAsyncTerminalPlatform, IDisposable
    {
        private readonly Channel<string> _lineChannel;
        private readonly Channel<int> _charChannel;
        private readonly ChannelWriter<string> _lineWriter;
        private readonly ChannelReader<string> _lineReader;
        private readonly ChannelWriter<int> _charWriter;
        private readonly ChannelReader<int> _charReader;
        private readonly object _bufferLock = new();
        private readonly StringBuilder _buffer = new();
        private volatile bool _disposed;
        private int _pendingChars;

        public BlazorTerminalPlatform()
        {
            _lineChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = true
            });
            _charChannel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = true
            });
            _lineWriter = _lineChannel.Writer;
            _lineReader = _lineChannel.Reader;
            _charWriter = _charChannel.Writer;
            _charReader = _charChannel.Reader;
        }

        public event Action? BufferChanged;

        public void Initialize() { }

        public void Write(string s) => AppendText(s ?? string.Empty);

        public void WriteLine(string s) => AppendText((s ?? string.Empty) + "\n");

        public string GetBufferSnapshot()
        {
            lock (_bufferLock)
            {
                return _buffer.ToString();
            }
        }

        public void SubmitLine(string? text)
        {
            if (_disposed) return;
            var line = text ?? string.Empty;
            AppendText(line + "\n");
            _lineWriter.TryWrite(line);
            EnqueueCharacters(line);
            EnqueueCharacters("\n");
        }

        public string? ReadLine() => throw new NotSupportedException("Synchronous reads are not supported for the Blazor terminal platform.");

        public int ReadChar() => throw new NotSupportedException("Synchronous reads are not supported for the Blazor terminal platform.");

        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return null;
            try
            {
                return await _lineReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public async ValueTask<int> ReadCharAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return -1;
            try
            {
                var value = await _charReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Decrement(ref _pendingChars);
                return value;
            }
            catch (ChannelClosedException)
            {
                return -1;
            }
        }

        public bool TryReadKey(out int keyCode)
        {
            keyCode = -1;
            return false;
        }

        public bool SupportsKeyEvents => false;
        public bool KeyAvailable => Volatile.Read(ref _pendingChars) > 0;

        public void Clear()
        {
            lock (_bufferLock)
            {
                _buffer.Clear();
            }
            BufferChanged?.Invoke();
        }

        public void SetCursorPosition(int column, int row) { }

        public int CursorColumn => 0;
        public int CursorRow => 0;
        public int ConsoleWidth => 80;
        public int ConsoleHeight => 24;

        public void SetCursorVisible(bool visible) { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lineWriter.TryComplete();
            _charWriter.TryComplete();
        }

        private void AppendText(string text)
        {
            if (text.Length == 0) return;
            lock (_bufferLock)
            {
                _buffer.Append(text);
            }
            BufferChanged?.Invoke();
        }

        private void EnqueueCharacters(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var ch in text)
            {
                EnqueueChar(ch);
            }
        }

        private void EnqueueChar(int ch)
        {
            if (_disposed) return;
            Interlocked.Increment(ref _pendingChars);
            _charWriter.TryWrite(ch);
        }
    }
}
