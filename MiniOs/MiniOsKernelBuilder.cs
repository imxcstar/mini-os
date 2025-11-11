using System;

namespace MiniOS
{
    /// <summary>
    /// Fluent builder that wires up the MiniOS kernel using the configured strategies.
    /// </summary>
    public sealed class MiniOsKernelBuilder
    {
        private Func<IVirtualFileSystem>? _fileSystemFactory;
        private Func<Terminal>? _terminalFactory;
        private Func<ProcessInputRouter>? _inputRouterFactory;
        private Func<KernelConstructionContext, IProcessScheduler>? _schedulerFactory;
        private Func<KernelConstructionContext, IProcessScheduler, ISysApi>? _sysApiFactory;
        private Func<KernelConstructionContext, IProcessScheduler, ISysApi, IProgramLoader>? _programLoaderFactory;
        private Func<KernelServices, Shell>? _shellFactory;
        private IRootFileSystemProvider _rootfsProvider = DefaultRootFileSystemProvider.Instance;

        public MiniOsKernelBuilder UseFileSystem(Func<IVirtualFileSystem> factory)
        {
            _fileSystemFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public MiniOsKernelBuilder UseTerminal(Func<Terminal> factory)
        {
            _terminalFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public MiniOsKernelBuilder UseInputRouter(Func<ProcessInputRouter> factory)
        {
            _inputRouterFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public MiniOsKernelBuilder UseRootFileSystemProvider(IRootFileSystemProvider provider)
        {
            _rootfsProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        public MiniOsKernelBuilder ConfigureScheduler(Func<KernelConstructionContext, IProcessScheduler> factory)
        {
            _schedulerFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public MiniOsKernelBuilder ConfigureSystemApi(Func<KernelConstructionContext, IProcessScheduler, ISysApi> factory)
        {
            _sysApiFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public MiniOsKernelBuilder ConfigureProgramLoader(Func<KernelConstructionContext, IProcessScheduler, ISysApi, IProgramLoader> factory)
        {
            _programLoaderFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public MiniOsKernelBuilder ConfigureShell(Func<KernelServices, Shell> factory)
        {
            _shellFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            return this;
        }

        public IMiniOsKernel Build()
        {
            var fileSystem = (_fileSystemFactory ?? (() => new Vfs()))();
            var terminal = (_terminalFactory ?? (() => new Terminal()))();
            var inputRouter = (_inputRouterFactory ?? (() => new ProcessInputRouter()))();
            var context = new KernelConstructionContext(fileSystem, terminal, inputRouter);

            var scheduler = (_schedulerFactory ?? DefaultSchedulerFactory)(context);
            var sysApi = (_sysApiFactory ?? DefaultSystemApiFactory)(context, scheduler);
            var loader = (_programLoaderFactory ?? DefaultProgramLoaderFactory)(context, scheduler, sysApi);
            var shellFactory = _shellFactory ?? DefaultShellFactory;

            var services = new KernelServices(fileSystem, terminal, inputRouter, scheduler, sysApi, loader);
            return new MiniOsKernel(services, shellFactory, _rootfsProvider);
        }

        private static IProcessScheduler DefaultSchedulerFactory(KernelConstructionContext context)
        {
            var cwd = context.FileSystem.GetCwd("/");
            return new Scheduler(context.InputRouter, context.Terminal, cwd);
        }

        private static ISysApi DefaultSystemApiFactory(KernelConstructionContext context, IProcessScheduler scheduler)
            => new Syscalls(context.FileSystem, scheduler, context.Terminal);

        private static IProgramLoader DefaultProgramLoaderFactory(
            KernelConstructionContext context,
            IProcessScheduler scheduler,
            ISysApi sysApi)
            => new ProgramLoader(context.FileSystem, scheduler, context.Terminal, sysApi);

        private static Shell DefaultShellFactory(KernelServices services)
            => new Shell(
                services.FileSystem,
                services.Scheduler,
                services.Terminal,
                services.ProgramLoader,
                services.InputRouter);
    }

    public readonly record struct KernelConstructionContext(
        IVirtualFileSystem FileSystem,
        Terminal Terminal,
        ProcessInputRouter InputRouter);
}
