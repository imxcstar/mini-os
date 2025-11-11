using System;

namespace MiniOS
{
    public sealed class ConsoleTerminalPlatform : ITerminalPlatform
    {
        public void Initialize()
        {
            try { Console.TreatControlCAsInput = true; }
            catch { }
        }

        public void Write(string s) => Console.Write(s);
        public void WriteLine(string s) => Console.WriteLine(s);
        public string? ReadLine() => Console.ReadLine();
        public int ReadChar() => Console.Read();

        public bool TryReadKey(out int keyCode)
        {
            try
            {
                var key = Console.ReadKey(intercept: true);
                keyCode = EncodeConsoleKey(key);
                return true;
            }
            catch
            {
                keyCode = -1;
                return false;
            }
        }

        public bool SupportsKeyEvents
        {
            get
            {
                try { return !Console.IsInputRedirected; }
                catch { return true; }
            }
        }

        public bool KeyAvailable
        {
            get
            {
                try { return Console.KeyAvailable; }
                catch { return false; }
            }
        }

        public void Clear() => Console.Clear();
        public void SetCursorPosition(int column, int row) => Console.SetCursorPosition(column, row);
        public int CursorColumn => Console.CursorLeft;
        public int CursorRow => Console.CursorTop;
        public int ConsoleWidth => Console.BufferWidth;
        public int ConsoleHeight => Console.BufferHeight;

        public void SetCursorVisible(bool visible)
        {
            try { Console.CursorVisible = visible; }
            catch { }
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
    }
}
