
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public class BfVm
    {
        private readonly Terminal _term;
        private readonly Syscalls _sys;

        public BfVm(Terminal term, Syscalls sys)
        {
            _term = term; _sys = sys;
        }

        public async Task<int> RunAsync(string source, CancellationToken ct)
        {
            var code = Filter(source);
            var jumps = BuildJumps(code);

            byte[] tape = new byte[65536];
            int ptr = 0;
            int ip = 0;

            while (ip < code.Count)
            {
                ct.ThrowIfCancellationRequested();
                char op = code[ip];

                switch (op)
                {
                    case '>': ptr = (ptr + 1) & 0xFFFF; break;
                    case '<': ptr = (ptr - 1) & 0xFFFF; break;
                    case '+': tape[ptr]++; break;
                    case '-': tape[ptr]--; break;
                    case '.': _term.Write(((char)tape[ptr]).ToString()); break;
                    case ',':
                    {
                        int c = _term.ReadChar();
                        tape[ptr] = c < 0 ? (byte)0 : (byte)(c & 0xFF);
                        break;
                    }
                    case '[': if (tape[ptr] == 0) ip = jumps[ip]; break;
                    case ']': if (tape[ptr] != 0) ip = jumps[ip]; break;
                    case '!':
                    {
                        var status = _sys.Invoke(tape, ptr, ct, out var res);
                        tape[ptr] = status;
                        tape[(ptr+1)&0xFFFF] = (byte)(res & 0xFF);
                        tape[(ptr+2)&0xFFFF] = (byte)((res >> 8) & 0xFF);
                        break;
                    }
                }
                ip++;
            }
            await Task.Yield();
            return 0;
        }

        private static List<char> Filter(string s)
        {
            var list = new List<char>(s.Length);
            foreach (var ch in s)
            {
                if (ch is '>' or '<' or '+' or '-' or '.' or ',' or '[' or ']' or '!')
                    list.Add(ch);
                // else comment / whitespace ignored
            }
            return list;
        }

        private static Dictionary<int,int> BuildJumps(List<char> code)
        {
            var jumps = new Dictionary<int,int>();
            var stack = new Stack<int>();
            for (int i=0;i<code.Count;i++)
            {
                if (code[i]=='[') stack.Push(i);
                else if (code[i]==']')
                {
                    var j = stack.Pop();
                    jumps[i]=j; jumps[j]=i;
                }
            }
            if (stack.Count>0) throw new Exception("Unmatched [");
            return jumps;
        }
    }
}
