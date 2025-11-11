using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MiniOS
{
    public static class Rootfs
    {
        private const string EmbeddedResourcePrefix = "rootfs/";

        public static async Task MountAsync(IVirtualFileSystem vfs, HttpClient? httpClient = null)
        {
            if (vfs is null) throw new ArgumentNullException(nameof(vfs));

            // ✅ Blazor WASM 环境：从 wwwroot/rootfs 加载
            if (OperatingSystem.IsBrowser() && httpClient is not null)
            {
                if (await TryMountFromHttpAsync(vfs, httpClient))
                    return;
            }

            // ✅ 普通 .NET 环境：优先从本地 rootfs 目录加载
            if (TryMountFromHost(vfs))
                return;

            // ✅ 如果本地不可用，则尝试从程序集资源加载
            if (TryMountFromEmbedded(vfs))
                return;

            throw new InvalidOperationException("rootfs not found (neither host, embedded, nor wasm http).");
        }

        private static async Task<bool> TryMountFromHttpAsync(IVirtualFileSystem vfs, HttpClient httpClient)
        {
            try
            {
                vfs.EnsureDirectory("/");

                // 约定在 wwwroot 下有 rootfs.manifest 文件，列出资源清单（路径列表）
                var manifestUrl = "rootfs/manifest.txt";
                var manifest = await httpClient.GetStringAsync(manifestUrl);

                var files = manifest
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .OrderBy(l => l, StringComparer.Ordinal);

                foreach (var relative in files)
                {
                    var targetPath = Normalize(relative);
                    if (string.IsNullOrEmpty(targetPath)) continue;

                    // 确保目录存在
                    var slashIndex = targetPath.LastIndexOf('/');
                    if (slashIndex >= 0)
                    {
                        var dirPath = targetPath[..slashIndex];
                        vfs.EnsureDirectory("/" + dirPath);
                    }

                    // 下载文件内容
                    var bytes = await httpClient.GetByteArrayAsync("rootfs/" + relative);
                    vfs.WriteAllBytes("/" + targetPath, bytes);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Rootfs] HTTP mount failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryMountFromHost(IVirtualFileSystem vfs)
        {
            try
            {
                var hostPath = LocateOnHost();
                Populate(vfs, hostPath);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryMountFromEmbedded(IVirtualFileSystem vfs)
        {
            var assembly = typeof(Rootfs).Assembly;
            var resources = assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(EmbeddedResourcePrefix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (resources.Length == 0)
                return false;

            vfs.EnsureDirectory("/");

            foreach (var resourceName in resources)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                    continue;

                var relative = resourceName[EmbeddedResourcePrefix.Length..].Replace('\\', '/');
                var targetPath = Normalize(relative);
                if (string.IsNullOrEmpty(targetPath))
                    continue;

                var slashIndex = targetPath.LastIndexOf('/');
                if (slashIndex >= 0)
                {
                    var directoryPath = targetPath[..slashIndex];
                    if (!string.IsNullOrEmpty(directoryPath))
                        vfs.EnsureDirectory("/" + directoryPath);
                }

                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                vfs.WriteAllBytes("/" + targetPath, ms.ToArray());
            }

            return true;
        }

        public static void Populate(IVirtualFileSystem vfs, string hostRoot)
        {
            if (!Directory.Exists(hostRoot))
                throw new InvalidOperationException($"rootfs '{hostRoot}' does not exist");

            vfs.EnsureDirectory("/");

            var directories = Directory.GetDirectories(hostRoot, "*", SearchOption.AllDirectories)
                .OrderBy(d => d, StringComparer.Ordinal);
            foreach (var dir in directories)
            {
                var rel = Path.GetRelativePath(hostRoot, dir);
                var targetPath = Normalize(rel);
                if (string.IsNullOrEmpty(targetPath)) continue;
                vfs.EnsureDirectory("/" + targetPath);
            }

            var files = Directory.GetFiles(hostRoot, "*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(hostRoot, file);
                var targetFile = Normalize(rel);
                if (string.IsNullOrEmpty(targetFile)) continue;
                var bytes = File.ReadAllBytes(file);
                vfs.WriteAllBytes("/" + targetFile, bytes);
            }
        }

        public static string LocateOnHost()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "rootfs"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "rootfs"))
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }
            throw new InvalidOperationException("rootfs directory not found. Build artifacts may be missing.");
        }

        private static string Normalize(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
                return string.Empty;
            var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            if (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized[2..];
            return normalized.TrimStart('/');
        }
    }
}
