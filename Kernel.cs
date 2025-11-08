
using System;
using System.Threading.Tasks;

namespace MiniOS
{
    public static class Kernel
    {
        public static readonly ProcessInputRouter InputRouter = new ProcessInputRouter();
        public static readonly Vfs Vfs = new Vfs();
        public static readonly Terminal Terminal = new Terminal();
        public static readonly Scheduler Scheduler = new Scheduler(InputRouter, Terminal, Vfs.GetCwd("/"));
        public static readonly Syscalls Sys = new Syscalls(Vfs, Scheduler, Terminal);
        public static readonly ProgramLoader Loader = new ProgramLoader(Vfs, Scheduler, Terminal, Sys);

        public static async Task BootAsync()
        {
            Console.WriteLine("MiniOS");
            Console.WriteLine("Type `help` for commands.\n");

            Rootfs.Mount(Vfs);

            var shell = new Shell(Vfs, Scheduler, Terminal, Loader, InputRouter);
            await shell.RunAsync();
        }
    }
}
