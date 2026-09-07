// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Services.Catalog;
using Foundry.Core.Services.Media;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeMountedImageCustomizationServiceTests
{
    [Fact]
    public async Task CustomizeAsync_UnverifiedEmbeddedRuntimeFailsBeforeCommit()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        byte[] xml = Encoding.UTF8.GetBytes("<catalog schemaVersion=\"4\" />");
        string digest = Convert.ToHexString(SHA256.HashData(xml)).ToLowerInvariant();
        var catalog = new VerifiedCatalogDocument(VerifiedCatalogSources.OperatingSystems, xml, digest,
            "sha256:" + digest, VerifiedCatalogSources.GetUri(VerifiedCatalogSources.OperatingSystems), DateTimeOffset.UtcNow);
        using var prepared = new WinPePreparedRuntimePayloads(Guid.NewGuid(),
            [new("Foundry.Connect", "win-x64", temp.RootPath, WinPeProvisioningSource.Release, "v1", new string('a', 64),
                [new("Foundry.Connect.exe", 1, new string('b', 64))])]);
        WinPeMediaManifest manifest = WinPeMediaManifestStore.Create(prepared, MediaOperationTarget.Iso, null, [catalog]);
        var runner = new FakeCustomizationRunner();
        var assets = new FakeAssetProvisioningService();
        var service = new WinPeMountedImageCustomizationService(runner, new FakeDriverInjectionService(),
            new FakeInternationalizationService(), assets, new FakeRuntimePayloadProvisioningService(),
            new FakeWinRePreparationService(), AcceptCapabilities);

        WinPeResult result = await service.CustomizeAsync(new()
        {
            Artifact = temp.Artifact,
            Tools = temp.Tools,
            WinPeLanguage = "en-US",
            PreparedRuntime = prepared,
            MediaManifest = manifest,
            VerifiedCatalogDocuments = [catalog],
            AssetProvisioning = new() { BootstrapScriptContent = "bootstrap", IanaWindowsTimeZoneMapJson = "{}" }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Same(manifest, Assert.Single(assets.Options).MediaManifest);
        Assert.Same(catalog, Assert.Single(assets.Options[0].VerifiedCatalogDocuments));
        Assert.DoesNotContain(runner.Executions, execution => execution.Arguments.Contains("/Commit", StringComparison.Ordinal));
        Assert.Contains(runner.Executions, execution => execution.Arguments.Contains("/Discard", StringComparison.Ordinal));
    }

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
        bool capabilitiesValidated = false;
        var service = new WinPeMountedImageCustomizationService(
            runner,
            driverInjection,
            internationalization,
            assetProvisioning,
            runtimePayloadProvisioning,
            winRePreparation,
            (options, token) =>
            {
                capabilitiesValidated = true;
                Assert.Same(Assert.Single(internationalization.Options), options);
                Assert.Single(runtimePayloadProvisioning.Options);
                Assert.DoesNotContain(runner.Executions, execution => execution.Arguments.Contains("/Commit", StringComparison.Ordinal));
                return AcceptCapabilities(options, token);
            });
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
                    IanaWindowsTimeZoneMapJson = "{}"
                },
                RuntimePayloadProvisioning = runtimePayloadOptions,
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(winRePreparation.WasCalled);
        Assert.True(capabilitiesValidated);
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
            new FakeWinRePreparationService());

        WinPeResult result = await service.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                BootImageSource = WinPeBootImageSource.WinPe,
                WinPeLanguage = "en-US",
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
            },
            TestContext.Current.CancellationToken);

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
            }), AcceptCapabilities);

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
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Single(driverInjection.Options);
    }

    [Fact]
    public async Task CustomizeAsync_WhenFinalCapabilityInventoryCannotBeVerified_DiscardsWithoutCommit()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        var runner = new FakeCustomizationRunner();
        var runtime = new FakeRuntimePayloadProvisioningService();
        var service = new WinPeMountedImageCustomizationService(runner, new FakeDriverInjectionService(),
            new FakeInternationalizationService(), new FakeAssetProvisioningService(), runtime, new FakeWinRePreparationService());

        WinPeResult result = await service.CustomizeAsync(new WinPeMountedImageCustomizationOptions
        {
            Artifact = temp.Artifact,
            Tools = temp.Tools,
            WinPeLanguage = "en-US",
            RuntimePayloadProvisioning = new WinPeRuntimePayloadProvisioningOptions()
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.BuildFailed, result.Error?.Code);
        Assert.Single(runtime.Options);
        Assert.Contains(runner.Executions, execution => execution.Arguments.Contains("/Get-Packages", StringComparison.Ordinal));
        Assert.Contains(runner.Executions, execution => execution.Arguments.Contains("/Discard", StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Executions, execution => execution.Arguments.Contains("/Commit", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CustomizeAsync_WhenRuntimeIsPrepared_PlacesSamePayloadWithoutAcquisition(bool provideDestinations)
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        using var prepared = new WinPePreparedRuntimePayloads(Guid.NewGuid(), []);
        var runtime = new FakeRuntimePayloadProvisioningService { RejectLegacyPlacement = true };
        var service = new WinPeMountedImageCustomizationService(new FakeCustomizationRunner(), new FakeDriverInjectionService(),
            new FakeInternationalizationService(), new FakeAssetProvisioningService(), runtime, new FakeWinRePreparationService(), AcceptCapabilities);
        var destinations = new WinPeRuntimePayloadProvisioningOptions
        {
            Connect = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true },
            WorkingDirectoryPath = temp.Artifact.WorkingDirectoryPath
        };

        WinPeResult result = await service.CustomizeAsync(new WinPeMountedImageCustomizationOptions
        {
            Artifact = temp.Artifact,
            Tools = temp.Tools,
            WinPeLanguage = "en-US",
            RuntimePayloadProvisioning = provideDestinations ? destinations : null,
            PreparedRuntime = prepared
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Same(prepared, Assert.Single(runtime.PreparedPayloads));
        Assert.Equal(temp.Artifact.MountDirectoryPath, Assert.Single(runtime.Options).MountedImagePath);
        Assert.Equal(temp.Artifact.Architecture, runtime.Options[0].Architecture);
        if (provideDestinations) Assert.Same(destinations.Connect, runtime.Options[0].Connect);
        Assert.Empty(runtime.DownloadProgressItems);
        Assert.False(prepared.IsDisposed);
    }
    private static Task<WinPeResult<WinPeCapabilityValidationResult>> AcceptCapabilities(
        WinPeImageInternationalizationOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(WinPeResult<WinPeCapabilityValidationResult>.Success(new([], [])));
    [Fact]
    public async Task CustomizeAsync_WhenPrimaryExceptionAndDiscardFail_PreservesBothAndOwnedMount()
    {
        using var temp = TempWinPeArtifact.Create();
        var original = new IOException("driver input failed");
        var runner = new FakeCustomizationRunner { FailDiscard = true };
        var service = new WinPeMountedImageCustomizationService(runner,
            new FakeDriverInjectionService { OnInject = _ => throw original },
            new FakeInternationalizationService(), new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(), new FakeWinRePreparationService());
        var result = await service.CustomizeAsync(new WinPeMountedImageCustomizationOptions
        {
            Artifact = temp.Artifact,
            Tools = temp.Tools,
            WinPeLanguage = "en-US",
            DriverPackagePaths = ["driver"]
        }, TestContext.Current.CancellationToken);
        Assert.Same(original, result.Error!.Exception);
        Assert.True(result.Error.RecoveryRequired);
        Assert.Equal(temp.Artifact.MountDirectoryPath, result.Error.OwnedMountPath);
        Assert.Equal(8, result.Error.CleanupDiagnostic!.ExitCode);
        Assert.True(Directory.Exists(temp.Artifact.MountDirectoryPath));
        Assert.Single(runner.Executions.Where(item => item.Arguments.Contains("/Discard")));
        Assert.DoesNotContain(runner.Executions, item => item.Arguments.Contains("/Commit"));
    }

    [Fact]
    public async Task CustomizeAsync_WhenStageReportsUncertainRecoveryWithoutException_DoesNotDiscard()
    {
        using var temp = TempWinPeArtifact.Create();
        var primary = new WinPeDiagnostic(WinPeErrorCodes.BuildFailed, "Native ownership remains uncertain.")
        {
            RecoveryRequired = true,
            RetainedPaths = [Path.Combine(temp.Artifact.MountDirectoryPath, "payload")]
        };
        var runner = new FakeCustomizationRunner();
        var service = new WinPeMountedImageCustomizationService(runner, new FakeDriverInjectionService(),
            new FakeInternationalizationService(WinPeResult.Failure(primary)), new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(), new FakeWinRePreparationService());
        var result = await service.CustomizeAsync(new WinPeMountedImageCustomizationOptions
        {
            Artifact = temp.Artifact,
            Tools = temp.Tools,
            WinPeLanguage = "en-US"
        }, TestContext.Current.CancellationToken);
        Assert.True(result.Error!.RecoveryRequired);
        Assert.Equal(temp.Artifact.MountDirectoryPath, result.Error.OwnedMountPath);
        Assert.DoesNotContain(runner.Executions, item => item.Arguments.Contains("/Discard"));
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
        public bool FailDiscard { get; init; }
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
            var execution = new WinPeProcessExecution
            {
                ExitCode = FailDiscard && argumentList.Contains("/Discard") ? 8 : 0,
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
        public List<WinPePreparedRuntimePayloads> PreparedPayloads { get; } = [];
        public bool RejectLegacyPlacement { get; init; }

        public Task<WinPeResult<WinPePreparedRuntimePayloads>> PrepareAsync(
            WinPeRuntimePayloadProvisioningOptions options, IProgress<WinPeDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This placement fixture must not acquire runtimes.");

        public Task<WinPeResult> ValidatePreparedAsync(WinPePreparedRuntimePayloads prepared,
            CancellationToken cancellationToken = default) => Task.FromResult(WinPeResult.Success());

        public Task<WinPeResult> ProvisionPreparedAsync(WinPePreparedRuntimePayloads prepared,
            WinPeRuntimePayloadProvisioningOptions destinations, CancellationToken cancellationToken = default)
        {
            PreparedPayloads.Add(prepared);
            Options.Add(destinations);
            return Task.FromResult(result ?? WinPeResult.Success());
        }
        public Task<WinPeResult> ProvisionAsync(
            WinPeRuntimePayloadProvisioningOptions options,
            IProgress<WinPeDownloadProgress>? downloadProgress = null,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            DownloadProgressItems.Add(downloadProgress);
            return Task.FromResult(RejectLegacyPlacement
                ? WinPeResult.Failure(WinPeErrorCodes.BuildFailed, "Prepared placement must not reacquire runtimes.")
                : result ?? WinPeResult.Success());
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
