
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MiniOS
{
    public class Terminal
    {
        private const int SkipKey = int.MinValue;
        private const int EscapeTimeoutMs = 25;
        private readonly Queue<int> _pendingKeys = new();
        private bool _suppressNextLineFeed;
        private bool _preferConsoleKeyInfo;

        public Terminal()
        {
            try { Console.TreatControlCAsInput = true; }
            catch { }
            _preferConsoleKeyInfo = DetectConsoleKeySupport();
        }

        public virtual void Write(string s) => Console.Write(s);
        public virtual void WriteLine(string s = "") => Console.WriteLine(s);
        public virtual string? ReadLine() => Console.ReadLine();
        public virtual int ReadChar()
        {
            int c = Console.Read();
            return c < 0 ? -1 : c;
        }

        public virtual int ReadKey()
        {
            if (_pendingKeys.Count > 0)
                return NormalizeRawKey(_pendingKeys.Dequeue(), fromQueue: true);

            if (_preferConsoleKeyInfo && TryReadConsoleKey(out var code))
                return code;

            return ReadRawKey();
        }

        public virtual void Clear() => Console.Clear();
        public virtual void SetCursorPosition(int column, int row) => Console.SetCursorPosition(Math.Max(0, column), Math.Max(0, row));
        public virtual int CursorColumn => Console.CursorLeft;
        public virtual int CursorRow => Console.CursorTop;
        public virtual int ConsoleWidth => Console.BufferWidth;
        public virtual int ConsoleHeight => Console.BufferHeight;
        public virtual void SetCursorVisible(bool visible)
        {
            try { Console.CursorVisible = visible; }
            catch { }
        }

        public virtual void Prompt(string cwd) => Write($"{cwd} $ ");

        private static bool DetectConsoleKeySupport()
        {
            try { return !Console.IsInputRedirected; }
            catch { return true; }
        }

        private bool TryReadConsoleKey(out int code)
        {
            try
            {
                var key = Console.ReadKey(intercept: true);
                code = EncodeConsoleKey(key);
                return true;
            }
            catch
            {
                _preferConsoleKeyInfo = false;
                code = -1;
                return false;
            }
        }

        private static int EncodeConsoleKey(ConsoleKeyInfo info)
        {
            switch (info.Key)
            {
                case ConsoleKey.UpArrow: return TerminalKeyCodes.ArrowUp;
                case ConsoleKey.DownArrow: return TerminalKeyCodes.ArrowDown;
                case ConsoleKey.LeftArrow: return TerminalKeyCodes.ArrowLeft;
                case ConsoleKey.RightArrow: return TerminalKeyCodes.ArrowRight;
                case ConsoleKey.Home: return TerminalKeyCodes.Home;
                case ConsoleKey.End: return TerminalKeyCodes.End;
                case ConsoleKey.PageUp: return TerminalKeyCodes.PageUp;
                case ConsoleKey.PageDown: return TerminalKeyCodes.PageDown;
                case ConsoleKey.Delete: return TerminalKeyCodes.Delete;
                case ConsoleKey.Escape: return TerminalKeyCodes.Escape;
                case ConsoleKey.Enter: return TerminalKeyCodes.Enter;
                case ConsoleKey.Tab: return TerminalKeyCodes.Tab;
                case ConsoleKey.Backspace: return TerminalKeyCodes.Backspace;
            }

            var ch = info.KeyChar;
            if (ch == '\r' || ch == '\n') return TerminalKeyCodes.Enter;
            if (ch == '\t') return TerminalKeyCodes.Tab;
            if (ch == '\b' || ch == 127) return TerminalKeyCodes.Backspace;
            return ch;
        }

        private int ReadRawKey()
        {
            while (true)
            {
                int value = Console.Read();
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
                    value = Console.Read();
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

        private static bool KeyAvailableSafe()
        {
            try { return Console.KeyAvailable; }
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
