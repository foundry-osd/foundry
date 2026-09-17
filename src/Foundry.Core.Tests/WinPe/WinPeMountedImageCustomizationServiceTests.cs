// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeMountedImageCustomizationServiceTests
{
    [Theory]
    [InlineData("/Mount-Image", true)]
    [InlineData("/Commit", false)]
    public async Task CustomizeAsync_WhenCancelledDuringServicing_FinishesStageAndUnmountsBeforeCancellation(string stage, bool expectsDiscard)
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        using var cancellation = new CancellationTokenSource();
        var runner = new FakeCustomizationRunner
        {
            OnRun = (arguments, token) =>
            {
                if (arguments.Contains(stage, StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
                Assert.False(token.CanBeCanceled);
            }
        };
        var internationalization = new FakeInternationalizationService();
        var service = new WinPeMountedImageCustomizationService(
            runner, new FakeDriverInjectionService(), internationalization,
            new FakeAssetProvisioningService(), new FakeRuntimePayloadProvisioningService(), new FakeBootImagePreparationService());

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                WinPeLanguage = "en-US"
            }, cancellation.Token));

        Assert.Equal(expectsDiscard, runner.Executions.Any(execution => execution.Arguments.Contains("/Discard", StringComparison.Ordinal)));
        Assert.Equal(expectsDiscard ? 0 : 1, internationalization.Options.Count);
        Assert.Equal(2, runner.Executions.Count);
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
        List<string> provisioningOrder = [];
        var assetProvisioning = new FakeAssetProvisioningService { OnProvision = () => provisioningOrder.Add("assets") };
        var runtimePayloadProvisioning = new FakeRuntimePayloadProvisioningService { OnProvision = () => provisioningOrder.Add("runtime") };
        var winRePreparation = new FakeBootImagePreparationService();
        var service = new WinPeMountedImageCustomizationService(
            runner,
            driverInjection,
            internationalization,
            assetProvisioning,
            runtimePayloadProvisioning,
            winRePreparation);
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
                    CurlExecutableSourcePath = Path.Combine(temp.RootPath, "curl.exe"),
                    IanaWindowsTimeZoneMapJson = "{}"
                },
                RuntimePayloadProvisioning = runtimePayloadOptions,
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(["runtime", "assets"], provisioningOrder);
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

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task CustomizeAsync_WhenInternationalizationFails_PreservesPrimaryFailureAfterDiscard(int discardExitCode)
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();

        var runner = new FakeCustomizationRunner { DiscardExitCode = discardExitCode };
        var diagnostic = new WinPeDiagnostic(WinPeErrorCodes.BuildFailed, "intl failed", "primary detail", stage: "Apply language", exitCode: 5, toolName: "dism.exe");
        var service = new WinPeMountedImageCustomizationService(
            runner,
            new FakeDriverInjectionService(),
            new FakeInternationalizationService(WinPeResult.Failure(diagnostic)),
            new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(),
            new FakeBootImagePreparationService());

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
        Assert.Equal(5, result.Error?.ExitCode);
        Assert.Equal("Apply language", result.Error?.Stage);
        Assert.Equal("dism.exe", result.Error?.ToolName);
        Assert.Contains("primary detail", result.Error?.Details);
        if (discardExitCode != 0)
        {
            Assert.Contains("Discard diagnostics:", result.Error?.Details);
        }
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
            new FakeBootImagePreparationService(new WinPeBootImagePreparationResult
            {
                DependencyFiles =
                [
                    new WinPeDependencyFile
                    {
                        FileName = "dmcmnutils.dll",
                        StagedPath = stagedDependency
                    }
                ]
            }));

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

    [Theory]
    [InlineData(WinPeBootImageSource.WinPe)]
    [InlineData(WinPeBootImageSource.WinReWifi)]
    public async Task CustomizeAsync_ForArm64_PreparesOnceAndPreservesGraphicsProvidedByOptionalComponents(WinPeBootImageSource bootImageSource)
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        string stagedPath = Path.Combine(temp.RootPath, "d3dcompiler_47.dll");
        File.WriteAllText(stagedPath, "graphics dependency");
        string system32 = Path.Combine(temp.Artifact.MountDirectoryPath, "Windows", "System32");
        Directory.CreateDirectory(system32);
        string shellPath = Path.Combine(system32, "winpeshl.ini");
        File.WriteAllText(shellPath, "original shell");
        var preparation = new FakeBootImagePreparationService(new WinPeBootImagePreparationResult
        {
            DependencyFiles =
            [
                new WinPeDependencyFile { FileName = "d3dcompiler_47.dll", StagedPath = stagedPath, OverwriteExisting = false },
                new WinPeDependencyFile { FileName = "dwrite.dll", StagedPath = stagedPath, OverwriteExisting = false }
            ]
        });
        var runner = new FakeCustomizationRunner();
        var service = new WinPeMountedImageCustomizationService(
            runner,
            new FakeDriverInjectionService(),
            new FakeInternationalizationService
            {
                OnApply = () =>
                {
                    Assert.False(File.Exists(Path.Combine(system32, "d3dcompiler_47.dll")));
                    File.WriteAllText(Path.Combine(system32, "dwrite.dll"), "optional component");
                }
            },
            new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(),
            preparation);

        WinPeResult result = await service.CustomizeAsync(new WinPeMountedImageCustomizationOptions
        {
            Artifact = temp.Artifact with { Architecture = WinPeArchitecture.Arm64 },
            Tools = temp.Tools,
            BootImageSource = bootImageSource,
            WinPeLanguage = "fr-FR",
            WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        WinPeBootImagePreparationOptions sourceOptions = Assert.Single(preparation.Options);
        Assert.Equal(bootImageSource, sourceOptions.BootImageSource);
        Assert.Equal(WinPeArchitecture.Arm64, sourceOptions.Artifact.Architecture);
        Assert.Equal("fr-FR", sourceOptions.WinPeLanguage);
        Assert.Equal(Path.Combine(temp.RootPath, "cache"), sourceOptions.CacheDirectoryPath);
        Assert.Equal("graphics dependency", File.ReadAllText(Path.Combine(system32, "d3dcompiler_47.dll")));
        Assert.Equal("optional component", File.ReadAllText(Path.Combine(system32, "dwrite.dll")));
        Assert.Equal(bootImageSource == WinPeBootImageSource.WinPe, File.Exists(shellPath));
        Assert.Single(runner.Executions, item => item.Arguments.Contains("/Mount-Image", StringComparison.Ordinal));
        Assert.Single(runner.Executions, item => item.Arguments.Contains("/Commit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CustomizeAsync_WhenStagedGraphicsFileIsMissing_DiscardsBootImageWithoutCommit()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        var runner = new FakeCustomizationRunner();
        var service = new WinPeMountedImageCustomizationService(
            runner,
            new FakeDriverInjectionService(),
            new FakeInternationalizationService(),
            new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(),
            new FakeBootImagePreparationService(new WinPeBootImagePreparationResult
            {
                DependencyFiles =
                [
                    new WinPeDependencyFile
                    {
                        FileName = "d3dcompiler_47.dll",
                        StagedPath = Path.Combine(temp.RootPath, "missing.dll"),
                        OverwriteExisting = false
                    }
                ]
            }));

        WinPeResult result = await service.CustomizeAsync(new WinPeMountedImageCustomizationOptions
        {
            Artifact = temp.Artifact with { Architecture = WinPeArchitecture.Arm64 },
            Tools = temp.Tools,
            WinPeLanguage = "en-US",
            WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("d3dcompiler_47.dll", result.Error?.Message);
        Assert.Single(runner.Executions, item => item.Arguments.Contains("/Discard", StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Executions, item => item.Arguments.Contains("/Commit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CustomizeAsync_WhenArm64SourcePreparationFails_DoesNotMountBootImage()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        var runner = new FakeCustomizationRunner();
        var diagnostic = new WinPeDiagnostic(WinPeErrorCodes.WinReExtractionFailed, "source unavailable");
        var service = new WinPeMountedImageCustomizationService(
            runner,
            new FakeDriverInjectionService(),
            new FakeInternationalizationService(),
            new FakeAssetProvisioningService(),
            new FakeRuntimePayloadProvisioningService(),
            new FakeBootImagePreparationService { Failure = diagnostic });

        WinPeResult result = await service.CustomizeAsync(new WinPeMountedImageCustomizationOptions
        {
            Artifact = temp.Artifact with { Architecture = WinPeArchitecture.Arm64 },
            Tools = temp.Tools,
            WinPeLanguage = "en-US",
            WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "cache")
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Same(diagnostic, result.Error);
        Assert.Empty(runner.Executions);
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
        public Action<string, CancellationToken>? OnRun { get; init; }
        public int DiscardExitCode { get; init; }
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            OnRun?.Invoke(arguments, cancellationToken);
            var execution = new WinPeProcessExecution
            {
                ExitCode = arguments.Contains("/Discard", StringComparison.Ordinal) ? DiscardExitCode : 0,
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
        public Action? OnApply { get; init; }

        public Task<WinPeResult> ApplyAsync(
            WinPeImageInternationalizationOptions options,
            CancellationToken cancellationToken = default)
        {
            OnApply?.Invoke();
            Options.Add(options);
            return Task.FromResult(result ?? WinPeResult.Success());
        }
    }

    private sealed class FakeAssetProvisioningService(WinPeResult? result = null) : IWinPeMountedImageAssetProvisioningService
    {
        public Action? OnProvision { get; init; }
        public List<WinPeMountedImageAssetProvisioningOptions> Options { get; } = [];

        public Task<WinPeResult> ProvisionAsync(
            WinPeMountedImageAssetProvisioningOptions options,
            CancellationToken cancellationToken = default)
        {
            OnProvision?.Invoke();
            Options.Add(options);
            return Task.FromResult(result ?? WinPeResult.Success());
        }
    }

    private sealed class FakeRuntimePayloadProvisioningService(WinPeResult? result = null) : IWinPeRuntimePayloadProvisioningService
    {
        public Action? OnProvision { get; init; }
        public Task<WinPeResult<WinPeRuntimePayloadProvisioningOptions>> PrepareAsync(
            WinPeRuntimePayloadProvisioningOptions options,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WinPeResult<WinPeRuntimePayloadProvisioningOptions>.Success(options));
        }
        public List<WinPeRuntimePayloadProvisioningOptions> Options { get; } = [];
        public List<IProgress<WinPeDownloadProgress>?> DownloadProgressItems { get; } = [];

        public Task<WinPeResult> ProvisionAsync(
            WinPeRuntimePayloadProvisioningOptions options,
            IProgress<WinPeDownloadProgress>? downloadProgress = null,
            CancellationToken cancellationToken = default)
        {
            OnProvision?.Invoke();
            Options.Add(options);
            DownloadProgressItems.Add(downloadProgress);
            return Task.FromResult(result ?? WinPeResult.Success());
        }
    }

    private sealed class FakeBootImagePreparationService(WinPeBootImagePreparationResult? result = null) : IWinPeBootImagePreparationService
    {
        public bool WasCalled => Options.Count > 0;
        public List<WinPeBootImagePreparationOptions> Options { get; } = [];
        public WinPeDiagnostic? Failure { get; init; }

        public Task<WinPeResult<WinPeBootImagePreparationResult>> PrepareAsync(
            WinPeBootImagePreparationOptions options,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            if (Failure is not null)
            {
                return Task.FromResult(WinPeResult<WinPeBootImagePreparationResult>.Failure(Failure));
            }
            return Task.FromResult(WinPeResult<WinPeBootImagePreparationResult>.Success(
                result ?? new WinPeBootImagePreparationResult
                {
                    DependencyFiles = []
                }));
        }
    }
}
