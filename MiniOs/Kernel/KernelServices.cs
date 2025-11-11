namespace MiniOS
{
    /// <summary>
    /// Bag of concrete core services built by the kernel builder.
    /// Acts as a simple service locator for consumers that opt-out of dependency injection.
    /// </summary>
    public sealed class KernelServices
    {
        public KernelServices(
            IVirtualFileSystem fileSystem,
            Terminal terminal,
            ProcessInputRouter inputRouter,
            IProcessScheduler scheduler,
            ISysApi systemApi,
            IProgramLoader programLoader)
        {
            FileSystem = fileSystem;
            Terminal = terminal;
            InputRouter = inputRouter;
            Scheduler = scheduler;
            SystemApi = systemApi;
            ProgramLoader = programLoader;
        }

        public IVirtualFileSystem FileSystem { get; }
        public Terminal Terminal { get; }
        public ProcessInputRouter InputRouter { get; }
        public IProcessScheduler Scheduler { get; }
        public ISysApi SystemApi { get; }
        public IProgramLoader ProgramLoader { get; }
    }
}
