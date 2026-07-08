// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Net;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPePowerShellModuleProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_ForGalleryModule_DownloadsExtractsAndSkipsPackageMetadata()
    {
        using TempImage image = TempImage.Create();
        byte[] nupkg = CreateNupkg();
        var handler = new RoutingHandler { { "https://gallery.test/package/Pester/5.5.0", nupkg } };
        var service = new WinPePowerShellModuleProvisioningService(new HttpClient(handler));

        WinPeResult result = await service.ProvisionAsync(
            new WinPePowerShellModuleProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                CacheDirectoryPath = image.CachePath,
                GalleryBaseUri = "https://gallery.test/package",
                Modules =
                [
                    new PowerShellModuleSelection
                    {
                        Source = PowerShellModuleSource.Gallery,
                        Name = "Pester",
                        Version = "5.5.0"
                    }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string moduleRoot = Path.Combine(image.MountedImagePath, "Program Files", "WindowsPowerShell", "Modules", "Pester", "5.5.0");
        Assert.True(File.Exists(Path.Combine(moduleRoot, "Pester.psm1")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "Pester.nuspec")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot, "_rels")));
    }

    [Fact]
    public async Task ProvisionAsync_ForLocalModule_CopiesFolder()
    {
        using TempImage image = TempImage.Create();
        string localModule = Path.Combine(image.RootPath, "LocalMod");
        Directory.CreateDirectory(localModule);
        File.WriteAllText(Path.Combine(localModule, "LocalMod.psd1"), "manifest");

        var service = new WinPePowerShellModuleProvisioningService(new HttpClient(new RoutingHandler()));

        WinPeResult result = await service.ProvisionAsync(
            new WinPePowerShellModuleProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                CacheDirectoryPath = image.CachePath,
                Modules =
                [
                    new PowerShellModuleSelection
                    {
                        Source = PowerShellModuleSource.Local,
                        Name = "LocalMod",
                        LocalPath = localModule
                    }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(Path.Combine(image.MountedImagePath, "Program Files", "WindowsPowerShell", "Modules", "LocalMod", "LocalMod.psd1")));
    }

    private static byte[] CreateNupkg()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "Pester.psm1", "module");
            WriteEntry(archive, "Pester.nuspec", "<package/>");
            WriteEntry(archive, "[Content_Types].xml", "<Types/>");
            WriteEntry(archive, "_rels/.rels", "<Relationships/>");
            WriteEntry(archive, "package/services/metadata/core-properties/x.psmdcp", "<core/>");
        }

        return memory.ToArray();

        static void WriteEntry(ZipArchive archive, string name, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
    }

    private sealed class TempImage : IDisposable
    {
        private TempImage(string rootPath, string mountedImagePath)
        {
            RootPath = rootPath;
            MountedImagePath = mountedImagePath;
            CachePath = Path.Combine(rootPath, "cache");
        }

        public string RootPath { get; }
        public string MountedImagePath { get; }
        public string CachePath { get; }

        public static TempImage Create()
        {
            string root = Path.Combine(Path.GetTempPath(), $"foundry-psmod-{Guid.NewGuid():N}");
            string mount = Path.Combine(root, "mount");
            Directory.CreateDirectory(mount);
            Directory.CreateDirectory(Path.Combine(root, "cache"));
            return new TempImage(root, mount);
        }

        public void Dispose()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(RootPath, recursive: true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    private sealed class RoutingHandler : HttpMessageHandler, IEnumerable<KeyValuePair<string, byte[]>>
    {
        private readonly Dictionary<string, byte[]> _responses = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string url, byte[] content) => _responses[url] = content;

        public IEnumerator<KeyValuePair<string, byte[]>> GetEnumerator() => _responses.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _responses.GetEnumerator();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            return Task.FromResult(_responses.TryGetValue(url, out byte[]? content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
