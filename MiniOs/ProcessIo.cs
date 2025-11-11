using System;

namespace MiniOS
{
    public interface IInputPipe
    {
        int ReadChar();
        string ReadLine();
        int ReadKey();
    }

    public interface IOutputPipe
    {
        void Write(string text);
        void WriteLine(string text = "");
    }

    public sealed class ProcessIoPipes
    {
        public IInputPipe Input { get; }
        public IOutputPipe StdOut { get; }
        public IOutputPipe StdErr { get; }

        public ProcessIoPipes(IInputPipe input, IOutputPipe stdOut, IOutputPipe stdErr)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            StdOut = stdOut ?? throw new ArgumentNullException(nameof(stdOut));
            StdErr = stdErr ?? throw new ArgumentNullException(nameof(stdErr));
        }

        public static ProcessIoPipes CreateTerminalPipes(ProcessControlBlock pcb, Terminal terminal, ProcessInputRouter router, InputAttachMode inputMode)
        {
            if (pcb is null) throw new ArgumentNullException(nameof(pcb));
            if (terminal is null) throw new ArgumentNullException(nameof(terminal));
            if (router is null) throw new ArgumentNullException(nameof(router));

            IInputPipe input = inputMode == InputAttachMode.None
                ? NullInputPipe.Instance
                : new TerminalInputPipe(pcb, terminal, router);
            var output = new TerminalOutputPipe(terminal);
            return new ProcessIoPipes(input, output, output);
        }

        public static ProcessIoPipes CreateNull() => new ProcessIoPipes(NullInputPipe.Instance, NullOutputPipe.Instance, NullOutputPipe.Instance);
    }

    internal sealed class TerminalInputPipe : IInputPipe
    {
        private readonly ProcessControlBlock _pcb;
        private readonly Terminal _terminal;
        private readonly ProcessInputRouter _router;

        public TerminalInputPipe(ProcessControlBlock pcb, Terminal terminal, ProcessInputRouter router)
        {
            _pcb = pcb;
            _terminal = terminal;
            _router = router;
        }

        private void AwaitForeground()
        {
            _router.WaitForForeground(_pcb.Pid, _pcb.Cts.Token);
        }

        public int ReadChar()
        {
            AwaitForeground();
            return _terminal.ReadChar();
        }

        public string ReadLine()
        {
            AwaitForeground();
            return _terminal.ReadLine() ?? string.Empty;
        }

        public int ReadKey()
        {
            AwaitForeground();
            return _terminal.ReadKey();
        }
    }

    internal sealed class TerminalOutputPipe : IOutputPipe
    {
        private readonly Terminal _terminal;

        public TerminalOutputPipe(Terminal terminal) => _terminal = terminal;

        public void Write(string text) => _terminal.Write(text);

        public void WriteLine(string text = "") => _terminal.WriteLine(text);
    }

    internal sealed class NullInputPipe : IInputPipe
    {
        public static readonly NullInputPipe Instance = new();

        private NullInputPipe() { }

        public int ReadChar() => -1;
        public string ReadLine() => string.Empty;
        public int ReadKey() => -1;
    }

    internal sealed class NullOutputPipe : IOutputPipe
    {
        public static readonly NullOutputPipe Instance = new();

        private NullOutputPipe() { }

        public void Write(string text) { }
        public void WriteLine(string text = "") { }
    }
}
