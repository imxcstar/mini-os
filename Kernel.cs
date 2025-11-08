
using System;
using System.IO;
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

            Vfs.Mkdir("/bin");
            Vfs.Mkdir("/home");
            Vfs.Mkdir("/home/user");

            SeedSystemCommands();

            var shell = new Shell(Vfs, Scheduler, Terminal, Loader, InputRouter);
            await shell.RunAsync();
        }

        private static void SeedSystemCommands()
        {
            var commandsDir = Path.Combine(AppContext.BaseDirectory, "SystemCommands");
            if (!Directory.Exists(commandsDir)) return;
            var files = Directory.GetFiles(commandsDir, "*.c", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var contents = File.ReadAllText(file);
                Vfs.WriteAllText($"/bin/{name}", contents);
            }
        }
    }
}
