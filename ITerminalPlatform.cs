namespace MiniOS
{
    public interface ITerminalPlatform
    {
        void Initialize();
        void Write(string s);
        void WriteLine(string s);
        string? ReadLine();
        int ReadChar();
        bool TryReadKey(out int keyCode);
        bool SupportsKeyEvents { get; }
        bool KeyAvailable { get; }
        void Clear();
        void SetCursorPosition(int column, int row);
        int CursorColumn { get; }
        int CursorRow { get; }
        int ConsoleWidth { get; }
        int ConsoleHeight { get; }
        void SetCursorVisible(bool visible);
    }
}
