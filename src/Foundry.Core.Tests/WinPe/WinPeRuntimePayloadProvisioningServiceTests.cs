// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Net;
using System.Reflection.PortableExecutable;
using Foundry.Core.Tests.TestUtilities;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeRuntimePayloadProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_WhenDebugArchivesAreProvided_ExtractsToNormalizedIsoAndUsbRuntimeRoots()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe");
        string deployArchivePath = workspace.CreateArchive("deploy.zip", "Foundry.Deploy.exe");
        string legacyConnectSeedPath = Path.Combine(workspace.MountedImagePath, "Foundry", "Seed", "Foundry.Connect.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConnectSeedPath)!);
        File.WriteAllText(legacyConnectSeedPath, "legacy");

        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                UsbCacheRootPath = workspace.UsbCacheRootPath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath
                },
                Deploy = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = deployArchivePath
                }
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64", "Foundry.Connect.exe")));
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Deploy", "win-x64", "Foundry.Deploy.exe")));
        Assert.True(File.Exists(Path.Combine(workspace.UsbCacheRootPath, "Runtime", "Foundry.Connect", "win-x64", "Foundry.Connect.exe")));
        Assert.True(File.Exists(Path.Combine(workspace.UsbCacheRootPath, "Runtime", "Foundry.Deploy", "win-x64", "Foundry.Deploy.exe")));
        Assert.False(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Seed", "Foundry.Connect.zip")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenArchiveAndProjectAreProvided_PrefersArchive()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe");
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.Connect", "Foundry.Connect.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner();

        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath,
                    ProjectPath = projectPath
                }
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Empty(runner.Executions);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64", "Foundry.Connect.exe")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenProjectIsProvided_PublishesSingleFileBeforeProvisioning()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.Deploy", "Foundry.Deploy.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner();

        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.Arm64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                Deploy = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ProjectPath = projectPath
                }
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        WinPeProcessExecution execution = Assert.Single(runner.Executions);
        Assert.Equal("dotnet", execution.FileName);
        Assert.Contains("publish", execution.Arguments);
        Assert.Contains("-r win-arm64", execution.Arguments);
        Assert.Contains("/p:PublishSingleFile=true", execution.Arguments);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Deploy", "win-arm64", "Foundry.Deploy.exe")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenConnectProjectIsProvided_PublishesSingleFileBeforeProvisioning()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.Connect", "Foundry.Connect.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner();

        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ProjectPath = projectPath
                }
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        WinPeProcessExecution execution = Assert.Single(runner.Executions);
        Assert.Equal("dotnet", execution.FileName);
        Assert.Contains("publish", execution.Arguments);
        Assert.Contains("-r win-x64", execution.Arguments);
        Assert.Contains("/p:PublishSingleFile=true", execution.Arguments);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64", "Foundry.Connect.exe")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenReleaseConnectIsEnabled_DownloadsAndExtractsReleaseAsset()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] archiveBytes = await File.ReadAllBytesAsync(workspace.CreateArchive("connect-release.zip", "Foundry.Connect.exe"), TestContext.Current.CancellationToken);
        var httpHandler = new FakeReleaseHttpMessageHandler("Foundry.Connect-win-x64.zip", archiveBytes);
        var service = new WinPeRuntimePayloadProvisioningService(
            new FakeRuntimeProcessRunner(),
            new HttpClient(httpHandler));
        var progress = new CapturingProgress<WinPeDownloadProgress>();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ProvisioningSource = WinPeProvisioningSource.Release
                }
            },
            progress,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Contains(FakeReleaseHttpMessageHandler.LatestReleaseUri, httpHandler.RequestUris);
        Assert.Contains(FakeReleaseHttpMessageHandler.DownloadUri, httpHandler.RequestUris);
        Assert.Contains(progress.Items, item => item is { Percent: 0, Status: "Downloading Foundry.Connect runtime payload." });
        Assert.Contains(progress.Items, item => item.Percent == 100 && item.Status.Contains("Foundry.Connect", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64", "Foundry.Connect.exe")));
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Deploy")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenApplicationIsDisabled_DoesNotCreateRuntimeRoot()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();

        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                UsbCacheRootPath = workspace.UsbCacheRootPath
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect")));
        Assert.False(Directory.Exists(Path.Combine(workspace.UsbCacheRootPath, "Runtime", "Foundry.Deploy")));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("sha1:abcd", true)]
    [InlineData("sha256:bad", true)]
    [InlineData(null, false)]
    public async Task ProvisionAsync_WhenReleaseIntegrityIsMissingOrMalformed_DoesNotTouchDestination(string? digest, bool includeSize)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] archive = await File.ReadAllBytesAsync(workspace.CreateArchive("release.zip", "Foundry.Connect.exe"), TestContext.Current.CancellationToken);
        var handler = new FakeReleaseHttpMessageHandler("Foundry.Connect-win-x64.zip", archive, digest, includeSize);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));
        string destination = Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64");
        Directory.CreateDirectory(destination);
        string sentinel = Path.Combine(destination, "existing.txt");
        await File.WriteAllTextAsync(sentinel, "keep", TestContext.Current.CancellationToken);

        WinPeResult result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(FakeReleaseHttpMessageHandler.DownloadUri, handler.RequestUris);
    }
    [Fact]
    public async Task PrepareAsync_SealsFilesAndReusesOneAcquisitionForBothDestinations()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] archive = await File.ReadAllBytesAsync(workspace.CreateArchive("prepared.zip", "Foundry.Connect.exe"), TestContext.Current.CancellationToken);
        var handler = new FakeReleaseHttpMessageHandler("Foundry.Connect-win-x64.zip", archive);
        var runner = new FakeRuntimeProcessRunner();
        var service = new WinPeRuntimePayloadProvisioningService(runner, new HttpClient(handler));
        var options = new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        };

        WinPeResult<WinPePreparedRuntimePayloads> result = await service.PrepareAsync(options, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Details);
        using WinPePreparedRuntimePayloads prepared = result.Value!;
        WinPePreparedRuntimeApplication application = Assert.Single(prepared.Applications);
        WinPeRuntimeFile file = Assert.Single(application.Files);
        string stagedFile = Path.Combine(application.DirectoryPath, file.RelativePath);
        Assert.Equal(new FileInfo(stagedFile).Length, file.Length);
        Assert.Equal("v1.0.0", application.ReleaseTag);
        Assert.Throws<IOException>(() => File.WriteAllText(stagedFile, "changed"));
        Assert.Throws<IOException>(() => File.Delete(stagedFile));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.MountedImagePath));

        WinPeResult mounted = await service.ProvisionPreparedAsync(prepared, options with { MountedImagePath = workspace.MountedImagePath }, TestContext.Current.CancellationToken);
        WinPeResult cached = await service.ProvisionPreparedAsync(prepared, options with { UsbCacheRootPath = workspace.UsbCacheRootPath }, TestContext.Current.CancellationToken);

        Assert.True(mounted.IsSuccess, mounted.Error?.Details);
        Assert.True(cached.IsSuccess, cached.Error?.Details);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Empty(runner.Executions);
        string mountedFile = Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", application.ApplicationName, application.RuntimeIdentifier, file.RelativePath);
        string cachedFile = Path.Combine(workspace.UsbCacheRootPath, "Runtime", application.ApplicationName, application.RuntimeIdentifier, file.RelativePath);
        Assert.Equal(await File.ReadAllBytesAsync(mountedFile, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(cachedFile, TestContext.Current.CancellationToken));
        prepared.Dispose();
        Assert.False(Directory.Exists(application.DirectoryPath));
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "prepared.zip")));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("unsafe")]
    [InlineData("architecture")]
    public async Task ProvisionPreparedAsync_RejectsChangedFileSetBeforeReplacingDestination(string change)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string source = Path.Combine(workspace.RootPath, "runtime");
        Directory.CreateDirectory(source);
        PortableExecutableFixture.Write(Path.Combine(source, "Foundry.Connect.exe"), Machine.Amd64);
        string libraryPath = Path.Combine(source, "native.dll");
        PortableExecutableFixture.Write(libraryPath, Machine.Amd64);
        WinPeRuntimeFile[] files = Directory.GetFiles(source).Select(path => new WinPeRuntimeFile(
            Path.GetFileName(path), new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))).ToArray();
        if (change == "changed")
        {
            byte[] bytes = await File.ReadAllBytesAsync(libraryPath, TestContext.Current.CancellationToken);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(libraryPath, bytes, TestContext.Current.CancellationToken);
        }
        else if (change == "missing")
        {
            File.Delete(libraryPath);
        }
        else if (change == "extra")
        {
            await File.WriteAllTextAsync(Path.Combine(source, "loader.runtimeconfig.json"), "{}", TestContext.Current.CancellationToken);
        }
        else if (change == "unsafe")
        {
            files[0] = files[0] with { RelativePath = "../outside.exe" };
        }

        using var prepared = new WinPePreparedRuntimePayloads(Guid.NewGuid(),
            [new("Foundry.Connect", "win-x64", source, WinPeProvisioningSource.Debug, null, null, files)]);
        string destination = Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64");
        Directory.CreateDirectory(destination);
        string sentinel = Path.Combine(destination, "existing.txt");
        await File.WriteAllTextAsync(sentinel, "keep", TestContext.Current.CancellationToken);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionPreparedAsync(prepared, new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            Architecture = change == "architecture" ? WinPeArchitecture.Arm64 : WinPeArchitecture.X64
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../outside.txt", false)]
    [InlineData("/absolute.txt", false)]
    [InlineData("Foundry.Connect.exe", false)]
    [InlineData("foundry.connect.EXE", false)]
    [InlineData("linked.dll", true)]
    public async Task PrepareAsync_RejectsUnsafeArchiveEntries(string entryName, bool link)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string archivePath = workspace.CreateArchive("unsafe.zip", "Foundry.Connect.exe");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName);
            if (link)
            {
                entry.ExternalAttributes = 0xA000 << 16;
            }
        }

        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());
        WinPeResult<WinPePreparedRuntimePayloads> result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Connect = new() { IsEnabled = true, ArchivePath = archivePath }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.MountedImagePath));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(workspace.WorkingDirectoryPath, "RuntimePayloads")));
    }

    [Fact]
    public async Task PrepareAsync_RejectsWrongArchitectureBeforeReturningPreparedPayload()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());
        WinPeResult<WinPePreparedRuntimePayloads> result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Architecture = WinPeArchitecture.Arm64,
            Connect = new() { IsEnabled = true, ArchivePath = workspace.CreateArchive("x64.zip", "Foundry.Connect.exe") }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.MountedImagePath));
    }
    [Fact]
    public async Task ProvisionAsync_WhenSecondApplicationFails_PreservesFirstApplicationDestination()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string destination = Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64");
        Directory.CreateDirectory(destination);
        string sentinel = Path.Combine(destination, "existing.txt");
        await File.WriteAllTextAsync(sentinel, "keep", TestContext.Current.CancellationToken);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            Connect = new() { IsEnabled = true, ArchivePath = workspace.CreateArchive("valid.zip", "Foundry.Connect.exe") },
            Deploy = new() { IsEnabled = true, ArchivePath = workspace.CreateArchive("invalid.zip", "Wrong.exe") }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(workspace.WorkingDirectoryPath, "RuntimePayloads")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("digest")]
    [InlineData("size")]
    public async Task PrepareAsync_WhenReleaseAcquisitionIsInvalid_ReturnsNoPreparedPayload(string failure)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] archive = await File.ReadAllBytesAsync(workspace.CreateArchive("valid.zip", "Foundry.Connect.exe"), TestContext.Current.CancellationToken);
        var handler = new FakeReleaseHttpMessageHandler(
            failure == "missing" ? "Foundry.Deploy-win-x64.zip" : "Foundry.Connect-win-x64.zip", archive,
            failure == "digest" ? "sha256:" + new string('0', 64) : null, sizeOffset: failure == "size" ? 1 : 0);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));

        WinPeResult<WinPePreparedRuntimePayloads> result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(workspace.WorkingDirectoryPath, "RuntimePayloads")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.MountedImagePath));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task PrepareAsync_ValidatesNativeDependenciesAndAllowsPortableManagedAssemblies(bool managed, bool expectedSuccess)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string source = Path.Combine(workspace.RootPath, "libraries");
        PortableExecutableFixture.Write(Path.Combine(source, "Foundry.Connect.exe"), Machine.Amd64);
        PortableExecutableFixture.Write(Path.Combine(source, "dependency.dll"), Machine.I386, managed);
        string archivePath = Path.Combine(workspace.RootPath, "libraries.zip");
        ZipFile.CreateFromDirectory(source, archivePath);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult<WinPePreparedRuntimePayloads> result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Connect = new() { IsEnabled = true, ArchivePath = archivePath }
        }, cancellationToken: TestContext.Current.CancellationToken);
        using WinPePreparedRuntimePayloads? prepared = result.Value;

        Assert.Equal(expectedSuccess, result.IsSuccess);
    }
    [Fact]
    public async Task PrepareAsync_RejectsOversizedMetadataBeforeReadingItsBody()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        using var content = new ByteArrayContent("{}"u8.ToArray());
        content.Headers.ContentLength = 32L * 1024 * 1024 + 1;
        var handler = new SingleResponseHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), handler);

        WinPeResult<WinPePreparedRuntimePayloads> result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("permitted size", result.Error!.Details);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task PrepareAsync_RejectsHttpsDowngradeBeforeSendingRedirectedRequest()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri("http://example.test/release.json");
        var handler = new SingleResponseHandler(response);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), handler);

        WinPeResult<WinPePreparedRuntimePayloads> result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("HTTPS", result.Error!.Details);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount > 1) { throw new InvalidOperationException("An unexpected follow-up request was sent."); }
            return Task.FromResult(response);
        }
    }
    private sealed class FakeReleaseHttpMessageHandler(string assetName, byte[] archiveBytes, string? digestOverride = null, bool includeSize = true, int sizeOffset = 0) : HttpMessageHandler
    {
        public const string LatestReleaseUri = "https://api.github.com/repos/foundry-osd/foundry/releases/latest";
        public const string DownloadUri = "https://example.test/Foundry.Connect-win-x64.zip";

        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string requestUri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestUris.Add(requestUri);

            if (string.Equals(requestUri, LatestReleaseUri, StringComparison.Ordinal))
            {
                string digest = digestOverride ?? "sha256:" + Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
                string json = $$"""
                    {
                      "tag_name": "v1.0.0",
                      "assets": [
                        {
                          "name": "{{assetName}}",
                          "browser_download_url": "{{DownloadUri}}",
                          "digest": "{{digest}}",
                          "size": {{(includeSize ? (archiveBytes.Length + sizeOffset).ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}}
                        }
                      ]
                    }
                    """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            if (string.Equals(requestUri, DownloadUri, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archiveBytes)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class CapturingProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public void Report(T value)
        {
            Items.Add(value);
        }
    }

    private sealed class FakeRuntimeProcessRunner : IWinPeProcessRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null,
            TimeSpan? executionTimeout = null)
        {
            throw new NotSupportedException("Executable calls must pass argument tokens.");
        }

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            IReadOnlyList<string> argumentList,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null,
            TimeSpan? executionTimeout = null)
        {
            string arguments = string.Join(' ', argumentList);
            string outputDirectory = argumentList[argumentList.ToList().IndexOf("-o") + 1];
            Directory.CreateDirectory(outputDirectory);
            string executableName = arguments.Contains("Foundry.Connect.csproj", StringComparison.OrdinalIgnoreCase)
                ? "Foundry.Connect.exe"
                : "Foundry.Deploy.exe";
            PortableExecutableFixture.Write(Path.Combine(outputDirectory, executableName), arguments.Contains("win-arm64", StringComparison.Ordinal) ? Machine.Arm64 : Machine.Amd64);

            var execution = new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            };

            Executions.Add(execution);
            return Task.FromResult(execution);
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            TimeSpan? executionTimeout = null)
        {
            throw new NotSupportedException();
        }

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            TimeSpan? executionTimeout = null)
        {
            throw new NotSupportedException();
        }

    }

    private sealed class TempRuntimeWorkspace : IDisposable
    {
        private TempRuntimeWorkspace(string rootPath)
        {
            RootPath = rootPath;
            WorkingDirectoryPath = Path.Combine(rootPath, "work");
            MountedImagePath = Path.Combine(rootPath, "mount");
            UsbCacheRootPath = Path.Combine(rootPath, "usb-cache");
            Directory.CreateDirectory(WorkingDirectoryPath);
            Directory.CreateDirectory(MountedImagePath);
            Directory.CreateDirectory(UsbCacheRootPath);
        }

        public string RootPath { get; }
        public string WorkingDirectoryPath { get; }
        public string MountedImagePath { get; }
        public string UsbCacheRootPath { get; }

        public static TempRuntimeWorkspace Create()
        {
            return new TempRuntimeWorkspace(Path.Combine(Path.GetTempPath(), $"foundry-runtime-{Guid.NewGuid():N}"));
        }

        public string CreateArchive(string archiveName, string executableName)
        {
            string payloadPath = Path.Combine(RootPath, "payloads", Path.GetFileNameWithoutExtension(archiveName));
            Directory.CreateDirectory(payloadPath);
            PortableExecutableFixture.Write(Path.Combine(payloadPath, executableName), Machine.Amd64);

            string archivePath = Path.Combine(RootPath, archiveName);
            ZipFile.CreateFromDirectory(payloadPath, archivePath);
            return archivePath;
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
