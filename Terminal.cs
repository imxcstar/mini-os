
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public class Terminal
    {
        private const int SkipKey = int.MinValue;
        private const int EscapeTimeoutMs = 25;
        private readonly ITerminalPlatform _platform;
        private readonly IAsyncTerminalPlatform? _asyncPlatform;
        private readonly Queue<int> _pendingKeys = new();
        private bool _suppressNextLineFeed;
        private bool _preferPlatformKeyInfo;

        public Terminal(ITerminalPlatform? platform = null)
        {
            _platform = platform ?? new ConsoleTerminalPlatform();
            _asyncPlatform = _platform as IAsyncTerminalPlatform;
            TryInitializePlatform();
            _preferPlatformKeyInfo = DetectPlatformKeySupport();
        }

        protected ITerminalPlatform Platform => _platform;

        private void TryInitializePlatform()
        {
            try { _platform.Initialize(); }
            catch { }
        }

        private bool DetectPlatformKeySupport()
        {
            try { return _platform.SupportsKeyEvents; }
            catch { return true; }
        }

        public virtual void Write(string s) => _platform.Write(s);
        public virtual void WriteLine(string s = "") => _platform.WriteLine(s);
        public virtual string? ReadLine()
        {
            if (_asyncPlatform is not null)
            {
                return _asyncPlatform.ReadLineAsync().GetAwaiter().GetResult();
            }
            return _platform.ReadLine();
        }

        public virtual Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            if (_asyncPlatform is not null)
            {
                return _asyncPlatform.ReadLineAsync(cancellationToken).AsTask();
            }
            return Task.FromResult(ReadLine());
        }

        public virtual int ReadChar()
        {
            if (_asyncPlatform is not null)
            {
                return _asyncPlatform.ReadCharAsync().GetAwaiter().GetResult();
            }
            int c = _platform.ReadChar();
            return c < 0 ? -1 : c;
        }

        public virtual Task<int> ReadCharAsync(CancellationToken cancellationToken = default)
        {
            if (_asyncPlatform is not null)
            {
                return _asyncPlatform.ReadCharAsync(cancellationToken).AsTask();
            }
            return Task.FromResult(ReadChar());
        }

        public virtual int ReadKey()
        {
            if (_pendingKeys.Count > 0)
                return NormalizeRawKey(_pendingKeys.Dequeue(), fromQueue: true);

            if (_preferPlatformKeyInfo && TryReadPlatformKey(out var code))
                return code;

            return ReadRawKey();
        }

        public virtual void Clear() => _platform.Clear();
        public virtual void SetCursorPosition(int column, int row) => _platform.SetCursorPosition(Math.Max(0, column), Math.Max(0, row));
        public virtual int CursorColumn => _platform.CursorColumn;
        public virtual int CursorRow => _platform.CursorRow;
        public virtual int ConsoleWidth => _platform.ConsoleWidth;
        public virtual int ConsoleHeight => _platform.ConsoleHeight;
        public virtual void SetCursorVisible(bool visible) => _platform.SetCursorVisible(visible);

        public virtual void Prompt(string cwd) => Write($"{cwd} $ ");

        private bool TryReadPlatformKey(out int code)
        {
            try
            {
                if (_platform.TryReadKey(out code))
                    return true;
            }
            catch
            {
                // swallow and disable fallthrough below
            }
            _preferPlatformKeyInfo = false;
            code = -1;
            return false;
        }

        private int ReadRawKey()
        {
            while (true)
            {
                int value = ReadChar();
                var normalized = NormalizeRawKey(value, fromQueue: false);
                if (normalized == SkipKey)
                    continue;
                return normalized;
            }
        }

        private int NormalizeRawKey(int value, bool fromQueue)
        {
            if (value < 0) return -1;
            if (!fromQueue && value == 0x1B)
                return DecodeEscapeSequence();

            if (value == '\r')
            {
                _suppressNextLineFeed = true;
                return TerminalKeyCodes.Enter;
            }

            if (value == '\n')
            {
                if (_suppressNextLineFeed && !fromQueue)
                {
                    _suppressNextLineFeed = false;
                    return SkipKey;
                }
                _suppressNextLineFeed = false;
                return TerminalKeyCodes.Enter;
            }

            _suppressNextLineFeed = false;
            if (value == '\t') return TerminalKeyCodes.Tab;
            if (value == '\b' || value == 127) return TerminalKeyCodes.Backspace;
            return value;
        }

        private int DecodeEscapeSequence()
        {
            if (!TryReadNextChar(out var second))
                return TerminalKeyCodes.Escape;

            if (second == '[')
            {
                if (!TryReadNextChar(out var third))
                {
                    PushPending(second);
                    return TerminalKeyCodes.Escape;
                }
                switch (third)
                {
                    case 'A': return TerminalKeyCodes.ArrowUp;
                    case 'B': return TerminalKeyCodes.ArrowDown;
                    case 'C': return TerminalKeyCodes.ArrowRight;
                    case 'D': return TerminalKeyCodes.ArrowLeft;
                    case 'H': return TerminalKeyCodes.Home;
                    case 'F': return TerminalKeyCodes.End;
                }
                if (third >= '0' && third <= '9')
                {
                    if (!TryReadNextChar(out var fourth))
                    {
                        PushPending(second, third);
                        return TerminalKeyCodes.Escape;
                    }
                    if (fourth == '~')
                    {
                        switch (third)
                        {
                            case '1':
                            case '7':
                                return TerminalKeyCodes.Home;
                            case '3':
                                return TerminalKeyCodes.Delete;
                            case '4':
                            case '8':
                                return TerminalKeyCodes.End;
                            case '5':
                                return TerminalKeyCodes.PageUp;
                            case '6':
                                return TerminalKeyCodes.PageDown;
                        }
                    }
                    PushPending(second, third, fourth);
                    return TerminalKeyCodes.Escape;
                }
                PushPending(second, third);
                return TerminalKeyCodes.Escape;
            }

            if (second == 'O')
            {
                if (!TryReadNextChar(out var third))
                {
                    PushPending(second);
                    return TerminalKeyCodes.Escape;
                }
                switch (third)
                {
                    case 'H': return TerminalKeyCodes.Home;
                    case 'F': return TerminalKeyCodes.End;
                }
                PushPending(second, third);
                return TerminalKeyCodes.Escape;
            }

            PushPending(second);
            return TerminalKeyCodes.Escape;
        }

        private bool TryReadNextChar(out int value)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                if (KeyAvailableSafe())
                {
                    value = ReadChar();
                    return true;
                }
                if (sw.ElapsedMilliseconds >= EscapeTimeoutMs)
                {
                    value = -1;
                    return false;
                }
                Thread.Sleep(1);
            }
        }

        private bool KeyAvailableSafe()
        {
            try { return _platform.KeyAvailable; }
            catch { return false; }
        }

        private void PushPending(params int[] values)
        {
            if (values is null) return;
            foreach (var value in values)
            {
                if (value >= 0)
                    _pendingKeys.Enqueue(value);
            }
        }
    }

    public static class TerminalKeyCodes
    {
        public const int Enter = 10;
        public const int Escape = 27;
        public const int Backspace = 8;
        public const int Tab = 9;
        public const int ArrowUp = 1001;
        public const int ArrowDown = 1002;
        public const int ArrowLeft = 1003;
        public const int ArrowRight = 1004;
        public const int Home = 1005;
        public const int End = 1006;
        public const int PageUp = 1007;
        public const int PageDown = 1008;
        public const int Delete = 1009;
    }
}
