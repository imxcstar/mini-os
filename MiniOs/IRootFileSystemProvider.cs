using System.Net.Http;
using System.Threading.Tasks;

namespace MiniOS
{
    public interface IRootFileSystemProvider
    {
        Task PopulateAsync(IVirtualFileSystem vfs, HttpClient? httpClient = null);
    }
}
