
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
        public static readonly ProgramLoader Loader = new ProgramLoader(Vfs, Scheduler, Terminal);

        public static async Task BootAsync()
        {
            Console.WriteLine("MiniOS :: Brainfuck VM + syscalls + MiniC");
            Console.WriteLine("Type `help` for commands.\n");

            Vfs.Mkdir("/bin");
            Vfs.Mkdir("/home");
            Vfs.Mkdir("/home/user");
            Vfs.WriteAllText("/home/user/readme.txt", "Welcome to MiniOS with Brainfuck VM and a tiny C->BF compiler!");

            // Seed a simple C hello
            Vfs.WriteAllText("/home/user/hello.c", @"int main(){ puts(""Hello from MiniC!""); return 0; }");

            var shell = new Shell(Vfs, Scheduler, Terminal, Loader);
            await shell.RunAsync();
        }
    }
}
