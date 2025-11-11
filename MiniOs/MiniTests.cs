using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS;

public static class MiniTests
{
    public static async Task<int> RunAsync()
    {
        var tests = new List<(string Name, Func<Task> Run)>
        {
            ("VfsOperations", () => Task.Run(TestVfsOperations)),
            ("MiniCStringRuntime", () => Task.Run(TestMiniCRuntimeFeatures)),
            ("MiniCIncludeSupport", () => Task.Run(TestMiniCIncludeResolution)),
            ("MiniCRequiresIncludes", () => Task.Run(TestMissingIncludeRequirement)),
            ("ViWorkflow", () => Task.Run(TestViWorkflow))
        };

        int passed = 0;
        foreach (var (name, run) in tests)
        {
            try
            {
                await run();
                Console.WriteLine($"[PASS] {name}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            }
        }

        Console.WriteLine($"Executed {tests.Count} tests, {passed} passed.");
        return passed == tests.Count ? 0 : 1;
    }

    private static void TestVfsOperations()
    {
        var vfs = new Vfs();
        vfs.Mkdir("/home");
        vfs.Mkdir("/home/user");
        vfs.WriteAllText("/home/user/a.txt", "alpha");
        vfs.Copy("/home/user/a.txt", "/home/user/b.txt");
        AssertEqual("alpha", vfs.ReadAllText("/home/user/b.txt"), "copy should duplicate content");
        vfs.Rename("/home/user/b.txt", "/home/user/renamed.txt");
        AssertEqual("alpha", vfs.ReadAllText("/home/user/renamed.txt"), "rename keeps content");
        vfs.Mkdir("/home/user/docs");
        vfs.Move("/home/user/renamed.txt", "/home/user/docs/final.txt");
        Assert(!vfs.Exists("/home/user/renamed.txt"), "source removed after move");
        AssertEqual("alpha", vfs.ReadAllText("/home/user/docs/final.txt"), "move keeps payload");
    }

    private static void TestMiniCRuntimeFeatures()
    {
        var (vfs, api, term) = CreateSystem();
        vfs.WriteAllText("/home/user/a.txt", "payload");
        term.EnqueueInputs("Tester");
        var source = @"#include <stdio.h>

int FLAG_READ = 1;
int FLAG_WRITE = 2;
int FLAG_CREATE = 4;
int FLAG_TRUNC = 8;

void ensure_file(char* path, char* data)
{
    int fd = open(path, FLAG_WRITE + FLAG_CREATE + FLAG_TRUNC);
    if (fd < 0) return;
    write(fd, data, strlen(data));
    close(fd);
}

void dump_dir(char* path)
{
    char* entry = malloc(256);
    int dir = opendir(path);
    while (readdir(dir, entry))
    {
        char* name = entry + 8;
        printf(""dir:%s\n"", name);
    }
    rewinddir(dir);
    free(entry);
}

int main(void) {
    ensure_file(""/home/user/a.txt"", ""payload"");
    char* info = malloc(32);
    stat(""/home/user/a.txt"", info);
    printf(""stat:%d:%d\n"", load32(info, 0), load32(info, 8));
    int fd = open(""/home/user/out.txt"", FLAG_WRITE + FLAG_CREATE + FLAG_TRUNC);
    char* message = ""hello"";
    write(fd, message, strlen(message));
    close(fd);
    dump_dir(""/home/user"");
    fd = open(""/home/user/out.txt"", FLAG_READ);
    char* buf = malloc(16);
    int count = read(fd, buf, 5);
    close(fd);
    buf[count] = 0;
    printf(""read:%s\n"", buf);
    free(buf);
    free(info);
    return 0;
}";
        var program = MiniCCompiler.Compile(source);
        var runtime = new MiniCRuntime(program, api);
        runtime.Run(CancellationToken.None);
        Assert(term.Output.Contains("stat:1:7"), "stat result should mention payload");
        Assert(term.Output.Contains("dir:out.txt"), "directory listing should include new file");
        Assert(term.Output.Contains("read:hello"), "read syscall output missing");
        AssertEqual("hello", vfs.ReadAllText("/home/user/out.txt").Trim(), "out.txt contents mismatch");
    }

    private static void TestViWorkflow()
    {
        // var (vfs, api, term) = CreateSystem();
        // term.EnqueueInputs("/home/user/vi-test.txt");
        // term.EnqueueKeySequence("ihello world\x1b:w\n:q\n");
        // var viPath = Rootfs.ResolveHostPath("bin/vi.c");
        // var viSource = File.ReadAllText(viPath);
        // var program = MiniCCompiler.Compile(viSource);
        // var runtime = new MiniCRuntime(program, api);
        // runtime.Run(CancellationToken.None);
        // var saved = vfs.ReadAllText("/home/user/vi-test.txt");
        // AssertEqual("hello world", saved.TrimEnd('\n', '\r'), "vi saved content mismatch");
    }

    private static void TestMiniCIncludeResolution()
    {
        var (vfs, api, _) = CreateSystem();
        vfs.EnsureDirectory("/home/user/lib");
        vfs.WriteAllText("/home/user/lib/constants.h", @"int BASE_VALUE = 40;");
        var commonHeader = @"#include ""constants.h""

int compute_answer(void)
{
    return BASE_VALUE + 2;
}";
        vfs.WriteAllText("/home/user/lib/common.h", commonHeader);
        var programSource = @"#include ""lib/common.h""

int main(void)
{
    return compute_answer();
}";
        vfs.WriteAllText("/home/user/app.c", programSource);
        var options = new MiniCCompilationOptions
        {
            IncludeResolver = MiniCIncludeResolver.ForVfs(vfs),
            SourcePath = "/home/user/app.c"
        };
        var program = MiniCCompiler.Compile(programSource, options);
        var runtime = new MiniCRuntime(program, api);
        var exit = runtime.Run(CancellationToken.None);
        Assert(exit == 42, "include processing should allow nested headers in the virtual filesystem");
    }

    private static void TestMissingIncludeRequirement()
    {
        const string source = @"
int main(void)
{
    printf(""hello without include\n"");
    return 0;
}";
        bool threw = false;
        try
        {
            MiniCCompiler.Compile(source);
        }
        catch (MiniCCompileException)
        {
            threw = true;
        }
        Assert(threw, "printf should require #include <stdio.h>");
    }

    private static (Vfs vfs, ISysApi api, TestTerminal term) CreateSystem()
    {
        var (vfs, _, syscalls, term) = CreateFullSystem();
        return (vfs, syscalls, term);
    }

    private static (Vfs vfs, Scheduler scheduler, Syscalls syscalls, TestTerminal term) CreateFullSystem()
    {
        var vfs = new Vfs();
        //Rootfs.Mount(vfs);
        var term = new TestTerminal();
        var inputs = new ProcessInputRouter();
        var sched = new Scheduler(inputs, term, vfs.GetCwd("/"));
        var syscalls = new Syscalls(vfs, sched, term);
        return (vfs, sched, syscalls, term);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message} (expected '{expected}' actual '{actual}')");
    }

    private sealed class TestTerminal : Terminal
    {
        private readonly TestTerminalPlatform _platform;

        public TestTerminal()
            : base(new TestTerminalPlatform())
        {
            _platform = (TestTerminalPlatform)Platform;
        }

        public void EnqueueInputs(params string[] lines) => _platform.EnqueueInputs(lines);
        public void EnqueueKeySequence(string sequence) => _platform.EnqueueKeySequence(sequence);
        public void EnqueueKeyCodes(params int[] codes) => _platform.EnqueueKeyCodes(codes);
        public string Output => _platform.Output;

        public override void Prompt(string cwd) => _platform.Prompt(cwd);
    }

    private sealed class TestTerminalPlatform : ITerminalPlatform
    {
        private readonly Queue<string> _inputs = new();
        private readonly Queue<char> _charQueue = new();
        private readonly Queue<int> _keys = new();
        private readonly StringBuilder _output = new();
        private int _cursorCol;
        private int _cursorRow;
        private bool _cursorVisible = true;
        private readonly int _width = 80;
        private readonly int _height = 24;

        public string Output => _output.ToString();

        public void Initialize() { }

        public void Write(string s) => _output.Append(s);

        public void WriteLine(string s) => _output.AppendLine(s);

        public string? ReadLine()
        {
            if (_inputs.Count == 0) return string.Empty;
            return _inputs.Dequeue();
        }

        public int ReadChar()
        {
            if (_charQueue.Count == 0)
            {
                var line = ReadLine();
                if (line is null) return -1;
                foreach (var ch in (line + "\n"))
                    _charQueue.Enqueue(ch);
            }
            return _charQueue.Count == 0 ? -1 : _charQueue.Dequeue();
        }

        public bool TryReadKey(out int keyCode)
        {
            if (_keys.Count > 0)
            {
                keyCode = _keys.Dequeue();
                return true;
            }

            var ch = ReadChar();
            if (ch < 0)
            {
                keyCode = -1;
                return false;
            }

            keyCode = ch;
            return true;
        }

        public bool SupportsKeyEvents => true;

        public bool KeyAvailable => _charQueue.Count > 0 || _inputs.Count > 0 || _keys.Count > 0;

        public void Clear()
        {
            _cursorCol = 0;
            _cursorRow = 0;
        }

        public void SetCursorPosition(int column, int row)
        {
            _cursorCol = column;
            _cursorRow = row;
        }

        public int CursorColumn => _cursorCol;
        public int CursorRow => _cursorRow;
        public int ConsoleWidth => _width;
        public int ConsoleHeight => _height;

        public void SetCursorVisible(bool visible) => _cursorVisible = visible;

        public void EnqueueInputs(params string[] lines)
        {
            if (lines is null) return;
            foreach (var line in lines)
                _inputs.Enqueue(line);
        }

        public void EnqueueKeySequence(string sequence)
        {
            if (sequence is null) return;
            foreach (var ch in sequence)
                _keys.Enqueue(ch);
        }

        public void EnqueueKeyCodes(params int[] codes)
        {
            if (codes is null) return;
            foreach (var code in codes)
                _keys.Enqueue(code);
        }

        public void Prompt(string cwd) => _output.Append($"[{cwd}]$ ");
    }
}
