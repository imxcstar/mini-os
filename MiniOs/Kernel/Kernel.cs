
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public static class Kernel
    {
        private static readonly object _sync = new();
        private static readonly MiniOsKernelBuilder _builder = new();
        private static IMiniOsKernel? _kernel;

        public static IMiniOsKernel Instance => EnsureKernel();
        public static KernelServices Services => Instance.Services;

        public static Task BootAsync(HttpClient? httpClient = null, CancellationToken cancellationToken = default)
            => Instance.BootAsync(httpClient, cancellationToken);

        public static void Configure(Action<MiniOsKernelBuilder> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            lock (_sync)
            {
                configure(_builder);
                _kernel = _builder.Build();
            }
        }

        public static void UseTerminalPlatform(ITerminalPlatform platform)
        {
            if (platform is null) throw new ArgumentNullException(nameof(platform));
            Configure(builder => builder.UseTerminal(() => new Terminal(platform)));
        }

        private static IMiniOsKernel EnsureKernel()
        {
            lock (_sync)
            {
                _kernel ??= _builder.Build();
                return _kernel;
            }
        }
    }
}
