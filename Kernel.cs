
using System;
using System.Threading.Tasks;

namespace MiniOS
{
    public static class Kernel
    {
        public static readonly Scheduler Scheduler = new Scheduler();
        public static readonly Vfs Vfs = new Vfs();
        public static readonly Terminal Terminal = new Terminal();
        public static readonly Syscalls Sys = new Syscalls(Vfs, Scheduler, Terminal);
        public static readonly SysApi Api = new SysApi(Sys);
        public static readonly ProgramLoader Loader = new ProgramLoader(Vfs, Scheduler, Terminal, Api);

        public static async Task BootAsync()
        {
            Console.WriteLine("MiniOS :: Brainfuck VM + syscalls + MiniC");
            Console.WriteLine("Type `help` for commands.\n");

            Vfs.Mkdir("/bin");
            Vfs.Mkdir("/home");
            Vfs.Mkdir("/home/user");
            Vfs.WriteAllText("/home/user/readme.txt", "Welcome to MiniOS with the Brainfuck VM plus a richer MiniC runtime. Try 'run /home/user/hello.c'.");

            // Seed a feature-rich MiniC hello
            Vfs.WriteAllText("/home/user/hello.c", @"#include <stdio.h>

int main(void) {
    printf(""MiniC runtime demo\n"");
    char buffer[32] = ""Hello from MiniC!"";
    puts(buffer);
    for (int i = 0; i < 5; ++i) {
        printf(""i=%d\n"", i);
    }
    return 0;
}");

            var shell = new Shell(Vfs, Scheduler, Terminal, Loader);
            await shell.RunAsync();
        }
    }
}
