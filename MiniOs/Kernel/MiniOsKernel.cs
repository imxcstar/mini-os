using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    /// <summary>
    /// Runtime host that orchestrates bootstrapping the configured kernel services.
    /// </summary>
    public sealed class MiniOsKernel : IMiniOsKernel
    {
        private readonly Func<KernelServices, Shell> _shellFactory;
        private readonly IRootFileSystemProvider _rootfsProvider;

        public MiniOsKernel(
            KernelServices services,
            Func<KernelServices, Shell> shellFactory,
            IRootFileSystemProvider rootfsProvider)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            _shellFactory = shellFactory ?? throw new ArgumentNullException(nameof(shellFactory));
            _rootfsProvider = rootfsProvider ?? DefaultRootFileSystemProvider.Instance;
        }

        public KernelServices Services { get; }

        public async Task BootAsync(HttpClient? httpClient = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var terminal = Services.Terminal;
            terminal.WriteLine("MiniOS");
            terminal.WriteLine("Type `help` for commands.\n");

            await _rootfsProvider.PopulateAsync(Services.FileSystem, httpClient).ConfigureAwait(false);

            if (Services.SystemApi is ISystemApiHost host)
            {
                host.AttachRunner(Services.ProgramLoader);
            }

            var shell = _shellFactory(Services);
            await shell.RunAsync().ConfigureAwait(false);
        }
    }
}
