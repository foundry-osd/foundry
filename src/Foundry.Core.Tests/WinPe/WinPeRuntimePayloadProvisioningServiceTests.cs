// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Runtime;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeRuntimePayloadProvisioningServiceTests
{
    private const string TestReleaseVersion = "26.11.2.1";

    [Fact]
    public async Task Provision_ReadsTheReleaseOfTheFoundryVersion()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", File.ReadAllBytes(workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe")));
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);

        WinPeResult result = await service.ProvisionAsync(CreateReleaseWinPeOptions(workspace, TestReleaseVersion), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("https://api.github.com/repos/foundry-osd/foundry/releases/tags/v26.11.2.1", handler.RequestUris[0]);
        Assert.DoesNotContain(handler.RequestUris, uri => uri.Contains("/latest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Provision_UnpublishedVersion_ReadsLatest()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", File.ReadAllBytes(workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe")));
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);

        WinPeResult result = await service.ProvisionAsync(CreateReleaseWinPeOptions(workspace, FoundryReleaseTag.UnpublishedVersion), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(FakeReleaseHttpMessageHandler.LatestReleaseUri, handler.RequestUris[0]);
    }

    [Fact]
    public async Task Provision_ReleaseTagOverride_Wins()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", File.ReadAllBytes(workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe")));
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler),
            name => name == FoundryReleaseTag.ReleaseTagVariable ? "v26.10.18.1" : null);

        WinPeResult result = await service.ProvisionAsync(CreateReleaseWinPeOptions(workspace, TestReleaseVersion), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("https://api.github.com/repos/foundry-osd/foundry/releases/tags/v26.10.18.1", handler.RequestUris[0]);
    }

    [Fact]
    public async Task Provision_WithoutReleaseVersion_FailsBeforeReadingAnyRelease()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", File.ReadAllBytes(workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe")));
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);

        WinPeResult result = await service.ProvisionAsync(CreateReleaseWinPeOptions(workspace, string.Empty), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("ReleaseVersion", result.Error.Details, StringComparison.Ordinal);
        Assert.Empty(handler.RequestUris);
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry")));
    }

    private static WinPeRuntimePayloadProvisioningOptions CreateReleaseWinPeOptions(TempRuntimeWorkspace workspace, string releaseVersion) => new()
    {
        WorkingDirectoryPath = workspace.WorkingDirectoryPath,
        PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
        MountedImagePath = workspace.MountedImagePath,
        ReleaseVersion = releaseVersion,
        WinPe = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
    };

    private static string? NoEnvironment(string _) => null;

    [Theory]
    [InlineData("missing", false)]
    [InlineData("duplicate", false)]
    [InlineData("oversize", false)]
    public async Task PostInstallManifestMustBePresentUniqueAndBounded(string shape, bool accepted)
    {
        using var workspace = TempRuntimeWorkspace.Create();
        string path = workspace.CreateArchive("postinstall.zip", "Foundry.PostInstall.exe");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            if (shape != "duplicate") archive.GetEntry("foundry.postinstall.json")!.Delete();
            if (shape != "missing")
            {
                using var writer = new StreamWriter(archive.CreateEntry(shape == "duplicate" ? "FOUNDRY.POSTINSTALL.JSON" : "foundry.postinstall.json").Open());
                writer.Write(shape == "oversize" ? new string('x', 65537) : "{\"schemaVersion\":1,\"runtimeIdentifier\":\"win-x64\"}");
            }
        }
        var result = await new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()).PrepareAsync(new()
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = path }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(accepted, result.IsSuccess);
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry")));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task PreparedArchiveRequiresSupportedPostInstallContract(int contract, bool accepted)
    {
        using var workspace = TempRuntimeWorkspace.Create();
        string archive = workspace.CreateArchive("postinstall.zip", "Foundry.PostInstall.exe", contract: contract);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());
        var result = await service.PrepareAsync(new()
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = archive }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(accepted, result.IsSuccess);
    }

    [Fact]
    public async Task ProvisionAsync_WithoutMountedImage_FailsValidation()
    {
        using var workspace = TempRuntimeWorkspace.Create();
        var result = await new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()).ProvisionAsync(new()
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreateArchive("postinstall.zip", "Foundry.PostInstall.exe") }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("MountedImagePath", result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_WithDisabledPostInstall_FailsValidation()
    {
        using var workspace = TempRuntimeWorkspace.Create();
        var result = await new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()).PrepareAsync(new()
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = false }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("PostInstall.IsEnabled", result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_WhenCancelledDuringLocalPublish_WaitsForPublisherAndSkipsArchive()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "Foundry.WinPE.csproj");
        File.WriteAllText(projectPath, "<Project />");
        using var cancellation = new CancellationTokenSource();
        var runner = new FakeRuntimeProcessRunner
        {
            OnRun = token =>
            {
                cancellation.Cancel();
                Assert.False(token.CanBeCanceled);
            }
        };
        var service = new WinPeRuntimePayloadProvisioningService(runner);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            WinPe = new() { IsEnabled = true, ProjectPath = projectPath }
        }, cancellation.Token));

        Assert.Single(runner.Executions);
        Assert.Empty(Directory.GetFiles(workspace.WorkingDirectoryPath, "*.zip", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "win-x64", false)]
    [InlineData(WinPeArchitecture.X64, "win-x64", true)]
    [InlineData(WinPeArchitecture.Arm64, "win-arm64", false)]
    [InlineData(WinPeArchitecture.Arm64, "win-arm64", true)]
    public async Task ProvisionAsync_ExtractsOnlyThePostInstallLaunchFilesIntoTheBootImage(WinPeArchitecture architecture, string runtime, bool release)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string archive = workspace.CreateArchive("postinstall.zip", "Foundry.PostInstall.exe", runtimeIdentifier: runtime);
        var handler = new FakeReleaseHttpMessageHandler($"Foundry.PostInstall-{runtime}.zip", File.ReadAllBytes(archive));
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);
        string foundryRoot = Path.Combine(workspace.MountedImagePath, "Foundry");
        Directory.CreateDirectory(Path.Combine(foundryRoot, "Config"));
        File.WriteAllText(Path.Combine(foundryRoot, "Config", "foundry.runtime-trust.json"), "{}");

        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            ReleaseVersion = TestReleaseVersion,
            PostInstall = release
                ? new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
                : new() { IsEnabled = true, ArchivePath = archive }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(release, handler.RequestUris.Count > 0);
        string postInstallRoot = Path.Combine(foundryRoot, WinPeWorkspaceLayout.PostInstallDirectoryName);
        Assert.Equal(["Foundry.PostInstall.exe", "foundry.postinstall.json", "Launch.cmd"],
            Directory.EnumerateFileSystemEntries(postInstallRoot).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("Foundry.PostInstall.exe", File.ReadAllText(Path.Combine(postInstallRoot, "Foundry.PostInstall.exe")));
        Assert.False(Directory.Exists(Path.Combine(foundryRoot, "Runtime")));
        Assert.False(File.Exists(Path.Combine(foundryRoot, "Config", "foundry.runtime-trust.json")));
        Assert.False(Directory.Exists(Path.Combine(foundryRoot, WinPeWorkspaceLayout.AutopilotInteractiveDirectoryName)));
    }

    [Theory]
    [InlineData("hash", "PostInstall runtime file 'Launch.cmd' does not match its manifest.")]
    [InlineData("missing-launch", "PostInstall runtime archive must contain exactly one 'Launch.cmd' at its root.")]
    [InlineData("unlisted-file", "PostInstall runtime metadata must list exactly the executable and Launch.cmd.")]
    public async Task ProvisionAsync_WhenPostInstallFilesDoNotMatchTheManifest_Fails(string defect, string expectedDetail)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string archive = workspace.CreateArchive("postinstall.zip", "Foundry.PostInstall.exe");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            if (defect == "hash")
            {
                zip.GetEntry("Launch.cmd")!.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("Launch.cmd").Open());
                writer.Write("tampered");
            }
            else if (defect == "missing-launch") zip.GetEntry("Launch.cmd")!.Delete();
            else
            {
                ZipArchiveEntry manifest = zip.GetEntry("foundry.postinstall.json")!;
                string json;
                using (var reader = new StreamReader(manifest.Open())) json = reader.ReadToEnd();
                manifest.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("foundry.postinstall.json").Open());
                writer.Write(json.Replace("\"files\":[", "\"files\":[{\"relativePath\":\"dependency.dll\",\"length\":18,\"sha256\":\"" + new string('0', 64) + "\"},", StringComparison.Ordinal));
            }
        }

        var result = await new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()).ProvisionAsync(new()
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            PostInstall = new() { IsEnabled = true, ArchivePath = archive }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.BuildFailed, result.Error!.Code);
        Assert.Equal(expectedDetail, result.Error.Details);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Provision_Throws_WhenAutopilotInteractiveEnabledWithoutLocalSource(bool releaseSource, bool withProject)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "Foundry.AutopilotInteractive.csproj");
        File.WriteAllText(projectPath, "<Project />");
        var handler = new FakeReleaseHttpMessageHandler("unused", []);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));

        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            MountedImagePath = workspace.MountedImagePath,
            AutopilotInteractive = new()
            {
                IsEnabled = true,
                ProvisioningSource = releaseSource ? WinPeProvisioningSource.Release : WinPeProvisioningSource.Debug,
                ProjectPath = withProject ? projectPath : string.Empty
            }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("only provisioned on debug media", result.Error!.Details, StringComparison.Ordinal);
        Assert.Empty(handler.RequestUris);
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry")));
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "win-x64")]
    [InlineData(WinPeArchitecture.Arm64, "win-arm64")]
    public async Task Provision_WritesTheDebugAutopilotInteractiveArchiveIntoTheBootImage(WinPeArchitecture architecture, string runtime)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string archive = workspace.CreateArchive("autopilot.zip", "Foundry.AutopilotInteractive.exe", runtimeIdentifier: runtime);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive(architecture) },
            MountedImagePath = workspace.MountedImagePath,
            AutopilotInteractive = new() { IsEnabled = true, ArchivePath = archive }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string imageArchive = Path.Combine(workspace.MountedImagePath, "Foundry", WinPeWorkspaceLayout.AutopilotInteractiveDirectoryName,
            WinPeWorkspaceLayout.AutopilotInteractiveArchiveFileName);
        Assert.Equal(File.ReadAllBytes(archive), File.ReadAllBytes(imageArchive));
        Assert.Single(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(imageArchive)!));
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime")));
        Assert.False(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Config", "foundry.runtime-trust.json")));
    }

    [Fact]
    public async Task Provision_PublishesAutopilotInteractiveProject_WithExecutableInArchive()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.AutopilotInteractive", "Foundry.AutopilotInteractive.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner();

        var result = await new WinPeRuntimePayloadProvisioningService(runner).ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            MountedImagePath = workspace.MountedImagePath,
            AutopilotInteractive = new() { IsEnabled = true, ProjectPath = projectPath }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Contains("PublishSingleFile=true", Assert.Single(runner.Executions).Arguments);
        using ZipArchive stored = ZipFile.OpenRead(Path.Combine(workspace.MountedImagePath, "Foundry",
            WinPeWorkspaceLayout.AutopilotInteractiveDirectoryName, WinPeWorkspaceLayout.AutopilotInteractiveArchiveFileName));
        Assert.Single(stored.Entries, entry => entry.FullName == "Foundry.AutopilotInteractive.exe");
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "win-x64")]
    [InlineData(WinPeArchitecture.Arm64, "win-arm64")]
    public async Task ReleaseProvisioning_NeverIncludesAutopilotInteractive(WinPeArchitecture architecture, string runtime)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] winPe = File.ReadAllBytes(workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe"));
        var handler = new FakeReleaseHttpMessageHandler($"Foundry.WinPE-{runtime}.zip", winPe);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);
        var options = new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive(architecture) },
            MountedImagePath = workspace.MountedImagePath,
            ReleaseVersion = TestReleaseVersion,
            WinPe = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        };

        Assert.False(options.AutopilotInteractive.IsEnabled);
        var result = await service.ProvisionAsync(options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.DoesNotContain(handler.RequestUris, uri => uri.Contains("AutopilotInteractive", StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", WinPeWorkspaceLayout.AutopilotInteractiveDirectoryName)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("md5:abcd")]
    [InlineData("sha256:abcd")]
    [InlineData("sha256:gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task PrepareAsync_WhenReleaseDigestIsInvalid_RejectsBeforeDownloading(string? digest)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] bytes = File.ReadAllBytes(workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe"));
        var handler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", bytes) { OverrideDigest = true, Digest = digest };
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);
        var result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            ReleaseVersion = TestReleaseVersion,
            WinPe = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Contains("SHA256", result.Error!.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FakeReleaseHttpMessageHandler.ReleaseUri, Assert.Single(handler.RequestUris));
    }

    [Fact]
    public async Task PrepareAsync_WhenWinPeArchiveHasNoExecutableAtTheRoot_Rejects()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string archive = workspace.CreateArchive("winpe.zip", "Foundry.Bootstrap.exe");
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());
        var result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            WinPe = new() { IsEnabled = true, ArchivePath = archive }
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Contains("Foundry.WinPE.exe", result.Error!.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_WhenReleaseDigestDoesNotMatch_RejectsWithoutProvisioning()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] bytes = File.ReadAllBytes(workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe"));
        var handler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", bytes)
        {
            OverrideDigest = true,
            Digest = "sha256:" + new string('0', 64)
        };
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);
        var result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            ReleaseVersion = TestReleaseVersion,
            WinPe = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Contains("digest mismatch", result.Error!.Details, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenDownloadIsCancelled_PropagatesCancellationWithoutProvisioning()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] bytes = File.ReadAllBytes(workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe"));
        using var cancellation = new CancellationTokenSource();
        var handler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", bytes) { OnDownload = cancellation.Cancel };
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            MountedImagePath = workspace.MountedImagePath,
            ReleaseVersion = TestReleaseVersion,
            WinPe = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenPreparedArchiveChanges_RejectsWithoutProvisioning()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string archive = workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe");
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());
        var prepared = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            WinPe = new() { IsEnabled = true, ArchivePath = archive }
        }, TestContext.Current.CancellationToken);
        Assert.True(prepared.IsSuccess, prepared.Error?.Details);
        using (ZipArchive changed = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(changed.CreateEntry("replacement.dll").Open());
            writer.Write("tampered dependency");
        }

        var result = await service.ProvisionAsync(prepared.Value! with { MountedImagePath = workspace.MountedImagePath }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Contains("SHA256", result.Error!.Details, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry")));
    }

    [Fact]
    public async Task PrepareAsync_WhenApplicationOptionsAreNull_ReturnsValidationFailure()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("unused", []);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));
        var result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            WinPe = null!
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData(1, "nonzero_exit")]
    [InlineData(0, "artifact_missing")]
    public async Task ProvisionAsync_WhenPublishFails_PreservesProcessContext(int exitCode, string reason)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "Foundry.WinPE.csproj");
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner { ExitCode = exitCode, CreateOutput = false };
        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            MountedImagePath = workspace.MountedImagePath,
            WinPe = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true, ProjectPath = projectPath }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(exitCode, result.Error!.ExitCode);
        Assert.Equal("dotnet", result.Error.ToolName);
        Assert.Equal(reason, result.Error.FailureReason);
        Assert.Equal("runtime.publish", result.Error.Stage);
        Assert.Contains(exitCode == 0 ? "expected executable" : "error NETSDK1045", exitCode == 0 ? result.Error.Message : result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_WhenArchiveIsMissing_PreservesOriginalException()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            MountedImagePath = workspace.MountedImagePath,
            WinPe = new WinPeRuntimePayloadApplicationOptions
            {
                IsEnabled = true,
                ArchivePath = Path.Combine(workspace.RootPath, "missing.zip")
            }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.IsType<FileNotFoundException>(result.Error!.Exception);
        Assert.Contains("archive was not found", result.Error.Exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "win-x64")]
    [InlineData(WinPeArchitecture.Arm64, "win-arm64")]
    public async Task ProvisionAsync_WhenDebugArchivesAreProvided_ExtractsWinPeAndPostInstallIntoTheBootImage(WinPeArchitecture architecture, string runtime)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string winPeArchivePath = workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe");
        string postInstallArchivePath = workspace.CreateArchive("postinstall.zip", "Foundry.PostInstall.exe", runtimeIdentifier: runtime);
        string foundryRoot = Path.Combine(workspace.MountedImagePath, "Foundry");
        string legacyConnectSeedPath = Path.Combine(foundryRoot, "Seed", "Foundry.Connect.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConnectSeedPath)!);
        File.WriteAllText(legacyConnectSeedPath, "legacy");
        Directory.CreateDirectory(Path.Combine(foundryRoot, "Bootstrap"));
        File.WriteAllText(Path.Combine(foundryRoot, "Bootstrap", "Foundry.Bootstrap.exe"), "legacy bootstrap");
        foreach (string legacyApplication in new[] { "Foundry.Connect", "Foundry.Deploy" })
        {
            string oldRoot = Path.Combine(foundryRoot, "Runtime", legacyApplication, runtime);
            Directory.CreateDirectory(oldRoot);
            File.WriteAllText(Path.Combine(oldRoot, "original.zip"), "legacy archive");
        }

        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = architecture,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                MountedImagePath = workspace.MountedImagePath,
                WinPe = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true, ArchivePath = winPeArchivePath },
                PostInstall = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true, ArchivePath = postInstallArchivePath }
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("Foundry.WinPE.exe", File.ReadAllText(Path.Combine(foundryRoot, "WinPE", "Foundry.WinPE.exe")));
        Assert.False(Directory.Exists(Path.Combine(foundryRoot, "Bootstrap")));
        Assert.False(Directory.Exists(Path.Combine(foundryRoot, "Runtime")));
        Assert.False(File.Exists(legacyConnectSeedPath));
        Assert.True(File.Exists(Path.Combine(foundryRoot, WinPeWorkspaceLayout.PostInstallDirectoryName, "Foundry.PostInstall.exe")));
        Assert.False(File.Exists(Path.Combine(foundryRoot, WinPeWorkspaceLayout.PostInstallDirectoryName, "dependency.dll")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenArchiveAndProjectAreProvided_PrefersArchive()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string winPeArchivePath = workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe");
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.WinPE", "Foundry.WinPE.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner();

        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
                MountedImagePath = workspace.MountedImagePath,
                WinPe = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = winPeArchivePath,
                    ProjectPath = projectPath
                }
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Empty(runner.Executions);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "WinPE", "Foundry.WinPE.exe")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenPostInstallProjectIsProvided_PublishesSingleFileBeforeProvisioning()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.PostInstall", "Foundry.PostInstall.csproj");
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
                PostInstall = new() { IsEnabled = true, ProjectPath = projectPath }
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        WinPeProcessExecution execution = Assert.Single(runner.Executions);
        Assert.Equal("dotnet", execution.FileName);
        Assert.Contains("publish", execution.Arguments);
        Assert.Contains("-r win-arm64", execution.Arguments);
        Assert.Contains("/p:Platform=ARM64", execution.Arguments);
        // Single-file without extraction or compression: the executable runs in place in the target OS and boot.wim compresses it.
        Assert.Contains("/p:PublishSingleFile=true", execution.Arguments);
        Assert.Contains("/p:EnableCompressionInSingleFile=false", execution.Arguments);
        Assert.Contains("/p:IncludeNativeLibrariesForSelfExtract=false", execution.Arguments);
        Assert.Contains("/p:IncludeAllContentForSelfExtract=false", execution.Arguments);
        Assert.Contains("/p:DebugType=None", execution.Arguments);
        Assert.DoesNotContain("SelfExtract=true", execution.Arguments);
        Assert.DoesNotContain("EnableCompressionInSingleFile=true", execution.Arguments);
        string archive = Assert.Single(Directory.GetFiles(workspace.WorkingDirectoryPath, "Foundry.PostInstall-win-arm64.zip", SearchOption.AllDirectories));
        using (ZipArchive stored = ZipFile.OpenRead(archive))
        {
            Assert.Equal(["Foundry.PostInstall.exe", "foundry.postinstall.json", "Launch.cmd"],
                stored.Entries.Select(entry => entry.FullName).Order(StringComparer.OrdinalIgnoreCase));
        }

        string postInstallRoot = Path.Combine(workspace.MountedImagePath, "Foundry", WinPeWorkspaceLayout.PostInstallDirectoryName);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<PostInstallRuntimeManifest>(
            File.ReadAllText(Path.Combine(postInstallRoot, PostInstallRuntimeManifest.FileName)),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("win-arm64", manifest.RuntimeIdentifier);
        Assert.Equal(2, manifest.Files.Count);
        foreach (var file in manifest.Files)
        {
            Assert.Equal(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(postInstallRoot, file.RelativePath)))));
        }
        Assert.False(File.Exists(Path.Combine(postInstallRoot, "Foundry.PostInstall.pdb")));
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "win-x64", "x64")]
    [InlineData(WinPeArchitecture.Arm64, "win-arm64", "ARM64")]
    public async Task ProvisionAsync_WhenWinPeProjectIsProvided_PublishesASingleFileExecutableIntoTheBootImage(WinPeArchitecture architecture, string runtime, string platform)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "src", "Foundry.WinPE", "Foundry.WinPE.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner();

        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = architecture,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive(architecture) },
                MountedImagePath = workspace.MountedImagePath,
                WinPe = new WinPeRuntimePayloadApplicationOptions
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
        Assert.Contains($"-r {runtime}", execution.Arguments);
        Assert.Contains($"/p:Platform={platform}", execution.Arguments);
        Assert.Contains("--self-contained true", execution.Arguments);
        // Single-file without extraction: WPF's native libraries stay beside the executable and boot.wim compresses the payload.
        Assert.Contains("/p:PublishSingleFile=true", execution.Arguments);
        Assert.Contains("/p:EnableCompressionInSingleFile=false", execution.Arguments);
        Assert.Contains("/p:IncludeNativeLibrariesForSelfExtract=false", execution.Arguments);
        Assert.Contains("/p:IncludeAllContentForSelfExtract=false", execution.Arguments);
        Assert.DoesNotContain("SelfExtract=true", execution.Arguments);
        Assert.DoesNotContain("EnableCompressionInSingleFile=true", execution.Arguments);
        string winPeRoot = Path.Combine(workspace.MountedImagePath, "Foundry", "WinPE");
        Assert.True(File.Exists(Path.Combine(winPeRoot, "Foundry.WinPE.exe")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenReleaseWinPeIsEnabled_DownloadsAndExtractsReleaseArchive()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] archiveBytes = await File.ReadAllBytesAsync(workspace.CreateArchive("winpe-release.zip", "Foundry.WinPE.exe"), TestContext.Current.CancellationToken);
        var httpHandler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", archiveBytes);
        var service = new WinPeRuntimePayloadProvisioningService(
            new FakeRuntimeProcessRunner(),
            new HttpClient(httpHandler), NoEnvironment);
        var progress = new CapturingProgress<WinPeDownloadProgress>();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
                MountedImagePath = workspace.MountedImagePath,
                ReleaseVersion = TestReleaseVersion,
                WinPe = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ProvisioningSource = WinPeProvisioningSource.Release
                }
            },
            progress,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Contains(FakeReleaseHttpMessageHandler.ReleaseUri, httpHandler.RequestUris);
        Assert.Contains(FakeReleaseHttpMessageHandler.DownloadUri, httpHandler.RequestUris);
        Assert.Contains(progress.Items, item => item is { Percent: 0, ItemName: "Foundry.WinPE" }
            && item.Status == WinPeProgressStatuses.DownloadingRuntimePayload);
        Assert.Contains(progress.Items, item => item is { Percent: 100, ItemName: "Foundry.WinPE", BytesTransferred: > 0 });
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "WinPE", "Foundry.WinPE.exe")));
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenOptionalApplicationsAreDisabled_ProvisionsOnlyPostInstall()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();

        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner());

        WinPeResult result = await service.ProvisionAsync(
            new WinPeRuntimePayloadProvisioningOptions
            {
                Architecture = WinPeArchitecture.X64,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
                MountedImagePath = workspace.MountedImagePath
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "WinPE")));
        Assert.True(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", WinPeWorkspaceLayout.PostInstallDirectoryName)));
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64)]
    [InlineData(WinPeArchitecture.Arm64)]
    public async Task ProvisionAsync_WhenWinPeArchiveIsProvided_StagesOnlyInMountedWinPe(WinPeArchitecture architecture)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("unused", []);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));
        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive(architecture) },
            MountedImagePath = workspace.MountedImagePath,
            WinPe = new() { IsEnabled = true, ArchivePath = workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe") }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "WinPE", "Foundry.WinPE.exe")));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task ProvisionAsync_WhenReleaseMetadataIsIncomplete_DoesNotDownloadAnyArchive()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("Foundry.WinPE-win-x64.zip", []);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);
        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            ReleaseVersion = TestReleaseVersion,
            WinPe = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release },
            PostInstall = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(FakeReleaseHttpMessageHandler.ReleaseUri, Assert.Single(handler.RequestUris));
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("missing-archive")]
    [InlineData("invalid-archive")]
    [InlineData("missing-executable")]
    [InlineData("missing-project")]
    [InlineData("publish-failed")]
    [InlineData("publish-missing-executable")]
    public async Task ProvisionAsync_WhenWinPeDebugSourceFails_DoesNotContactGitHub(string failure)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "Foundry.WinPE.csproj");
        File.WriteAllText(projectPath, "<Project />");
        string invalidArchive = Path.Combine(workspace.RootPath, "invalid.zip");
        File.WriteAllText(invalidArchive, "invalid zip");
        var winPe = failure switch
        {
            "missing-archive" => new WinPeRuntimePayloadApplicationOptions { ArchivePath = Path.Combine(workspace.RootPath, "missing.zip") },
            "invalid-archive" => new() { ArchivePath = invalidArchive },
            "missing-executable" => new() { ArchivePath = workspace.CreateArchive("wrong.zip", "wrong.exe") },
            "missing-project" => new() { ProjectPath = Path.Combine(workspace.RootPath, "missing.csproj") },
            "publish-failed" or "publish-missing-executable" => new() { ProjectPath = projectPath },
            _ => new()
        };
        var handler = new FakeReleaseHttpMessageHandler("unused", []);
        var runner = new FakeRuntimeProcessRunner { ExitCode = failure == "publish-failed" ? 1 : 0, CreateOutput = false };
        var service = new WinPeRuntimePayloadProvisioningService(runner, new HttpClient(handler), NoEnvironment);
        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            WinPe = winPe with { IsEnabled = true },
            ReleaseVersion = TestReleaseVersion,
            PostInstall = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "win-x64")]
    [InlineData(WinPeArchitecture.Arm64, "win-arm64")]
    public async Task PrepareAsync_WhenLatestChanges_ReusesOneSnapshotAcrossProvisioningStages(WinPeArchitecture architecture, string runtime)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var payloads = new Dictionary<string, byte[]>
        {
            [$"Foundry.WinPE-{runtime}.zip"] = File.ReadAllBytes(workspace.CreateArchive("winpe-release.zip", "Foundry.WinPE.exe")),
            [$"Foundry.PostInstall-{runtime}.zip"] = File.ReadAllBytes(workspace.CreateArchive("postinstall-release.zip", "Foundry.PostInstall.exe", runtimeIdentifier: runtime))
        };
        var handler = new MovingReleaseHttpMessageHandler(payloads);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);
        var original = new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            ReleaseVersion = FoundryReleaseTag.UnpublishedVersion,
            WinPe = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release },
            PostInstall = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        };
        var prepared = await service.PrepareAsync(original, TestContext.Current.CancellationToken);
        Assert.True(prepared.IsSuccess, prepared.Error?.Details);
        Assert.Equal("v1", prepared.Value!.ReleaseSnapshot!.TagName);
        Assert.Equal(3, handler.RequestUris.Count);
        string secondImage = Path.Combine(workspace.RootPath, "mount-2");
        var image = await service.ProvisionAsync(prepared.Value with { MountedImagePath = workspace.MountedImagePath }, cancellationToken: TestContext.Current.CancellationToken);
        var repeated = await service.ProvisionAsync(prepared.Value with { MountedImagePath = secondImage }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(image.IsSuccess, image.Error?.Details);
        Assert.True(repeated.IsSuccess, repeated.Error?.Details);
        Assert.Equal(1, handler.MetadataRequests);
        Assert.Equal(3, handler.RequestUris.Count);
        Assert.All(handler.RequestUris.Where(uri => uri != FakeReleaseHttpMessageHandler.LatestReleaseUri), uri => Assert.Contains("/v1/", uri));
        foreach (string mount in new[] { workspace.MountedImagePath, secondImage })
        {
            Assert.True(File.Exists(Path.Combine(mount, "Foundry", "WinPE", "Foundry.WinPE.exe")));
            Assert.True(File.Exists(Path.Combine(mount, "Foundry", WinPeWorkspaceLayout.PostInstallDirectoryName, "Foundry.PostInstall.exe")));
        }
        var nextBuild = await service.PrepareAsync(original, TestContext.Current.CancellationToken);
        Assert.True(nextBuild.IsSuccess, nextBuild.Error?.Details);
        Assert.Equal("v2", nextBuild.Value!.ReleaseSnapshot!.TagName);
        Assert.Equal("v1", prepared.Value.ReleaseSnapshot.TagName);
        Assert.NotEqual(prepared.Value.PostInstall.ArchivePath, nextBuild.Value.PostInstall.ArchivePath);
    }

    [Fact]
    public async Task ProvisionAsync_WhenWinPeReleaseHasArchiveOverride_DoesNotResolveWinPeRelease()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] postInstall = File.ReadAllBytes(workspace.CreateArchive("postinstall.zip", "Foundry.PostInstall.exe"));
        var handler = new FakeReleaseHttpMessageHandler("Foundry.PostInstall-win-x64.zip", postInstall);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler), NoEnvironment);
        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            ReleaseVersion = TestReleaseVersion,
            WinPe = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release, ArchivePath = workspace.CreateArchive("winpe.zip", "Foundry.WinPE.exe") },
            PostInstall = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task PrepareAsync_WhenWinPeProjectIsProvided_PublishesOnceForReuse()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "Foundry.WinPE.csproj");
        File.WriteAllText(projectPath, "<Project />");
        var handler = new FakeReleaseHttpMessageHandler("unused", []);
        var runner = new FakeRuntimeProcessRunner();
        var service = new WinPeRuntimePayloadProvisioningService(runner, new HttpClient(handler));
        var prepared = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            PostInstall = new() { IsEnabled = true, ArchivePath = workspace.CreatePostInstallArchive() },
            WinPe = new() { IsEnabled = true, ProjectPath = projectPath }
        }, TestContext.Current.CancellationToken);
        Assert.True(prepared.IsSuccess, prepared.Error?.Details);
        var result = await service.ProvisionAsync(prepared.Value! with { MountedImagePath = workspace.MountedImagePath }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Single(runner.Executions);
        Assert.Empty(handler.RequestUris);
    }

    private sealed class MovingReleaseHttpMessageHandler(Dictionary<string, byte[]> payloads) : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];
        public int MetadataRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string uri = request.RequestUri!.AbsoluteUri;
            RequestUris.Add(uri);
            if (uri.StartsWith(FakeReleaseHttpMessageHandler.ReleasesApiUri, StringComparison.Ordinal))
            {
                MetadataRequests++;
                string tag = $"v{MetadataRequests}";
                string json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    tag_name = tag,
                    assets = payloads.Select(pair => new
                    {
                        name = pair.Key,
                        browser_download_url = $"https://example.test/{tag}/{pair.Key}",
                        digest = $"sha256:{Convert.ToHexString(SHA256.HashData(pair.Value))}"
                    })
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payloads[Path.GetFileName(request.RequestUri.AbsolutePath)])
            });
        }
    }

    private sealed class FakeReleaseHttpMessageHandler(string assetName, byte[] archiveBytes) : HttpMessageHandler
    {
        public const string ReleasesApiUri = "https://api.github.com/repos/foundry-osd/foundry/releases/";
        public const string LatestReleaseUri = ReleasesApiUri + "latest";
        public const string ReleaseUri = ReleasesApiUri + "tags/v" + TestReleaseVersion;
        public const string DownloadUri = "https://example.test/Foundry.WinPE-win-x64.zip";
        public bool OverrideDigest { get; init; }
        public string? Digest { get; init; }
        public Action? OnDownload { get; init; }

        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string requestUri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestUris.Add(requestUri);

            if (requestUri.StartsWith(ReleasesApiUri, StringComparison.Ordinal))
            {
                string digest = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
                string json = $$"""
                    {
                      "assets": [
                        {
                          "name": "{{assetName}}",
                          "browser_download_url": "{{DownloadUri}}",
                          "digest": {{System.Text.Json.JsonSerializer.Serialize(OverrideDigest ? Digest : $"sha256:{digest}")}}
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
                OnDownload?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
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
        public Action<CancellationToken>? OnRun { get; init; }
        public List<WinPeProcessExecution> Executions { get; } = [];
        public int ExitCode { get; init; }
        public bool CreateOutput { get; init; } = true;

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            OnRun?.Invoke(cancellationToken);
            string outputDirectory = ExtractOutputDirectory(arguments);
            Directory.CreateDirectory(outputDirectory);
            string executableName = arguments.Contains("Foundry.WinPE.csproj", StringComparison.OrdinalIgnoreCase)
                ? "Foundry.WinPE.exe"
                : arguments.Contains("Foundry.PostInstall.csproj", StringComparison.OrdinalIgnoreCase)
                ? "Foundry.PostInstall.exe"
                : "Foundry.AutopilotInteractive.exe";
            if (CreateOutput)
            {
                File.WriteAllText(Path.Combine(outputDirectory, executableName), executableName);
                if (executableName == "Foundry.PostInstall.exe")
                {
                    File.WriteAllText(Path.Combine(outputDirectory, "Launch.cmd"), "@echo off");
                    File.WriteAllText(Path.Combine(outputDirectory, "Foundry.PostInstall.pdb"), "symbols");
                }
            }

            var execution = new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                ExitCode = ExitCode,
                StandardError = ExitCode == 0 ? string.Empty : "error NETSDK1045: unsupported target framework"
            };

            Executions.Add(execution);
            return Task.FromResult(execution);
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        private static string ExtractOutputDirectory(string arguments)
        {
            int marker = arguments.IndexOf("-o ", StringComparison.Ordinal);
            Assert.True(marker >= 0, arguments);
            string remaining = arguments[(marker + 3)..].Trim();
            if (remaining.StartsWith('"'))
            {
                int endQuote = remaining.IndexOf('"', 1);
                Assert.True(endQuote > 1, remaining);
                return remaining[1..endQuote];
            }

            int nextSpace = remaining.IndexOf(' ', StringComparison.Ordinal);
            return nextSpace < 0 ? remaining : remaining[..nextSpace];
        }
    }

    private sealed class TempRuntimeWorkspace : IDisposable
    {
        private TempRuntimeWorkspace(string rootPath)
        {
            RootPath = rootPath;
            WorkingDirectoryPath = Path.Combine(rootPath, "work");
            MountedImagePath = Path.Combine(rootPath, "mount");
            Directory.CreateDirectory(WorkingDirectoryPath);
            Directory.CreateDirectory(MountedImagePath);
        }

        public string RootPath { get; }
        public string WorkingDirectoryPath { get; }
        public string MountedImagePath { get; }

        public static TempRuntimeWorkspace Create()
        {
            return new TempRuntimeWorkspace(Path.Combine(Path.GetTempPath(), $"foundry-runtime-{Guid.NewGuid():N}"));
        }

        public string CreateArchive(string archiveName, string executableName, string runtimeIdentifier = "win-x64", int contract = 1)
        {
            string payloadPath = Path.Combine(RootPath, "payloads", Path.GetFileNameWithoutExtension(archiveName));
            Directory.CreateDirectory(payloadPath);
            File.WriteAllText(Path.Combine(payloadPath, executableName), executableName);
            File.WriteAllText(Path.Combine(payloadPath, "dependency.dll"), "archive dependency");
            if (executableName == "Foundry.PostInstall.exe")
            {
                File.WriteAllText(Path.Combine(payloadPath, "Launch.cmd"), "launch");
                File.WriteAllText(Path.Combine(payloadPath, "foundry.postinstall.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    contractVersion = contract,
                    runtimeIdentifier,
                    files = new[] { executableName, "Launch.cmd" }.Select(name => new
                    {
                        relativePath = name,
                        length = new FileInfo(Path.Combine(payloadPath, name)).Length,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(payloadPath, name))))
                    })
                }));
            }

            string archivePath = Path.Combine(RootPath, archiveName);
            ZipFile.CreateFromDirectory(payloadPath, archivePath);
            return archivePath;
        }

        public string CreatePostInstallArchive(WinPeArchitecture architecture = WinPeArchitecture.X64) => CreateArchive(
            $"required-postinstall-{architecture}.zip", "Foundry.PostInstall.exe", architecture == WinPeArchitecture.Arm64 ? "win-arm64" : "win-x64");

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
