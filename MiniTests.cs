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
        vfs.Rename("/home/user/b.txt", "renamed.txt");
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

int main(void) {
    char* lines[3];
    lines[0] = ""alpha"";
    lines[1] = ""beta"";
    lines[2] = strcat(lines[0], lines[1]);
    printf(""len=%d char=%d\n"", strlen(lines[2]), strchar(""xyz"", 1));
    char* slice = substr(""hello"", 1, 3);
    printf(""slice=%s\n"", slice);
    char* who = input(""name?"");
    printf(""hi %s\n"", who);
    if (startswith(lines[2], ""alph"")) {
        puts(""prefix-ok"");
    }
    rename(""/home/user/a.txt"", ""b.txt"");
    copy(""/home/user/b.txt"", ""/home/user/copy.txt"");
    move(""/home/user/copy.txt"", ""/home/user/moved.txt"");
    if (exists(""/home/user/moved.txt"")) puts(""files-ok"");
    return 0;
}";
        var program = MiniCCompiler.Compile(source);
        var runtime = new MiniCRuntime(program, api);
        runtime.Run(CancellationToken.None);
        Assert(term.Output.Contains("len=9"), "strlen result should be printed");
        Assert(term.Output.Contains("slice=ell"), "substr result missing");
        Assert(term.Output.Contains("hi Tester"), "input result missing");
        Assert(term.Output.Contains("files-ok"), "file ops confirmation missing");
        AssertEqual("payload", vfs.ReadAllText("/home/user/moved.txt"), "moved file lost content");
    }

    private static void TestViWorkflow()
    {
        var (vfs, api, term) = CreateSystem();
        term.EnqueueInputs("/home/user/vi-test.txt", ":i", "hello world", ".", ":w", ":q");
        var viPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Vi.c"));
        var viSource = File.ReadAllText(viPath);
        var program = MiniCCompiler.Compile(viSource);
        var runtime = new MiniCRuntime(program, api);
        runtime.Run(CancellationToken.None);
        var saved = vfs.ReadAllText("/home/user/vi-test.txt");
        AssertEqual("hello world", saved.TrimEnd('\n', '\r'), "vi saved content mismatch");
    }

    private static (Vfs vfs, SysApi api, TestTerminal term) CreateSystem()
    {
        var vfs = new Vfs();
        vfs.Mkdir("/home");
        vfs.Mkdir("/home/user");
        var term = new TestTerminal();
        var sched = new Scheduler();
        var syscalls = new Syscalls(vfs, sched, term);
        var api = new SysApi(syscalls);
        return (vfs, api, term);
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
        private readonly Queue<string> _inputs = new();
        private readonly Queue<char> _charQueue = new();
        private readonly StringBuilder _output = new();

        public void EnqueueInputs(params string[] lines)
        {
            foreach (var line in lines) _inputs.Enqueue(line);
        }

        public string Output => _output.ToString();

        public override string? ReadLine()
        {
            if (_inputs.Count == 0) return string.Empty;
            return _inputs.Dequeue();
        }

        public override int ReadChar()
        {
            if (_charQueue.Count == 0)
            {
                var line = ReadLine();
                if (line is null) return -1;
                var expanded = line + "\n";
                foreach (var ch in expanded) _charQueue.Enqueue(ch);
            }
            return _charQueue.Count == 0 ? -1 : _charQueue.Dequeue();
        }

        public override void Write(string s) => _output.Append(s);
        public override void WriteLine(string s = "") => _output.AppendLine(s);
        public override void Prompt(string cwd) => _output.Append($"[{cwd}]$ ");
    }
}
