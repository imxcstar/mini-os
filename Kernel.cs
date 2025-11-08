
using System;
using System.Threading.Tasks;

namespace MiniOS
{
    public static class Kernel
    {
        public static readonly ProcessInputRouter InputRouter = new ProcessInputRouter();
        public static readonly Vfs Vfs = new Vfs();
        public static Terminal Terminal { get; private set; } = new Terminal();
        public static Scheduler Scheduler { get; private set; } = BuildScheduler();
        public static Syscalls Sys { get; private set; } = BuildSyscalls();
        public static ProgramLoader Loader { get; private set; } = BuildProgramLoader();

        public static async Task BootAsync()
        {
            Terminal.WriteLine("MiniOS");
            Terminal.WriteLine("Type `help` for commands.\n");

            Rootfs.Mount(Vfs);

            var shell = new Shell(Vfs, Scheduler, Terminal, Loader, InputRouter);
            await shell.RunAsync();
        }

        public static void UseTerminalPlatform(ITerminalPlatform platform)
        {
            if (platform is null) throw new ArgumentNullException(nameof(platform));
            Terminal = new Terminal(platform);
            RefreshCoreServices();
        }

        private static void RefreshCoreServices()
        {
            Scheduler = BuildScheduler();
            Sys = BuildSyscalls();
            Loader = BuildProgramLoader();
        }

        private static Scheduler BuildScheduler() => new Scheduler(InputRouter, Terminal, Vfs.GetCwd("/"));
        private static Syscalls BuildSyscalls() => new Syscalls(Vfs, Scheduler, Terminal);
        private static ProgramLoader BuildProgramLoader() => new ProgramLoader(Vfs, Scheduler, Terminal, Sys);
    }
}
