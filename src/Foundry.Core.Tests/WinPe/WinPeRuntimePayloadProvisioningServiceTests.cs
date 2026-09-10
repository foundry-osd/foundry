// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeRuntimePayloadProvisioningServiceTests
{
    [Fact]
    public async Task PrepareAsync_WhenApplicationOptionsAreNull_ReturnsValidationFailure()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("unused", []);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));
        var result = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Bootstrap = null!
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
        string projectPath = Path.Combine(workspace.RootPath, "Foundry.Connect.csproj");
        File.WriteAllText(projectPath, "<Project />");
        var runner = new FakeRuntimeProcessRunner { ExitCode = exitCode, CreateOutput = false };
        var service = new WinPeRuntimePayloadProvisioningService(runner);

        WinPeResult result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            Connect = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true, ProjectPath = projectPath }
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
            MountedImagePath = workspace.MountedImagePath,
            Connect = new WinPeRuntimePayloadApplicationOptions
            {
                IsEnabled = true,
                ArchivePath = Path.Combine(workspace.RootPath, "missing.zip")
            }
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.IsType<FileNotFoundException>(result.Error!.Exception);
        Assert.Contains("archive was not found", result.Error.Exception.Message, StringComparison.Ordinal);
    }

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
    [InlineData(WinPeArchitecture.X64)]
    [InlineData(WinPeArchitecture.Arm64)]
    public async Task ProvisionAsync_WhenBootstrapArchiveIsProvided_StagesOnlyInMountedBootstrap(WinPeArchitecture architecture)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("unused", []);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));
        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            UsbCacheRootPath = workspace.UsbCacheRootPath,
            Bootstrap = new() { IsEnabled = true, ArchivePath = workspace.CreateArchive("bootstrap.zip", "Foundry.Bootstrap.exe") }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Bootstrap", "Foundry.Bootstrap.exe")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.UsbCacheRootPath));
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task ProvisionAsync_WhenReleaseMetadataIsIncomplete_DoesNotDownloadAnyArchive()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var handler = new FakeReleaseHttpMessageHandler("Foundry.Bootstrap-win-x64.zip", []);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));
        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            Bootstrap = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release },
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(FakeReleaseHttpMessageHandler.LatestReleaseUri, Assert.Single(handler.RequestUris));
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("missing-archive")]
    [InlineData("invalid-archive")]
    [InlineData("missing-executable")]
    [InlineData("missing-project")]
    [InlineData("publish-failed")]
    [InlineData("publish-missing-executable")]
    public async Task ProvisionAsync_WhenBootstrapDebugSourceFails_DoesNotContactGitHub(string failure)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "Foundry.Bootstrap.csproj");
        File.WriteAllText(projectPath, "<Project />");
        string invalidArchive = Path.Combine(workspace.RootPath, "invalid.zip");
        File.WriteAllText(invalidArchive, "invalid zip");
        var bootstrap = failure switch
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
        var service = new WinPeRuntimePayloadProvisioningService(runner, new HttpClient(handler));
        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            Bootstrap = bootstrap with { IsEnabled = true },
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "win-x64")]
    [InlineData(WinPeArchitecture.Arm64, "win-arm64")]
    public async Task PrepareAsync_WhenLatestChanges_ReusesOneSnapshotAcrossImageAndUsb(WinPeArchitecture architecture, string runtime)
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        var payloads = new Dictionary<string, byte[]>
        {
            [$"Foundry.Bootstrap-{runtime}.zip"] = File.ReadAllBytes(workspace.CreateArchive("bootstrap-release.zip", "Foundry.Bootstrap.exe")),
            [$"Foundry.Connect-{runtime}.zip"] = File.ReadAllBytes(workspace.CreateArchive("connect-release.zip", "Foundry.Connect.exe"))
        };
        var handler = new MovingReleaseHttpMessageHandler(payloads);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));
        var original = new WinPeRuntimePayloadProvisioningOptions
        {
            Architecture = architecture,
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Bootstrap = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release },
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        };
        var prepared = await service.PrepareAsync(original, TestContext.Current.CancellationToken);
        Assert.True(prepared.IsSuccess, prepared.Error?.Details);
        Assert.Equal("v1", prepared.Value!.ReleaseSnapshot!.TagName);
        Assert.Single(handler.RequestUris);
        var image = await service.ProvisionAsync(prepared.Value with { MountedImagePath = workspace.MountedImagePath }, cancellationToken: TestContext.Current.CancellationToken);
        var usb = await service.ProvisionAsync(prepared.Value with { UsbCacheRootPath = workspace.UsbCacheRootPath }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(image.IsSuccess, image.Error?.Details);
        Assert.True(usb.IsSuccess, usb.Error?.Details);
        Assert.Equal(1, handler.MetadataRequests);
        Assert.All(handler.RequestUris.Where(uri => uri != FakeReleaseHttpMessageHandler.LatestReleaseUri), uri => Assert.Contains("/v1/", uri));
        Assert.True(File.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Bootstrap", "Foundry.Bootstrap.exe")));
        Assert.False(Directory.Exists(Path.Combine(workspace.UsbCacheRootPath, "Runtime", "Foundry.Bootstrap")));
        Assert.False(Directory.Exists(Path.Combine(workspace.UsbCacheRootPath, "Bootstrap")));
        Assert.True(File.Exists(Path.Combine(workspace.UsbCacheRootPath, "Runtime", "Foundry.Connect", runtime, "Foundry.Connect.exe")));
        var nextBuild = await service.PrepareAsync(original, TestContext.Current.CancellationToken);
        Assert.True(nextBuild.IsSuccess, nextBuild.Error?.Details);
        Assert.Equal("v2", nextBuild.Value!.ReleaseSnapshot!.TagName);
        Assert.Equal("v1", prepared.Value.ReleaseSnapshot.TagName);
    }

    [Fact]
    public async Task ProvisionAsync_WhenBootstrapReleaseHasArchiveOverride_DoesNotResolveBootstrapRelease()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        byte[] connect = File.ReadAllBytes(workspace.CreateArchive("connect.zip", "Foundry.Connect.exe"));
        var handler = new FakeReleaseHttpMessageHandler("Foundry.Connect-win-x64.zip", connect);
        var service = new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner(), new HttpClient(handler));
        var result = await service.ProvisionAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            MountedImagePath = workspace.MountedImagePath,
            Bootstrap = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release, ArchivePath = workspace.CreateArchive("bootstrap.zip", "Foundry.Bootstrap.exe") },
            Connect = new() { IsEnabled = true, ProvisioningSource = WinPeProvisioningSource.Release }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task PrepareAsync_WhenBootstrapProjectIsProvided_PublishesOnceForReuse()
    {
        using TempRuntimeWorkspace workspace = TempRuntimeWorkspace.Create();
        string projectPath = Path.Combine(workspace.RootPath, "Foundry.Bootstrap.csproj");
        File.WriteAllText(projectPath, "<Project />");
        var handler = new FakeReleaseHttpMessageHandler("unused", []);
        var runner = new FakeRuntimeProcessRunner();
        var service = new WinPeRuntimePayloadProvisioningService(runner, new HttpClient(handler));
        var prepared = await service.PrepareAsync(new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = workspace.WorkingDirectoryPath,
            Bootstrap = new() { IsEnabled = true, ProjectPath = projectPath }
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
            if (uri == FakeReleaseHttpMessageHandler.LatestReleaseUri)
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
        public const string LatestReleaseUri = "https://api.github.com/repos/foundry-osd/foundry/releases/latest";
        public const string DownloadUri = "https://example.test/Foundry.Connect-win-x64.zip";

        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string requestUri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestUris.Add(requestUri);

            if (string.Equals(requestUri, LatestReleaseUri, StringComparison.Ordinal))
            {
                string digest = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
                string json = $$"""
                    {
                      "assets": [
                        {
                          "name": "{{assetName}}",
                          "browser_download_url": "{{DownloadUri}}",
                          "digest": "sha256:{{digest}}"
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
        public int ExitCode { get; init; }
        public bool CreateOutput { get; init; } = true;

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            string outputDirectory = ExtractOutputDirectory(arguments);
            Directory.CreateDirectory(outputDirectory);
            string executableName = arguments.Contains("Foundry.Bootstrap.csproj", StringComparison.OrdinalIgnoreCase)
                ? "Foundry.Bootstrap.exe"
                : arguments.Contains("Foundry.Connect.csproj", StringComparison.OrdinalIgnoreCase)
                ? "Foundry.Connect.exe"
                : "Foundry.Deploy.exe";
            if (CreateOutput)
            {
                File.WriteAllText(Path.Combine(outputDirectory, executableName), executableName);
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
            File.WriteAllText(Path.Combine(payloadPath, executableName), executableName);

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
