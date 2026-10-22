// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeWorkspacePreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_ResolvesDriversAndCustomizesImage()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        var driverResolution = new FakeDriverResolutionService(["drivers"]);
        var customization = new FakeMountedImageCustomizationService();
        var assetProvisioning = new WinPeMountedImageAssetProvisioningOptions
        {
            CurlExecutableSourcePath = Path.Combine(temp.RootPath, "curl.exe"),
            IanaWindowsTimeZoneMapJson = "{}"
        };
        var runtimePayloadProvisioning = new WinPeRuntimePayloadProvisioningOptions
        {
            WorkingDirectoryPath = Path.Combine(temp.RootPath, "runtime-work"),
            Connect = new WinPeRuntimePayloadApplicationOptions { IsEnabled = true }
        };
        var service = new WinPeWorkspacePreparationService(
            driverResolution,
            customization,
            new FakeBootImagePreparationService(),
            new WinPeToolResolver(() => null),
            new FakeBootExRunner("/bootex"));

        WinPeResult<WinPeWorkspacePreparationResult> result = await service.PrepareAsync(
            new WinPeWorkspacePreparationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                SignatureMode = WinPeSignatureMode.Pca2011,
                DriverCatalogUri = "https://example.test/catalog.xml",
                DriverVendors = [WinPeVendorSelection.Dell],
                WinPeLanguage = "en-US",
                AssetProvisioning = assetProvisioning,
                RuntimePayloadProvisioning = runtimePayloadProvisioning,
                WindowsSourceCacheDirectoryPath = Path.Combine(temp.RootPath, "sources"),
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "winre")
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Single(driverResolution.Requests);
        Assert.Single(customization.Options);
        Assert.Equal("drivers", Assert.Single(customization.Options[0].DriverPackagePaths));
        Assert.Same(assetProvisioning, customization.Options[0].AssetProvisioning);
        Assert.Same(runtimePayloadProvisioning, customization.Options[0].RuntimePayloadProvisioning);
        Assert.False(result.Value!.UseBootEx);
    }

    [Fact]
    public async Task PrepareAsync_WhenPca2023AndBootExUnsupported_ReturnsBootExUnsupported()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        var service = new WinPeWorkspacePreparationService(
            new FakeDriverResolutionService([]),
            new FakeMountedImageCustomizationService(),
            new FakeBootImagePreparationService(),
            new WinPeToolResolver(() => null),
            new FakeBootExRunner("MakeWinPEMedia help without support"));

        WinPeResult<WinPeWorkspacePreparationResult> result = await service.PrepareAsync(
            new WinPeWorkspacePreparationOptions
            {
                Artifact = temp.Artifact,
                Tools = temp.Tools,
                SignatureMode = WinPeSignatureMode.Pca2023,
                DriverCatalogUri = "https://example.test/catalog.xml",
                DriverVendors = [],
                WinPeLanguage = "en-US",
                WindowsSourceCacheDirectoryPath = Path.Combine(temp.RootPath, "sources"),
                WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "winre")
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.BootExUnsupported, result.Error?.Code);
    }

    [Theory]
    [InlineData(WinPeBootImageSource.WinRe, true)]
    [InlineData(WinPeBootImageSource.WinPe, true)]
    [InlineData(WinPeBootImageSource.WinRe, false)]
    public async Task PrepareAsync_InjectsWirelessSupplementOnlyIntoWinReWithWifi(WinPeBootImageSource bootImageSource, bool includeWirelessSupport)
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        var driverResolution = new FakeDriverResolutionService(["drivers"], ["wireless"]);
        var customization = new FakeMountedImageCustomizationService();
        var cause = new WinPeDiagnostic(WinPeErrorCodes.OperatingSystemCatalogFetchFailed, "offline");
        var preparation = new FakeBootImagePreparationService(new WinPeBootImagePreparationResult
        {
            BootImageSource = bootImageSource,
            FallbackCause = bootImageSource == WinPeBootImageSource.WinPe ? cause : null,
            DependencyFiles = []
        });
        var service = new WinPeWorkspacePreparationService(driverResolution, customization, preparation,
            new WinPeToolResolver(() => null), new FakeBootExRunner("/bootex"));

        WinPeResult<WinPeWorkspacePreparationResult> result = await service.PrepareAsync(CreateOptions(temp) with
        {
            IncludeWirelessSupport = includeWirelessSupport
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string[] expected = bootImageSource == WinPeBootImageSource.WinRe && includeWirelessSupport ? ["drivers", "wireless"] : ["drivers"];
        Assert.Equal(expected, Assert.Single(customization.Options).DriverPackagePaths);
        Assert.Equal(includeWirelessSupport, Assert.Single(preparation.Options).IncludeWirelessSupport);
        Assert.Equal(bootImageSource, result.Value!.BootImageSource);
        Assert.Equal(bootImageSource == WinPeBootImageSource.WinPe ? cause : null, result.Value.BootImageFallbackCause);
    }

    [Fact]
    public async Task PrepareAsync_WhenBootImagePreparationFails_DoesNotCustomize()
    {
        using TempWinPeArtifact temp = TempWinPeArtifact.Create();
        var customization = new FakeMountedImageCustomizationService();
        var failure = new WinPeDiagnostic(WinPeErrorCodes.WinReExtractionFailed, "ARM64 boot media requires the Windows Recovery Environment.");
        var service = new WinPeWorkspacePreparationService(new FakeDriverResolutionService([]), customization,
            new FakeBootImagePreparationService { Failure = failure }, new WinPeToolResolver(() => null), new FakeBootExRunner("/bootex"));

        WinPeResult<WinPeWorkspacePreparationResult> result = await service.PrepareAsync(CreateOptions(temp), TestContext.Current.CancellationToken);

        Assert.Same(failure, result.Error);
        Assert.Empty(customization.Options);
    }

    private static WinPeWorkspacePreparationOptions CreateOptions(TempWinPeArtifact temp) => new()
    {
        Artifact = temp.Artifact,
        Tools = temp.Tools,
        SignatureMode = WinPeSignatureMode.Pca2011,
        DriverCatalogUri = "https://example.test/catalog.xml",
        DriverVendors = [],
        WinPeLanguage = "en-US",
        WindowsSourceCacheDirectoryPath = Path.Combine(temp.RootPath, "sources"),
        WinReCacheDirectoryPath = Path.Combine(temp.RootPath, "winre")
    };

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
            string root = Path.Combine(Path.GetTempPath(), $"foundry-workspace-{Guid.NewGuid():N}");
            string media = Path.Combine(root, "media");
            string bootWim = Path.Combine(media, "sources", "boot.wim");
            Directory.CreateDirectory(Path.GetDirectoryName(bootWim)!);
            File.WriteAllText(bootWim, "wim");

            var artifact = new WinPeBuildArtifact
            {
                WorkingDirectoryPath = Path.Combine(root, "work"),
                MediaDirectoryPath = media,
                BootWimPath = bootWim,
                MountDirectoryPath = Path.Combine(root, "mount"),
                DriverWorkspacePath = Path.Combine(root, "drivers"),
                LogsDirectoryPath = Path.Combine(root, "logs"),
                MakeWinPeMediaPath = "MakeWinPEMedia.cmd",
                DismPath = "dism.exe",
                Architecture = WinPeArchitecture.X64
            };

            Directory.CreateDirectory(artifact.WorkingDirectoryPath);

            var tools = new WinPeToolPaths
            {
                KitsRootPath = root,
                MakeWinPeMediaPath = "MakeWinPEMedia.cmd",
                DismPath = "dism.exe"
            };

            return new TempWinPeArtifact(root, artifact, tools);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class FakeDriverResolutionService(IReadOnlyList<string> result, IReadOnlyList<string>? wirelessResult = null) : IWinPeDriverResolutionService
    {
        public List<WinPeDriverResolutionRequest> Requests { get; } = [];

        public Task<WinPeResult<IReadOnlyList<string>>> ResolveAsync(
            WinPeDriverResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(WinPeResult<IReadOnlyList<string>>.Success(request.IncludeWirelessSupplement ? wirelessResult ?? [] : result));
        }
    }

    private sealed class FakeMountedImageCustomizationService : IWinPeMountedImageCustomizationService
    {
        public List<WinPeMountedImageCustomizationOptions> Options { get; } = [];

        public Task<WinPeResult> CustomizeAsync(
            WinPeMountedImageCustomizationOptions options,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(WinPeResult.Success());
        }
    }

    private sealed class FakeBootImagePreparationService(WinPeBootImagePreparationResult? result = null) : IWinPeBootImagePreparationService
    {
        public List<WinPeBootImagePreparationOptions> Options { get; } = [];
        public WinPeDiagnostic? Failure { get; init; }

        public Task<WinPeResult<WinPeBootImagePreparationResult>> PrepareAsync(
            WinPeBootImagePreparationOptions options,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(Failure is not null
                ? WinPeResult<WinPeBootImagePreparationResult>.Failure(Failure)
                : WinPeResult<WinPeBootImagePreparationResult>.Success(result ?? new WinPeBootImagePreparationResult { DependencyFiles = [] }));
        }
    }

    private sealed class FakeBootExRunner(string helpOutput) : IWinPeProcessRunner
    {
        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            throw new NotSupportedException();
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
            return Task.FromResult(new WinPeProcessExecution
            {
                FileName = scriptPath,
                Arguments = scriptArguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = helpOutput
            });
        }
    }
}
