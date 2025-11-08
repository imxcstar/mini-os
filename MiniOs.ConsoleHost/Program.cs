using MiniOS;

if (args.Length > 0 && args[0] == "--test")
{
    var exit = await MiniTests.RunAsync();
    Environment.Exit(exit);
}

await Kernel.BootAsync();
