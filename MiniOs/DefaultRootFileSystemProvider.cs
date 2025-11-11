using System.Net.Http;
using System.Threading.Tasks;

namespace MiniOS
{
    /// <summary>
    /// Default adapter that keeps the historical <see cref="Rootfs"/> behavior behind the new interface.
    /// </summary>
    public sealed class DefaultRootFileSystemProvider : IRootFileSystemProvider
    {
        public static DefaultRootFileSystemProvider Instance { get; } = new();

        private DefaultRootFileSystemProvider()
        {
        }

        public Task PopulateAsync(IVirtualFileSystem vfs, HttpClient? httpClient = null)
            => Rootfs.MountAsync(vfs, httpClient);
    }
}
