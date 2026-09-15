// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.IO.Compression;
using Foundry.Bootstrap.Processes;
using Foundry.Bootstrap.Runtime;
using Foundry.Core.Services.WinPe;
using Serilog;
using Xunit;

namespace Foundry.Bootstrap.Tests.Runtime;

public sealed class RuntimeProvisioningIntegrationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("FoundryRuntimeProvisioning-").FullName;

    [Theory]
    [InlineData(WinPeArchitecture.X64, false)]
    [InlineData(WinPeArchitecture.X64, true)]
    [InlineData(WinPeArchitecture.Arm64, false)]
    [InlineData(WinPeArchitecture.Arm64, true)]
    public async Task ProvisionedMediaAuthenticatesTheWholePayloadThroughTheLaunchBoundary(WinPeArchitecture architecture, bool usb)
    {
        string archivePath = CreateArchive();
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
            Deploy = new() { IsEnabled = true, ArchivePath = archivePath }
        };
        var prepared = await service.PrepareAsync(options, TestContext.Current.CancellationToken);
        Assert.True(prepared.IsSuccess, prepared.Error?.Details);
        var imageResult = await service.ProvisionAsync(prepared.Value!, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(imageResult.IsSuccess, imageResult.Error?.Details);
        if (usb)
        {
            Assert.False(Directory.Exists(Path.Combine(bootRoot, "Runtime", "Foundry.Deploy")));
            var cacheResult = await service.ProvisionAsync(prepared.Value! with { MountedImagePath = "", UsbCacheRootPath = cache },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(cacheResult.IsSuccess, cacheResult.Error?.Details);
        }

        // Even an attached cache with no matching original cannot replace an ISO's boot-owned payload.
        string runtimeRoot = Path.Combine(cache, "Runtime");
        using var client = new HttpClient(new NoNetwork());
        using var logger = new LoggerConfiguration().CreateLogger();
        string rid = architecture == WinPeArchitecture.X64 ? "win-x64" : "win-arm64";
        var resolver = new RuntimeResolver(bootRoot, runtimeRoot, rid, client, logger, getEnvironmentVariable: _ => null);
        string executable = await resolver.ResolveAsync("Foundry.Deploy", true, TestContext.Current.CancellationToken);
        string payload = Path.GetDirectoryName(executable)!;
        Assert.Equal("trusted executable fixture", File.ReadAllText(executable));
        Assert.Equal("trusted DLL fixture", File.ReadAllText(Path.Combine(payload, "dependency.dll")));

        bool launchReached = false;
        var launcher = new ApplicationLauncher(logger, startProcess: start =>
        {
            launchReached = true;
            Assert.Equal(executable, start.FileName);
            Assert.StartsWith(Path.Combine(bootRoot, "Execution") + Path.DirectorySeparatorChar, start.WorkingDirectory);
            Assert.StartsWith(payload + Path.DirectorySeparatorChar, start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"]!);
            return Process.GetCurrentProcess();
        });
        var result = await launcher.StartDeployAsync(executable, new Dictionary<string, string?>(), TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.True(launchReached);
        Assert.True(File.Exists(executable));
    }

    private string CreateArchive()
    {
        string path = Path.Combine(root, "deploy.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("Foundry.Deploy.exe").Open())) writer.Write("trusted executable fixture");
        using (var writer = new StreamWriter(archive.CreateEntry("dependency.dll").Open())) writer.Write("trusted DLL fixture");
        return path;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provisioned offline runtime must not use the network.");
    }
}
