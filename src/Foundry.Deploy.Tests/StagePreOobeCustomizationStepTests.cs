// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using Foundry.Core.Models.PreOobe;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using CoreDeployNetworkProfileRoamingSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings;
using CoreDeployNetworkSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings;
using NetworkProfileRoamingTransportSettings = Foundry.Core.Models.Configuration.NetworkProfileRoamingTransportSettings;

namespace Foundry.Deploy.Tests;

public sealed class StagePreOobeCustomizationStepTests
{
    [Fact]
    public async Task MediaPublisher_OutputIsAcceptedWithoutNetworkOrWritableCache()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.RootPath, "hello.ps1");
        await File.WriteAllTextAsync(source, "Write-Output 'fixture'", TestContext.Current.CancellationToken);
        var library = new Foundry.Core.Services.Packages.PreOobePackageLibraryService(Path.Combine(temp.RootPath, "library"));
        var reference = await library.ImportAsync(source, TestContext.Current.CancellationToken);
        var settings = new Foundry.Core.Models.Configuration.PreOobeSettings
        {
            IsEnabled = true,
            Actions = [new() { Id = Guid.NewGuid().ToString("N"), Name = "Fixture", Kind = Foundry.Core.Models.Configuration.PreOobeActionKind.PowerShell,
                Package = reference, EntryPoint = "hello.ps1",
                Process = new() }]
        };
        using var runtime = NativeRuntimeFixture.Create(temp.RootPath);
        var publisher = new Foundry.Core.Services.WinPe.WinPePreOobeMediaService();
        using var media = await publisher.PrepareAsync(library, settings, new Dictionary<string, string> { ["win-x64"] = runtime.RuntimeArchivePath }, TestContext.Current.CancellationToken);
        string mediaRoot = Path.Combine(temp.RootPath, "media");
        Directory.CreateDirectory(mediaRoot);
        await publisher.PublishAsync(media, mediaRoot, TestContext.Current.CancellationToken);
        string descriptorPath = Path.Combine(temp.RootPath, "descriptor.json");
        await File.WriteAllTextAsync(descriptorPath, JsonSerializer.Serialize(new PreOobeRuntimeDescriptor { ReleaseTag = "local", Assets = [runtime.RuntimeAsset, runtime.RuntimeAsset with { RuntimeIdentifier = "win-arm64", AssetName = "Foundry.PostInstall-win-arm64.zip" }] },
            Foundry.Deploy.Services.Configuration.ConfigurationJsonDefaults.SerializerOptions), TestContext.Current.CancellationToken);
        using DeploymentStepExecutionContext context = CreateContext(temp, postInstall: new()
        { IsEnabled = true, Actions = settings.Actions, ManifestId = media.ManifestId, ManifestHash = media.ManifestHash });
        var resolver = new PreOobeContentResolver(new(), new ReadOnlyStorage()) { DescriptorPath = descriptorPath, MediaRoots = () => [mediaRoot] };
        using var prepared = await resolver.PrepareAsync(context, TestContext.Current.CancellationToken);
        Assert.NotNull(prepared);
        Assert.Equal(reference.ContentHash, Assert.Single(prepared.Packages).ContentHash);
        Assert.StartsWith(mediaRoot, prepared.RuntimeArchivePath, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<IOException>(() => File.Delete(Path.Combine(prepared.Packages[0].SourceRoot, "hello.ps1")));
    }

    private sealed class ReadOnlyStorage : IDeploymentStorageService
    {
        public long? GetAvailableBytes(string path) => throw new InvalidOperationException("An intact offline generation needs no writable runtime cache.");
        public bool CanWriteDirectory(string path, string? existingFilePath = null) => false;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedHookPublication_RemovesOnlyNewInputsAndRunnableState(bool customAnswer, bool cancelled)
    {
        using var temp = new TemporaryDirectory();
        using var context = CreateContext(temp, usesCustomUnattend: customAnswer);
        context.NetworkProfileRoamingPayload = CreateRoamingPayload();
        string answerPath = Path.Combine(temp.WindowsRoot, "Windows", "Panther", "unattend.xml");
        byte[]? originalAnswer = null;
        if (customAnswer)
        {
            originalAnswer = new Foundry.Deploy.Services.Deployment.Unattend.PreOobeUnattendHookService()
                .Prepare(System.Text.Encoding.UTF8.GetBytes("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\"/>"), "x64");
            Directory.CreateDirectory(Path.GetDirectoryName(answerPath)!);
            await File.WriteAllBytesAsync(answerPath, originalAnswer, TestContext.Current.CancellationToken);
        }
        string preserved = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "NetworkProfiles", "existing.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(preserved)!);
        File.WriteAllText(preserved, "preserve");
        var service = new PreOobeTargetStagingService(path => Directory.CreateDirectory(path))
        {
            BeforeHookPublication = () =>
            {
                string journalPath = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "State", "PreOobe", "execution-result.json");
                using var journal = JsonDocument.Parse(File.ReadAllText(journalPath));
                Assert.Equal("Staging", journal.RootElement.GetProperty("status").GetString());
                if (cancelled) throw new OperationCanceledException();
                throw new IOException("injected publication failure");
            }
        };
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(), service);
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(() => step.ExecuteAsync(context, TestContext.Current.CancellationToken));
        else Assert.Equal(DeploymentStepState.Failed, (await step.ExecuteAsync(context, TestContext.Current.CancellationToken)).State);
        Assert.True(File.Exists(preserved));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(preserved)!, "wifi-profile.xml")));
        string stateRoot = Path.Combine(temp.WindowsRoot, "Windows", "Temp", "Foundry", "State", "PreOobe");
        Assert.False(File.Exists(Path.Combine(stateRoot, "plan.json")));
        Assert.False(File.Exists(Path.Combine(stateRoot, "execution-result.json")));
        if (customAnswer) Assert.Equal(originalAnswer, File.ReadAllBytes(answerPath));
        else Assert.False(File.Exists(answerPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageDriverInstaller_StagesOnlyPayloadWithoutSetupHook(bool dryRun)
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory, isDryRun: dryRun);
        string source = Path.Combine(tempDirectory.RootPath, "driver.exe");
        await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.Current.CancellationToken);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = source;

        DeploymentStepResult result = await new StageDriverInstallerStep(new FakeDriverPackStrategyResolver())
            .ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe"), context.RuntimeState.DeferredDriverPackagePath);
        Assert.Equal(!dryRun, File.Exists(context.RuntimeState.DeferredDriverPackagePath));
        if (!dryRun)
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(context.RuntimeState.DeferredDriverPackagePath!, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd")));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDriverInstallerWasStaged_DoesNotRequireOrCopyOriginalDownload()
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        string stagedPath = Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        await File.WriteAllBytesAsync(stagedPath, [1, 2, 3], TestContext.Current.CancellationToken);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DeferredDriverPackagePath = stagedPath;
        context.RuntimeState.DownloadedDriverPackPath = Path.Combine(tempDirectory.RootPath, "missing-download.exe");
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(stagedPath, TestContext.Current.CancellationToken));
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(context.RuntimeState.PreOobeManifestPath!, TestContext.Current.CancellationToken));
        Assert.Equal(context.RuntimeState.OperationId, manifest.RootElement.GetProperty("operationId").GetString());
        JsonElement driver = manifest.RootElement.GetProperty("actions").EnumerateArray().Single(script => script.GetProperty("id").GetString() == "driver-pack");
        Assert.Equal("Payloads/Drivers/driver.exe", driver.GetProperty("parameters").GetProperty("packagePath").GetString());
    }

    [Theory]
    [InlineData("RET", false, false, true)]
    [InlineData("ret", false, true, true)]
    [InlineData("VOL", false, false, false)]
    [InlineData("RET", true, false, false)]
    [InlineData("RET", true, true, false)]
    [InlineData("", false, false, false)]
    [InlineData("OEM", false, false, false)]
    public async Task StagePreOobeCustomizationStep_OnlyStandardRetailDeploymentsStageOemActivation(
        string licenseChannel,
        bool usesCustomUnattend,
        bool isDryRun,
        bool expectsActivation)
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentStepExecutionContext context = CreateContext(tempDirectory, licenseChannel, usesCustomUnattend, isDryRun);
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expectsActivation ? DeploymentStepState.Succeeded : DeploymentStepState.Skipped, result.State);
        if (expectsActivation && !isDryRun)
        {
            using JsonDocument plan = JsonDocument.Parse(File.ReadAllText(context.RuntimeState.PreOobeManifestPath!));
            Assert.Contains(plan.RootElement.GetProperty("actions").EnumerateArray(), action => action.GetProperty("id").GetString() == "windows-oem-activation");
            Assert.True(File.Exists(context.RuntimeState.PreOobeRunnerPath));
        }
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenRoamingPayloadExists_StagesImporterAndCleanup()
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));
        context.NetworkProfileRoamingPayload = CreateRoamingPayload();

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.True(File.Exists(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "NetworkProfiles", "wifi-profile.xml")));
        Assert.Contains("network-profile-roaming", File.ReadAllText(context.RuntimeState.PreOobeManifestPath!));
        Assert.Contains("OnRequest", File.ReadAllText(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Panther", "unattend.xml")));
        using JsonDocument journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(context.RuntimeState.PreOobeManifestPath!)!, "execution-result.json")));
        Assert.Equal("Pending", journal.RootElement.GetProperty("status").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(context.RuntimeState.PreOobeManifestPath!))).ToLowerInvariant(), journal.RootElement.GetProperty("planHash").GetString());
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenRetailWithDriversAndRoaming_StagesActivationBeforeCleanup()
    {
        using var tempDirectory = new TemporaryDirectory();
        string driverPackagePath = Path.Combine(tempDirectory.RootPath, "driver.exe");
        File.WriteAllBytes(driverPackagePath, [1, 2, 3]);
        DeploymentStepExecutionContext context = CreateContext(tempDirectory, licenseChannel: "RET");
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = driverPackagePath;
        await new StageDriverInstallerStep(new FakeDriverPackStrategyResolver())
            .ExecuteAsync(context, TestContext.Current.CancellationToken);
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));
        context.NetworkProfileRoamingPayload = CreateRoamingPayload();

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(Path.Combine(tempDirectory.WindowsRoot, "Windows", "Temp", "Foundry", "Payloads", "Drivers", "driver.exe"), context.RuntimeState.DeferredDriverPackagePath);
        Assert.True(File.Exists(context.RuntimeState.DeferredDriverPackagePath));
        using JsonDocument plan = JsonDocument.Parse(File.ReadAllText(context.RuntimeState.PreOobeManifestPath!));
        Assert.Equal(new[] { "driver-pack", "network-profile-roaming", "windows-oem-activation", "cleanup" },
            plan.RootElement.GetProperty("actions").EnumerateArray().Select(action => action.GetProperty("id").GetString()));
        JsonElement network = plan.RootElement.GetProperty("ownedPayloads").EnumerateArray().Single(payload => payload.GetProperty("relativePath").GetString() == "Payloads/NetworkProfiles");
        Assert.Equal("network-profile-roaming", Assert.Single(network.GetProperty("consumerActionIds").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task StagePreOobeCustomizationStep_WhenDeferredDriverPayloadIsMissing_FailsWithoutStaging()
    {
        using var tempDirectory = new TemporaryDirectory();
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        var step = new StagePreOobeCustomizationStep(new FakeDriverPackStrategyResolver(),
            new PreOobeTargetStagingService(path => Directory.CreateDirectory(path)));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
    }

    [Fact]
    public async Task StageDriverInstaller_WhenDeferredDriverCommandIsUnsupported_FailsWithoutStaging()
    {
        using var tempDirectory = new TemporaryDirectory();
        string driverPackagePath = Path.Combine(tempDirectory.RootPath, "driver.exe");
        File.WriteAllBytes(driverPackagePath, [1, 2, 3]);
        DeploymentStepExecutionContext context = CreateContext(tempDirectory);
        context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
        context.RuntimeState.DownloadedDriverPackPath = driverPackagePath;
        var step = new StageDriverInstallerStep(new FakeDriverPackStrategyResolver(DeferredDriverPackageCommandKind.None));

        DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
    }

    private static PreOobeNetworkProfileRoamingPayload CreateRoamingPayload()
    {
        return new PreOobeNetworkProfileRoamingPayload
        {
            DataFiles =
            [
                new PreOobeScriptDataFile
                {
                    FileName = Path.Combine("NetworkProfiles", "wifi-profile.xml"),
                    Content = "<WLANProfile />"
                },
                new PreOobeScriptDataFile
                {
                    FileName = Path.Combine("NetworkProfiles", "import-settings.json"),
                    Content = "{}"
                }
            ]
        };
    }

    private static DeploymentStepExecutionContext CreateContext(
        TemporaryDirectory tempDirectory,
        string licenseChannel = "",
        bool usesCustomUnattend = false,
        bool isDryRun = false,
        Foundry.Core.Models.Configuration.Deploy.DeployPreOobeSettings? postInstall = null)
    {
        var request = new DeploymentContext
        {
            PreOobe = postInstall ?? new(),
            Mode = DeploymentMode.Iso,
            IsDryRun = isDryRun,
            Unattend = usesCustomUnattend
                ? new UnattendSelection(new Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile(), "custom.xml")
                : null,
            CacheRootPath = tempDirectory.WorkspaceRoot,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem { LicenseChannel = licenseChannel, Architecture = "x64" },
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem()
        };
        var runtimeState = new DeploymentRuntimeState
        {
            OperationId = Guid.NewGuid().ToString("N"),
            WorkspaceRoot = tempDirectory.WorkspaceRoot,
            Mode = DeploymentMode.Iso,
            TargetWindowsPartitionRoot = tempDirectory.WindowsRoot,
            TargetFoundryRoot = tempDirectory.TargetFoundryRoot,
            ResolvedCache = new CacheResolution
            {
                RootPath = tempDirectory.WorkspaceRoot,
                Source = "test"
            },
            Network = new CoreDeployNetworkSettings
            {
                ProfileRoaming = new CoreDeployNetworkProfileRoamingSettings
                {
                    WiredDot1x = new NetworkProfileRoamingTransportSettings { IsEnabled = true, IncludePrivateKeyMaterial = true },
                    Wifi = new NetworkProfileRoamingTransportSettings { IsEnabled = true, IncludePrivateKeyMaterial = true },
                    ArtifactRootPath = tempDirectory.RootPath
                }
            }
        };

        var context = new DeploymentStepExecutionContext(
            request,
            runtimeState,
            [],
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            _ => { });
        string fixtureRuntime = Path.Combine(tempDirectory.RootPath, "context-runtime");
        Directory.CreateDirectory(fixtureRuntime);
        context.PostInstallContent = NativeRuntimeFixture.Create(fixtureRuntime);
        return context;
    }

    private sealed class FakeDriverPackStrategyResolver(
        DeferredDriverPackageCommandKind commandKind = DeferredDriverPackageCommandKind.LenovoExecutable) : IDriverPackStrategyResolver
    {
        public DriverPackExecutionPlan Resolve(
            DriverPackSelectionKind selectionKind,
            DriverPackCatalogItem? driverPack,
            string downloadedPath)
        {
            return new DriverPackExecutionPlan
            {
                InstallMode = DriverPackInstallMode.DeferredSetupComplete,
                ExtractionMethod = DriverPackExtractionMethod.None,
                DeferredCommandKind = commandKind,
                DownloadedPath = downloadedPath,
                EffectiveFileExtension = ".exe",
                Manufacturer = "Lenovo",
            };
        }
    }

    private sealed class FakeDeploymentLogService : IDeploymentLogService
    {
        public DeploymentLogSession Initialize(string rootPath)
        {
            string logsDirectory = Path.Combine(rootPath, "Logs");
            string stateDirectory = Path.Combine(rootPath, "State");
            Directory.CreateDirectory(logsDirectory);
            Directory.CreateDirectory(stateDirectory);

            return new DeploymentLogSession
            {
                RootPath = rootPath,
                LogsDirectoryPath = logsDirectory,
                StateDirectoryPath = stateDirectory,
                StateFilePath = Path.Combine(stateDirectory, "deployment-state.json")
            };
        }

        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

    }

    private sealed class FakeOperationProgressService : IOperationProgressService
    {
        public bool IsOperationInProgress => false;
        public int Progress => 0;
        public string? Status => null;
        public OperationKind? CurrentOperation => null;
        public bool CanStartOperation => true;
        public event EventHandler? ProgressChanged;
        public bool TryStart(OperationKind kind, string initialStatus, int initialProgress = 0) => true;
        public void Report(int progress, string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Complete(string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Fail(string status) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void ResetToIdle() => ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeTargetDiskService : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false)
        {
            return Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(0);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
            WorkspaceRoot = Path.Combine(RootPath, "Workspace");
            WindowsRoot = Path.Combine(RootPath, "Windows");
            TargetFoundryRoot = Path.Combine(WindowsRoot, "Foundry");
            Directory.CreateDirectory(WorkspaceRoot);
            Directory.CreateDirectory(WindowsRoot);
            Directory.CreateDirectory(TargetFoundryRoot);
        }

        public string RootPath { get; }

        public string WorkspaceRoot { get; }

        public string WindowsRoot { get; }

        public string TargetFoundryRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
