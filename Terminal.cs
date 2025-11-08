
using System;

namespace MiniOS
{
    public class Terminal
    {
        public virtual void Write(string s) => Console.Write(s);
        public virtual void WriteLine(string s = "") => Console.WriteLine(s);
        public virtual string? ReadLine() => Console.ReadLine();
        public virtual int ReadChar()
        {
            int c = Console.Read();
            return c < 0 ? -1 : c;
        }
        public virtual void Prompt(string cwd) => Write($"{cwd} $ ");
    }
}
