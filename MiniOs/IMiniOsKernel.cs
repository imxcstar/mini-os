using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniOS
{
    public interface IMiniOsKernel
    {
        KernelServices Services { get; }
        Task BootAsync(HttpClient? httpClient = null, CancellationToken cancellationToken = default);
    }
}
