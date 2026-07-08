// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeMountedImageCustomizationServiceTests
{
    [Fact]
    public async Task CustomizeAsync_ForStandardBootImage_MountsInjectsInternationalizesAndCommits()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        string driverDirectory = Path.Combine(temp.RootPath, "drivers", "dell");
        Directory.CreateDirectory(driverDirectory);

        var runner = new FakeCustomizationRunner();
        var driverInjection = new FakeDriverInjectionService();
        var internationalization = new FakeInternationalizationService();
        var assetProvisioning = new FakeAssetProvisioningService();
        var runtimePayloadProvisioning = new FakeRuntimePayloadProvisioningService();
        var winRePreparation = new FakeWinRePreparationService();
        var service = new WinPeMountedImageCustomizationService(
            runner,
            driverInjection,
            internationalization,
            assetProvisioning,
            runtimePayloadProvisioning,
            winRePreparation,
            new FakePowerShell7ProvisioningService(),
            new FakePowerShellModuleProvisioningService());
        var runtimePayloadOptions = new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = Path.Combine(temp.RootPath, "runtime-work"),
            Connect = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true }
        };

        WinPeResult result = await service.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                BootImageSource = WinPeBootImageSource.WinPe,
                WinPeLanguage = "en-US",
                DriverPackagePaths = [driverDirectory],
                AssetProvisioning = new WinPeMountedImageAssetProvisioningOptions
                {
                    BootstrapScriptContent = "bootstrap",
                    CurlExecutableSourcePath = Path.Combine(temp.RootPath, "curl.exe"),
                    IanaWindowsTimeZoneMapJson = "{}"
                },
                RuntimePayloadProvisioning = runtimePayloadOptions,
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(winRePreparation.WasCalled);
        Assert.Single(assetProvisioning.Options);
        Assert.Equal(temp.Artifact.MountDirectoryPath, assetProvisioning.Options[0].MountedImagePath);
        Assert.Single(runtimePayloadProvisioning.Options);
        Assert.Equal(temp.Artifact.MountDirectoryPath, runtimePayloadProvisioning.Options[0].MountedImagePath);
        Assert.Equal(temp.Artifact.Architecture, runtimePayloadProvisioning.Options[0].Architecture);
        Assert.Same(runtimePayloadOptions.Connect, runtimePayloadProvisioning.Options[0].Connect);
        Assert.Single(driverInjection.Options);
        Assert.Equal(driverDirectory, Assert.Single(driverInjection.Options[0].DriverPackagePaths));
        Assert.Single(internationalization.Options);
        Assert.Contains(runner.Executions, execution => execution.Arguments.Contains("/Mount-Image", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(runner.Executions, execution => execution.Arguments.Contains("/Commit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CustomizeAsync_WhenInternationalizationFails_DiscardsMountedImage()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();

        var runner = new FakeCustomizationRunner();
        var service = new WinPeMountedImageCustomizationService(
            runner,
            new FakeDriverInjectionService(),
            new FakeInternationalizationService(WinPeResult.Failure(WinPeErrorCodes.BuildFailed, "intl failed")),
            new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(),
            new FakeWinRePreparationService(),
            new FakePowerShell7ProvisioningService(),
            new FakePowerShellModuleProvisioningService());

        WinPeResult result = await service.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                BootImageSource = WinPeBootImageSource.WinPe,
                WinPeLanguage = "en-US",
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(runner.Executions, execution => execution.Arguments.Contains("/Discard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CustomizeAsync_ForWinReWifi_AppliesDependencyFilesBeforeDriverInjection()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        string stagedDependency = Path.Combine(temp.RootPath, "cache", "wireless", "dmcmnutils.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedDependency)!);
        File.WriteAllText(stagedDependency, "dependency");

        string system32 = Path.Combine(temp.Artifact.MountDirectoryPath, "Windows", "System32");
        Directory.CreateDirectory(system32);
        string winPeShellPath = Path.Combine(system32, "winpeshl.ini");
        File.WriteAllText(winPeShellPath, "LaunchApp");
        string driverDirectory = Path.Combine(temp.RootPath, "drivers", "wifi");
        Directory.CreateDirectory(driverDirectory);

        var driverInjection = new FakeDriverInjectionService
        {
            OnInject = options =>
            {
                Assert.False(File.Exists(winPeShellPath));
                Assert.True(File.Exists(Path.Combine(options.MountedImagePath, "Windows", "System32", "dmcmnutils.dll")));
            }
        };
        var service = new WinPeMountedImageCustomizationService(
            new FakeCustomizationRunner(),
            driverInjection,
            new FakeInternationalizationService(),
            new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(),
            new FakeWinRePreparationService(new WinReBootImagePreparationResult
            {
                DependencyFiles =
                [
                    new WinReDependencyFile
                    {
                        FileName = "dmcmnutils.dll",
                        StagedPath = stagedDependency
                    }
                ]
            }),
            new FakePowerShell7ProvisioningService(),
            new FakePowerShellModuleProvisioningService());

        WinPeResult result = await service.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                BootImageSource = WinPeBootImageSource.WinReWifi,
                WinPeLanguage = "en-US",
                DriverPackagePaths = [driverDirectory],
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Single(driverInjection.Options);
    }

    [Fact]
    public async Task CustomizeAsync_ReportsTaskAndDriverPackageProgress()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        string driverA = Path.Combine(temp.RootPath, "drivers", "a");
        string driverB = Path.Combine(temp.RootPath, "drivers", "b");
        Directory.CreateDirectory(driverA);
        Directory.CreateDirectory(driverB);

        var progress = new CollectingProgress();
        var service = new WinPeMountedImageCustomizationService(
            new FakeCustomizationRunner(),
            new FakeDriverInjectionService(),
            new FakeInternationalizationService(),
            new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(),
            new FakeWinRePreparationService(),
            new FakePowerShell7ProvisioningService(),
            new FakePowerShellModuleProvisioningService());

        WinPeResult result = await service.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                BootImageSource = WinPeBootImageSource.WinPe,
                WinPeLanguage = "en-US",
                DriverPackagePaths = [driverA, driverB],
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache"),
                Progress = progress
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);

        // Numbered tasks share a single TaskCount; driver injection reports per-package item progress.
        List<WinPeMountedImageCustomizationProgress> numbered = progress.Reports.Where(report => report.TaskIndex.HasValue).ToList();
        Assert.NotEmpty(numbered);
        int taskCount = numbered[0].TaskCount!.Value;
        Assert.All(numbered, report => Assert.Equal(taskCount, report.TaskCount));

        List<WinPeMountedImageCustomizationProgress> driverReports = progress.Reports.Where(report => report.ItemCount.HasValue).ToList();
        Assert.All(driverReports, report => Assert.Equal(2, report.ItemCount));
        Assert.Contains(driverReports, report => report.ItemIndex == 1);
        Assert.Contains(driverReports, report => report.ItemIndex == 2);
    }

    [Fact]
    public async Task CustomizeAsync_WhenPowerShell7Enabled_InvokesPowerShell7Provisioning()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        var powerShell7 = new FakePowerShell7ProvisioningService();
        var service = new WinPeMountedImageCustomizationService(
            new FakeCustomizationRunner(),
            new FakeDriverInjectionService(),
            new FakeInternationalizationService(),
            new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(),
            new FakeWinRePreparationService(),
            powerShell7,
            new FakePowerShellModuleProvisioningService());

        WinPeResult result = await service.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                BootImageSource = WinPeBootImageSource.WinPe,
                WinPeLanguage = "en-US",
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache"),
                PowerShell7 = new WinPePowerShell7Settings
                {
                    IsEnabled = true,
                    CacheDirectoryPath = Path.Combine(temp.RootPath, "ps7cache"),
                    Release = new PowerShell7Release
                    {
                        Version = "7.5.8",
                        Tag = "v7.5.8",
                        AssetName = "PowerShell-7.5.8-win-x64.zip",
                        DownloadUrl = "https://example/ps7.zip"
                    }
                }
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        WinPePowerShell7ProvisioningOptions captured = Assert.Single(powerShell7.Options);
        Assert.Equal(temp.Artifact.MountDirectoryPath, captured.MountedImagePath);
        Assert.Equal("7.5.8", captured.Release!.Version);
    }

    private sealed class CollectingProgress : IProgress<WinPeMountedImageCustomizationProgress>
    {
        public List<WinPeMountedImageCustomizationProgress> Reports { get; } = [];

        public void Report(WinPeMountedImageCustomizationProgress value)
        {
            Reports.Add(value);
        }
    }

    private sealed class TempWinPeArtifact : IDisposable
    {
        private TempWinPeArtifact(string rootPath, WinPeBuildArtifact artifact, WinPeToolPaths tools)
        {
            RootPath = rootPath;
            Artifact = artifact;
            Tools = tools;
        }

        public string RootPath { get; }
        public WinPeBuildArtifact Artifact { get; }
        public WinPeToolPaths Tools { get; }

        public static TempWinPeArtifact Create()
        {
            string root = Path.Combine(Path.GetTempPath(), $"foundry-customization-{Guid.NewGuid():N}");
            string media = Path.Combine(root, "media");
            string bootWim = Path.Combine(media, "sources", "boot.wim");
            string mount = Path.Combine(root, "mount");
            string work = Path.Combine(root, "work");
            Directory.CreateDirectory(Path.GetDirectoryName(bootWim)!);
            Directory.CreateDirectory(mount);
            Directory.CreateDirectory(work);
            File.WriteAllText(bootWim, "wim");

            var artifact = new WinPeBuildArtifact
            {
                WorkingDirectoryPath = work,
                MediaDirectoryPath = media,
                BootWimPath = bootWim,
                MountDirectoryPath = mount,
                DriverWorkspacePath = Path.Combine(root, "drivers"),
                LogsDirectoryPath = Path.Combine(root, "logs"),
                MakeWinPeMediaPath = "makewinpemedia.cmd",
                DismPath = "dism.exe",
                Architecture = WinPeArchitecture.X64
            };

            var tools = new WinPeToolPaths
            {
                KitsRootPath = root,
                DismPath = "dism.exe"
            };

            return new TempWinPeArtifact(root, artifact, tools);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class FakeCustomizationRunner : IWinPeProcessRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
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
    }

    private sealed class FakeDriverInjectionService : IWinPeDriverInjectionService
    {
        public List<WinPeDriverInjectionOptions> Options { get; } = [];
        public Action<WinPeDriverInjectionOptions>? OnInject { get; init; }

        public Task<WinPeResult> InjectAsync(
            WinPeDriverInjectionOptions options,
            CancellationToken cancellationToken = default)
        {
            OnInject?.Invoke(options);
            Options.Add(options);
            return Task.FromResult(WinPeResult.Success());
        }
    }

    private sealed class FakeInternationalizationService(WinPeResult? result = null) : IWinPeImageInternationalizationService
    {
        public List<WinPeImageInternationalizationOptions> Options { get; } = [];

        public Task<WinPeResult> ApplyAsync(
            WinPeImageInternationalizationOptions options,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(result ?? WinPeResult.Success());
        }
    }

    private sealed class FakeAssetProvisioningService(WinPeResult? result = null) : IWinPeMountedImageAssetProvisioningService
    {
        public List<WinPeMountedImageAssetProvisioningOptions> Options { get; } = [];

        public Task<WinPeResult> ProvisionAsync(
            WinPeMountedImageAssetProvisioningOptions options,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(result ?? WinPeResult.Success());
        }
    }

    private sealed class FakeRuntimePayloadProvisioningService(WinPeResult? result = null) : IWinPeRuntimePayloadProvisioningService
    {
        public List<WinPeRuntimePayloadProvisioningOptions> Options { get; } = [];
        public List<IProgress<WinPeDownloadProgress>?> DownloadProgressItems { get; } = [];

        public Task<WinPeResult> ProvisionAsync(
            WinPeRuntimePayloadProvisioningOptions options,
            IProgress<WinPeDownloadProgress>? downloadProgress = null,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            DownloadProgressItems.Add(downloadProgress);
            return Task.FromResult(result ?? WinPeResult.Success());
        }
    }

    private sealed class FakePowerShell7ProvisioningService : IWinPePowerShell7ProvisioningService
    {
        public List<WinPePowerShell7ProvisioningOptions> Options { get; } = [];

        public Task<WinPeResult> ProvisionAsync(
            WinPePowerShell7ProvisioningOptions options,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(WinPeResult.Success());
        }
    }

    private sealed class FakePowerShellModuleProvisioningService : IWinPePowerShellModuleProvisioningService
    {
        public List<WinPePowerShellModuleProvisioningOptions> Options { get; } = [];

        public Task<WinPeResult> ProvisionAsync(
            WinPePowerShellModuleProvisioningOptions options,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(WinPeResult.Success());
        }
    }

    private sealed class FakeWinRePreparationService(WinReBootImagePreparationResult? result = null) : IWinReBootImagePreparationService
    {
        public bool WasCalled { get; private set; }

        public Task<WinPeResult<WinReBootImagePreparationResult>> ReplaceBootWimAsync(
            WinReBootImagePreparationOptions options,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(WinPeResult<WinReBootImagePreparationResult>.Success(
                result ?? new WinReBootImagePreparationResult
                {
                    DependencyFiles = []
                }));
        }
    }
}
