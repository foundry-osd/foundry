// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.WinPe;
using Foundry.WinPE.Runtime;
using Serilog;
using Xunit;

namespace Foundry.WinPE.Tests.Runtime;

public sealed class RuntimeProvisioningIntegrationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("FoundryRuntimeProvisioning-").FullName;

    [Theory]
    [InlineData(WinPeArchitecture.X64, false)]
    [InlineData(WinPeArchitecture.X64, true)]
    [InlineData(WinPeArchitecture.Arm64, false)]
    [InlineData(WinPeArchitecture.Arm64, true)]
    public async Task ProvisionedMediaAuthenticatesTheWholePostInstallPayload(WinPeArchitecture architecture, bool usb)
    {
        string rid = architecture == WinPeArchitecture.X64 ? "win-x64" : "win-arm64";
        string archivePath = CreateArchive(rid);
        string image = Path.Combine(root, "Image");
        string bootRoot = Path.Combine(image, "Foundry");
        string cache = Path.Combine(root, "Usb");
        var service = new WinPeRuntimePayloadProvisioningService();
        var options = new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = Path.Combine(root, "Work"),
            MountedImagePath = image,
            IncludePayloadsInImage = !usb,
            PostInstall = new() { IsEnabled = true, ArchivePath = archivePath }
        };
        var prepared = await service.PrepareAsync(options, TestContext.Current.CancellationToken);
        Assert.True(prepared.IsSuccess, prepared.Error?.Details);
        var imageResult = await service.ProvisionAsync(prepared.Value!, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(imageResult.IsSuccess, imageResult.Error?.Details);
        if (usb)
        {
            Assert.False(Directory.Exists(Path.Combine(bootRoot, "Runtime", "Foundry.PostInstall")));
            var cacheResult = await service.ProvisionAsync(prepared.Value! with { MountedImagePath = "", UsbCacheRootPath = cache },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(cacheResult.IsSuccess, cacheResult.Error?.Details);
        }

        // Even an attached cache with no matching original cannot replace an ISO's boot-owned payload.
        string runtimeRoot = Path.Combine(cache, "Runtime");
        using var client = new HttpClient(new NoNetwork());
        using var logger = new LoggerConfiguration().CreateLogger();
        var resolver = new RuntimeResolver(bootRoot, runtimeRoot, rid, client, logger, getEnvironmentVariable: _ => null);
        string executable = await resolver.ResolveAsync("Foundry.PostInstall", true, TestContext.Current.CancellationToken);
        string payload = Path.GetDirectoryName(executable)!;
        Assert.Equal("trusted executable fixture", File.ReadAllText(executable));
        Assert.Equal("trusted DLL fixture", File.ReadAllText(Path.Combine(payload, "dependency.dll")));
        Assert.StartsWith(Path.Combine(bootRoot, "Execution") + Path.DirectorySeparatorChar, payload);
    }

    private string CreateArchive(string runtimeIdentifier)
    {
        string path = Path.Combine(root, "postinstall.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("Foundry.PostInstall.exe").Open())) writer.Write("trusted executable fixture");
        using (var writer = new StreamWriter(archive.CreateEntry("dependency.dll").Open())) writer.Write("trusted DLL fixture");
        using (var writer = new StreamWriter(archive.CreateEntry(PostInstallRuntimeManifest.FileName).Open()))
            writer.Write($"{{\"schemaVersion\":1,\"contractVersion\":1,\"runtimeIdentifier\":\"{runtimeIdentifier}\"}}");
        return path;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provisioned offline runtime must not use the network.");
    }
}
