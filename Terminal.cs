
using System;

namespace MiniOS
{
    public class Terminal
    {
        public void Write(string s) => Console.Write(s);
        public void WriteLine(string s = "") => Console.WriteLine(s);
        public string? ReadLine() => Console.ReadLine();
        public int ReadChar()
        {
            int c = Console.Read();
            return c < 0 ? -1 : c;
        }
        public void Prompt(string cwd) => Write($"{cwd} $ ");
    }
}
