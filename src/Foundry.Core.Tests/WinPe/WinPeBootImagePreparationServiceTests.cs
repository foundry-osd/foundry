// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Utilities.Imaging;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeBootImagePreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_WhenCurrentWinReIsCached_DoesNotDownloadOrExtractAgain()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        byte[] source = Encoding.UTF8.GetBytes("windows-source-original");
        var handler = new StaticCatalogHandler(CreateCatalogXml(Convert.ToHexString(SHA256.HashData(source))), source);
        using var client = new HttpClient(handler);
        string legacy = Path.Combine(temp.Path, "legacy");
        Directory.CreateDirectory(legacy);
        await File.WriteAllTextAsync(Path.Combine(legacy, "old-24h2.esd"), "old", TestContext.Current.CancellationToken);
        var runner = new FakeWinPeProcessRunner();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            WinPeResult<WinPeBootImagePreparationResult> result = await PrepareAsync(temp.Path, client, runner,
                options => options with { LegacySourceDirectoryPath = legacy, IncludeWirelessSupport = true });

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Equal(WinPeBootImageSource.WinRe, result.Value!.BootImageSource);
            Assert.Equal("winre", await File.ReadAllTextAsync(BootWim(temp.Path), TestContext.Current.CancellationToken));
            Assert.Equal(2, result.Value.DependencyFiles.Count(file => file.OverwriteExisting));
        }

        Assert.Equal(1, handler.PayloadRequests);
        Assert.Single(runner.Executions, execution => execution.Arguments.Contains("/Mount-Image", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(temp.Path, "sources")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(legacy));
    }

    [Fact]
    public async Task PrepareAsync_AfterExtraction_KeepsSourcePackagesLeasedByAnotherProcess()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        string leasedEntry = Path.Combine(temp.Path, "sources", "LEASED");
        string idleEntry = Path.Combine(temp.Path, "sources", "IDLE");
        Directory.CreateDirectory(Path.Combine(leasedEntry, "generation-1"));
        Directory.CreateDirectory(Path.Combine(idleEntry, "generation-1"));
        await File.WriteAllTextAsync(Path.Combine(leasedEntry, "generation-1", "other.esd"), "in use", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(idleEntry, "generation-1", "old.esd"), "old", TestContext.Current.CancellationToken);
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty), Encoding.UTF8.GetBytes("source")));

        using (new FileStream(Path.Combine(leasedEntry, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True((await PrepareAsync(temp.Path, client, new FakeWinPeProcessRunner())).IsSuccess);
        }

        Assert.True(File.Exists(Path.Combine(leasedEntry, "generation-1", "other.esd")));
        Assert.False(Directory.Exists(idleEntry));
    }

    [Fact]
    public async Task PrepareAsync_WhenNewerSourceIsPublished_ReplacesPreviousEntry()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner();
        foreach (string content in new[] { "source-a", "source-b" })
        {
            byte[] source = Encoding.UTF8.GetBytes(content);
            using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(Convert.ToHexString(SHA256.HashData(source))), source));
            Assert.True((await PrepareAsync(temp.Path, client, runner)).IsSuccess);
        }

        Assert.Equal(2, runner.Executions.Count(execution => execution.Arguments.Contains("/Mount-Image", StringComparison.Ordinal)));
        Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, "winre"), "x64-en-us-*"));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("download")]
    [InlineData("extraction")]
    public async Task PrepareAsync_WhenNewestWinReCannotBeObtained_UsesPreviousCachedEntry(string failure)
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        byte[] previous = Encoding.UTF8.GetBytes("source-a");
        using (var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(Convert.ToHexString(SHA256.HashData(previous))), previous)))
        {
            Assert.True((await PrepareAsync(temp.Path, client, new FakeWinPeProcessRunner())).IsSuccess);
        }

        byte[] newest = Encoding.UTF8.GetBytes("source-b");
        var handler = new StaticCatalogHandler(CreateCatalogXml(Convert.ToHexString(SHA256.HashData(newest))), newest)
        {
            FailCatalog = failure == "catalog",
            FailPayload = failure == "download"
        };
        using var failingClient = new HttpClient(handler);
        var runner = new FakeWinPeProcessRunner { FailingOperation = failure == "extraction" ? "/Mount-Image" : null, ExitCode = 2 };
        await File.WriteAllTextAsync(BootWim(temp.Path), "original", TestContext.Current.CancellationToken);

        WinPeResult<WinPeBootImagePreparationResult> result = await PrepareAsync(temp.Path, failingClient, runner);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(WinPeBootImageSource.WinRe, result.Value!.BootImageSource);
        Assert.Equal("winre", await File.ReadAllTextAsync(BootWim(temp.Path), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PrepareAsync_ForX64_WhenNoWinReIsAvailable_KeepsAdkImageWithoutWirelessFilesAndReportsCause()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty)) { FailCatalog = true });

        WinPeResult<WinPeBootImagePreparationResult> result = await PrepareAsync(temp.Path, client, new FakeWinPeProcessRunner(),
            options => options with { IncludeWirelessSupport = true });

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(WinPeBootImageSource.WinPe, result.Value!.BootImageSource);
        Assert.Equal(WinPeErrorCodes.OperatingSystemCatalogFetchFailed, result.Value.FallbackCause?.Code);
        Assert.Empty(result.Value.DependencyFiles);
        Assert.Equal("original", await File.ReadAllTextAsync(BootWim(temp.Path), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64)]
    [InlineData(WinPeArchitecture.Arm64)]
    public async Task PrepareAsync_WhenSourceDownloadIsRefusedWithEmptyCache_FallsBackOnX64AndFailsOnArm64(WinPeArchitecture architecture)
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        string catalog = CreateFallbackCatalogXml();
        if (architecture == WinPeArchitecture.Arm64) catalog = catalog.Replace("<Architecture>x64</Architecture>", "<Architecture>arm64</Architecture>", StringComparison.Ordinal);
        var handler = new StaticCatalogHandler(catalog, Encoding.UTF8.GetBytes("source")) { FailPayload = true };
        using var client = new HttpClient(handler);
        var runner = new FakeWinPeProcessRunner();

        WinPeResult<WinPeBootImagePreparationResult> result = await PrepareAsync(temp.Path, client, runner,
            options => options with { Artifact = options.Artifact with { Architecture = architecture } });

        Assert.Equal(2, handler.PayloadRequests);
        Assert.Empty(runner.Executions);
        Assert.Equal("original", await File.ReadAllTextAsync(BootWim(temp.Path), TestContext.Current.CancellationToken));
        WinPeDiagnostic? cause;
        if (architecture == WinPeArchitecture.X64)
        {
            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Equal(WinPeBootImageSource.WinPe, result.Value!.BootImageSource);
            cause = result.Value.FallbackCause;
        }
        else
        {
            Assert.False(result.IsSuccess);
            Assert.Contains("ARM64", result.Error?.Message, StringComparison.Ordinal);
            cause = result.Error;
        }

        Assert.Equal(WinPeErrorCodes.DownloadFailed, cause?.Code);
        Assert.Contains("Failed to acquire a verified Windows source package.", cause?.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_WhenDownloadedSourceFailsHashVerification_ReportsHashMismatchAsCause()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        string expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("expected")));
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(expectedHash), Encoding.UTF8.GetBytes("damaged")));

        WinPeResult<WinPeBootImagePreparationResult> result = await PrepareAsync(temp.Path, client, new FakeWinPeProcessRunner());

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(WinPeBootImageSource.WinPe, result.Value!.BootImageSource);
        Assert.Equal(WinPeErrorCodes.HashMismatch, result.Value.FallbackCause?.Code);
    }

    [Fact]
    public async Task PrepareAsync_ForArm64_WhenNoWinReIsAvailable_FailsWithExplicitError()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty)) { FailCatalog = true });

        WinPeResult<WinPeBootImagePreparationResult> result = await PrepareAsync(temp.Path, client, new FakeWinPeProcessRunner(),
            options => options with { Artifact = options.Artifact with { Architecture = WinPeArchitecture.Arm64 } });

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.OperatingSystemCatalogFetchFailed, result.Error?.Code);
        Assert.Contains("ARM64", result.Error?.Message, StringComparison.Ordinal);
        Assert.Contains("Failed to download the operating system catalog.", result.Error?.Details, StringComparison.Ordinal);
        Assert.Equal("original", await File.ReadAllTextAsync(BootWim(temp.Path), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PrepareAsync_WithoutWirelessSupport_StillReplacesBootImageWithWinRe()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty), Encoding.UTF8.GetBytes("source")));

        WinPeResult<WinPeBootImagePreparationResult> result = await PrepareAsync(temp.Path, client, new FakeWinPeProcessRunner());

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(WinPeBootImageSource.WinRe, result.Value!.BootImageSource);
        Assert.Empty(result.Value.DependencyFiles);
        Assert.Equal("winre", await File.ReadAllTextAsync(BootWim(temp.Path), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PrepareAsync_WhenCachedWinReIsCorrupted_RebuildsItFromSource()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        byte[] source = Encoding.UTF8.GetBytes("source-a");
        var handler = new StaticCatalogHandler(CreateCatalogXml(Convert.ToHexString(SHA256.HashData(source))), source);
        using var client = new HttpClient(handler);
        var runner = new FakeWinPeProcessRunner();
        Assert.True((await PrepareAsync(temp.Path, client, runner)).IsSuccess);
        string entry = Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, "winre"), "x64-en-us-*"));
        await File.WriteAllTextAsync(Path.Combine(entry, "winre.wim"), "tampered", TestContext.Current.CancellationToken);

        WinPeResult<WinPeBootImagePreparationResult> result = await PrepareAsync(temp.Path, client, runner);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(2, handler.PayloadRequests);
        Assert.Equal("winre", await File.ReadAllTextAsync(BootWim(temp.Path), TestContext.Current.CancellationToken));
    }

    private static string BootWim(string root) => Path.Combine(root, "work", "media", "sources", "boot.wim");

    /// <summary>Runs one preparation with a workspace under <paramref name="root"/>, separate download and WinRE caches, and an ADK boot.wim.</summary>
    private static async Task<WinPeResult<WinPeBootImagePreparationResult>> PrepareAsync(string root, HttpClient client, FakeWinPeProcessRunner runner,
        Func<WinPeBootImagePreparationOptions, WinPeBootImagePreparationOptions>? configure = null)
    {
        string work = Path.Combine(root, "work");
        Directory.CreateDirectory(Path.GetDirectoryName(BootWim(root))!);
        if (!File.Exists(BootWim(root))) await File.WriteAllTextAsync(BootWim(root), "original", TestContext.Current.CancellationToken);
        var options = new WinPeBootImagePreparationOptions
        {
            Artifact = new() { Architecture = WinPeArchitecture.X64, WorkingDirectoryPath = work, BootWimPath = BootWim(root) },
            Tools = new() { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.Combine(root, "sources"),
            WinReCacheDirectoryPath = Path.Combine(root, "winre")
        };
        return await CreateService(runner, client).PrepareAsync(configure?.Invoke(options) ?? options, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("/Export-Image")]
    [InlineData("/Mount-Image")]
    public async Task PrepareAsync_WhenServicingFailsDuringCancellation_PreservesFailureWithoutTryingFallback(string stage)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-source-failure-{Guid.NewGuid():N}");
        string cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        byte[] source = Encoding.UTF8.GetBytes("cached source");
        await File.WriteAllBytesAsync(Path.Combine(cache, "source.esd"), source, TestContext.Current.CancellationToken);
        string catalog = CreateCatalogXml(Convert.ToHexString(SHA256.HashData(source)));
        string fallback = catalog.Replace("<Catalog>", "", StringComparison.Ordinal).Replace("</Catalog>", "", StringComparison.Ordinal)
            .Replace("Professional", "Enterprise", StringComparison.Ordinal).Replace("CLIENTCONSUMER", "CLIENTBUSINESS", StringComparison.Ordinal)
            .Replace("source.esd", "enterprise.esd", StringComparison.Ordinal);
        catalog = catalog.Replace("</Catalog>", fallback + "</Catalog>", StringComparison.Ordinal);
        var runner = new FakeWinPeProcessRunner
        {
            FailingOperation = stage,
            ExitCode = 2,
            OnRun = (arguments, _) => { if (arguments.Contains(stage, StringComparison.Ordinal)) caller.Cancel(); }
        };
        using var client = new HttpClient(new StaticCatalogHandler(catalog, source));
        var service = CreateService(runner, client, new WinPeWorkspaceCleanupService(() => []));
        try
        {
            WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
            {
                Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(root, "boot.wim"), WorkingDirectoryPath = root },
                Tools = new WinPeToolPaths { DismPath = "dism.exe" },
                WinPeLanguage = "en-US",
                CacheDirectoryPath = cache,
                WinReCacheDirectoryPath = cache + "-winre",
            }, caller.Token);
            Assert.False(result.IsSuccess);
            Assert.Equal(2, result.Error?.ExitCode);
            Assert.Equal(WinPeFailureReasons.NonZeroExit, result.Error?.FailureReason);
            Assert.Single(runner.Executions, execution => execution.Arguments.Contains(stage, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAsync_WhenCatalogRequestIsCancelled_PropagatesCallerCancellation()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new CancelledCatalogHandler(cancellation));
        var service = CreateService(new FakeWinPeProcessRunner(), client, new WinPeWorkspaceCleanupService(() => []));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(new WinPeBootImagePreparationOptions
        {
            Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.X64, BootWimPath = "boot.wim", WorkingDirectoryPath = temp.Path },
            Tools = new WinPeToolPaths { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.Combine(temp.Path, "sources"),
            WinReCacheDirectoryPath = Path.Combine(temp.Path, "winre"),
        }, cancellation.Token));
    }

    [Theory]
    [InlineData("/Export-Image", false)]
    [InlineData("/Mount-Image", true)]
    public async Task PrepareAsync_WhenCancelledDuringServicing_FinishesStageAndDiscardsBeforeCancellation(string stage, bool expectsDiscard)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-source-cancel-{Guid.NewGuid():N}");
        string cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "source.esd"), "cached source");
        using var cancellation = new CancellationTokenSource();
        var runner = new FakeWinPeProcessRunner
        {
            OnRun = (arguments, token) =>
            {
                if (arguments.Contains(stage, StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                    Assert.False(token.CanBeCanceled);
                }
                if (arguments.Contains("/Discard", StringComparison.Ordinal))
                {
                    Assert.True(token.CanBeCanceled);
                    Assert.False(token.IsCancellationRequested);
                }
            }
        };
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty)));
        var service = CreateService(runner, client, new WinPeWorkspaceCleanupService(() => []));
        try
        {
            OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() => service.PrepareAsync(new WinPeBootImagePreparationOptions
            {
                Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(root, "boot.wim"), WorkingDirectoryPath = root },
                Tools = new WinPeToolPaths { DismPath = "dism.exe" },
                WinPeLanguage = "en-US",
                CacheDirectoryPath = cache,
                WinReCacheDirectoryPath = cache + "-winre",
            }, cancellation.Token));
            Assert.Equal(cancellation.Token, observed.CancellationToken);
            Assert.Equal(expectsDiscard, runner.Executions.Any(execution => execution.Arguments.Contains("/Discard", StringComparison.Ordinal)));
            Assert.False(File.Exists(Path.Combine(root, "boot.wim")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_WhenCancelledAfterMountAndCleanupFails_ReturnsFinalCleanupFailure(bool exitUnconfirmed)
    {
        using var directory = new TestUtilities.TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        int discards = 0;
        var runner = new FakeWinPeProcessRunner
        {
            FailingOperation = "/Discard",
            ExitCode = 9,
            OnRun = (arguments, token) =>
            {
                if (arguments.Contains("/Mount-Image", StringComparison.Ordinal)) cancellation.Cancel();
                if (arguments.Contains("/Discard", StringComparison.Ordinal))
                {
                    discards++;
                    Assert.False(token.IsCancellationRequested);
                    if (exitUnconfirmed) throw new OperationCanceledException("DISM cleanup exit remains unconfirmed.");
                }
            }
        };
        using var client = new HttpClient(new StaticCatalogHandler(CreateFallbackCatalogXml(), Encoding.UTF8.GetBytes("source")));
        var service = CreateService(runner, client, new WinPeWorkspaceCleanupService(() => []));
        WinPeResult<WinPeBootImagePreparationResult>? result = null;

        Exception? observed = await Record.ExceptionAsync(async () => result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
        {
            Artifact = new() { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(directory.Path, "boot.wim"), WorkingDirectoryPath = directory.Path },
            Tools = new() { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.Combine(directory.Path, "cache"),
            WinReCacheDirectoryPath = Path.Combine(directory.Path, "cache") + "-winre"
        }, cancellation.Token));

        Assert.Null(observed);
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimUnmountFailed, result.Error?.Code);
        Assert.Equal(exitUnconfirmed ? WinPeMountCleanupStatus.ExitUnconfirmed : WinPeMountCleanupStatus.Failed, result.Error?.MountCleanupStatus);
        Assert.Contains("cancel", result.Error?.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, discards);
        Assert.Single(runner.Executions, execution => execution.Arguments.Contains("/Mount-Image", StringComparison.Ordinal));
        if (exitUnconfirmed)
        {
            Assert.Null(result.Error?.ExitCode);
            Assert.Contains("exit remains unconfirmed", result.Error?.Details, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory.Path, ".foundry-mount-cleanup-*.pending"));
        }
        else Assert.Equal(9, result.Error?.ExitCode);
    }

    [Theory]
    [InlineData("x64", "en-us", 26100)]
    [InlineData("arm64", "fr-fr", 26100)]
    [InlineData("arm64", "en-us", 22631)]
    [InlineData("arm64", "en-us", 27000)]
    public void SelectCatalogCandidates_RejectsIncompatibleArm64Sources(string architecture, string language, int build)
    {
        string catalog = CreateCatalogXml(string.Empty)
            .Replace("<Architecture>x64</Architecture>", $"<Architecture>{architecture}</Architecture>")
            .Replace("<LanguageCode>en-us</LanguageCode>", $"<LanguageCode>{language}</LanguageCode>")
            .Replace("<BuildMajor>26100</BuildMajor>", $"<BuildMajor>{build}</BuildMajor>");

        var result = WinPeBootImagePreparationService.SelectCatalogCandidates(catalog, WinPeArchitecture.Arm64, "en-US");

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WinReSourceSelectionFailed, result.Error?.Code);
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, null, false)]
    [InlineData(true, "dwrite.dll", false)]
    [InlineData(true, "dwrite.dll", true)]
    public async Task PrepareAsync_ForArm64_StagesGraphicsOrFailsBeforeReplacement(bool includeWirelessSupport, string? invalidFile, bool wrongArchitecture)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-arm64-{Guid.NewGuid():N}");
        string bootWim = Path.Combine(root, "boot.wim");
        string cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        File.WriteAllText(bootWim, "original");
        File.WriteAllText(Path.Combine(cache, "source.esd"), "cached source");
        string catalog = CreateCatalogXml(string.Empty).Replace("<Architecture>x64</Architecture>", "<Architecture>arm64</Architecture>");
        var runner = new FakeWinPeProcessRunner
        {
            InvalidGraphicsFile = invalidFile,
            WrongGraphicsArchitecture = wrongArchitecture
        };
        var service = CreateService(runner, new HttpClient(new StaticCatalogHandler(catalog)), new WinPeWorkspaceCleanupService(() => []));

        try
        {
            var result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
            {
                Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.Arm64, BootWimPath = bootWim, WorkingDirectoryPath = root },
                Tools = new WinPeToolPaths { DismPath = "dism.exe" },
                WinPeLanguage = "en-US",
                CacheDirectoryPath = cache,
                WinReCacheDirectoryPath = cache + "-winre",
                IncludeWirelessSupport = includeWirelessSupport
            }, TestContext.Current.CancellationToken);

            Assert.Single(runner.Executions, execution => execution.Arguments.Contains("/Export-Image", StringComparison.Ordinal));
            Assert.Single(runner.Executions, execution => execution.Arguments.Contains("/Mount-Image", StringComparison.Ordinal));
            Assert.Single(runner.Executions, execution => execution.Arguments.Contains("/Discard", StringComparison.Ordinal));
            if (invalidFile is not null)
            {
                Assert.False(result.IsSuccess);
                Assert.Contains(invalidFile, result.Error?.Details);
                Assert.Equal("original", File.ReadAllText(bootWim));
                return;
            }

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Equal("winre", File.ReadAllText(bootWim));
            Assert.Equal(new[] { "d3d9.dll", "d3dcompiler_47.dll", "dwrite.dll", "winmm.dll" },
                result.Value!.DependencyFiles.Where(file => !file.OverwriteExisting).Select(file => file.FileName).Order(StringComparer.Ordinal));
            Assert.Equal(includeWirelessSupport ? 2 : 0,
                result.Value.DependencyFiles.Count(file => file.OverwriteExisting));
            Assert.All(result.Value.DependencyFiles, file => Assert.True(File.Exists(file.StagedPath)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectCatalogCandidates_PrefersNewestServicingBranchBuildAndUbr()
    {
        string Item(string releaseId, int build, int ubr, string clientType, string channel, string fileName) => $"""
            <Item>
              <WindowsRelease>11</WindowsRelease>
              <ReleaseId>{releaseId}</ReleaseId>
              <BuildMajor>{build}</BuildMajor>
              <BuildUbr>{ubr}</BuildUbr>
              <Architecture>arm64</Architecture>
              <LanguageCode>en-us</LanguageCode>
              <ClientType>{clientType}</ClientType>
              <LicenseChannel>{channel}</LicenseChannel>
              <FileName>{fileName}</FileName>
              <Url>https://example.test/{fileName}</Url>
              <Sha256>abc</Sha256>
            </Item>
            """;
        string catalogXml = "<Catalog>" +
            Item("24H2", 26100, 4349, "CLIENTCONSUMER", "RET", "24h2.esd") +
            Item("26H2", 26300, 9457, "CLIENTCONSUMER", "RET", "26h2-old.esd") +
            Item("26H2", 26300, 9711, "CLIENTCONSUMER", "RET", "26h2.esd") +
            Item("25H2", 26200, 9457, "CLIENTCONSUMER", "RET", "25h2.esd") +
            Item("27H2", 27000, 1, "CLIENTCONSUMER", "RET", "unsupported.esd") +
            Item("25H2", 26200, 9457, "CLIENTBUSINESS", "VOL", "25h2-business.esd") +
            "</Catalog>";

        WinPeResult<IReadOnlyList<WindowsSourceCandidate>> result =
            WinPeBootImagePreparationService.SelectCatalogCandidates(catalogXml, WinPeArchitecture.Arm64, "en-US");

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Collection(
            result.Value!,
            candidate => Assert.Equal("26h2.esd", candidate.Source.FileName),
            candidate => Assert.Equal("25h2-business.esd", candidate.Source.FileName));
    }

    [Fact]
    public void SelectCatalogCandidates_FiltersArchitectureAndLanguage()
    {
        const string catalogXml = """
                                  <Catalog>
                                    <Item>
                                      <WindowsRelease>11</WindowsRelease>
                                      <ReleaseId>24H2</ReleaseId>
                                      <BuildMajor>26100</BuildMajor>
                                      <BuildUbr>2454</BuildUbr>
                                      <Architecture>x64</Architecture>
                                      <LanguageCode>fr-fr</LanguageCode>
                                      <Edition>Professional</Edition>
                                      <ClientType>CLIENTCONSUMER</ClientType>
                                      <LicenseChannel>RET</LicenseChannel>
                                      <FileName>consumer.esd</FileName>
                                      <Url>https://example.test/consumer.esd</Url>
                                      <Sha256>abc</Sha256>
                                    </Item>
                                    <Item>
                                      <WindowsRelease>11</WindowsRelease>
                                      <ReleaseId>24H2</ReleaseId>
                                      <BuildMajor>26100</BuildMajor>
                                      <BuildUbr>2454</BuildUbr>
                                      <Architecture>x64</Architecture>
                                      <LanguageCode>fr-fr</LanguageCode>
                                      <Edition>Enterprise</Edition>
                                      <ClientType>CLIENTBUSINESS</ClientType>
                                      <LicenseChannel>VOL</LicenseChannel>
                                      <FileName>business.esd</FileName>
                                      <Url>https://example.test/business.esd</Url>
                                      <Sha256>def</Sha256>
                                    </Item>
                                    <Item>
                                      <WindowsRelease>11</WindowsRelease>
                                      <ReleaseId>25H2</ReleaseId>
                                      <BuildMajor>26200</BuildMajor>
                                      <BuildUbr>5337</BuildUbr>
                                      <Architecture>x64</Architecture>
                                      <LanguageCode>de-de</LanguageCode>
                                      <Edition>Professional</Edition>
                                      <ClientType>CLIENTCONSUMER</ClientType>
                                      <LicenseChannel>RET</LicenseChannel>
                                      <FileName>old.esd</FileName>
                                      <Url>https://example.test/old.esd</Url>
                                      <Sha256>ghi</Sha256>
                                    </Item>
                                  </Catalog>
                                  """;

        WinPeResult<IReadOnlyList<WindowsSourceCandidate>> result =
            WinPeBootImagePreparationService.SelectCatalogCandidates(catalogXml, WinPeArchitecture.X64, "fr-FR");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Collection(
            result.Value,
            candidate =>
            {
                Assert.Equal("Pro", candidate.RequestedEdition);
                Assert.Equal("consumer.esd", candidate.Source.FileName);
            },
            candidate =>
            {
                Assert.Equal("Enterprise", candidate.RequestedEdition);
                Assert.Equal("business.esd", candidate.Source.FileName);
            });
    }

    [Theory]
    [InlineData("Pro", "professional", "Windows 11 Professionnel", 9)]
    [InlineData("Enterprise", "Enterprise", "Windows 11 Entreprise", 6)]
    public void ResolveImageIndexFromMetadata_SelectsExactEditionInsteadOfNVariant(string edition, string editionId, string name, int index)
    {
        WindowsImageMetadata[] images =
        [
            Image(1, string.Empty, "Windows Setup Media"),
            Image(2, editionId + "N", "Windows 11 " + edition),
            Image(index, editionId, name)
        ];
        WinPeResult<int> result = WinPeBootImagePreparationService.ResolveImageIndexFromMetadata(images, edition);

        Assert.True(result.IsSuccess);
        Assert.Equal(index, result.Value);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("n-only")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("invalid-index")]
    public void ResolveImageIndexFromMetadata_RejectsMissingAmbiguousOrInvalidEdition(string scenario)
    {
        WindowsImageMetadata[] images = scenario switch
        {
            "missing" => [Image(6, "Core", "Windows 11 Pro")],
            "n-only" => [Image(6, "ProfessionalN", "Windows 11 Pro")],
            "duplicate" => [Image(6, "Professional"), Image(9, "Professional")],
            "invalid-index" => [Image(0, "Professional")],
            _ => []
        };
        WinPeResult<int> result = WinPeBootImagePreparationService.ResolveImageIndexFromMetadata(images, "Pro");

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WinReIndexResolutionFailed, result.Error?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    public void ResolveImageIndexFromMetadata_RejectsUnsupportedEdition(string edition)
    {
        WinPeResult<int> result = WinPeBootImagePreparationService.ResolveImageIndexFromMetadata([Image(6, "Professional")], edition);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_WhenMetadataReadFails_DoesNotExport(bool cancelled)
    {
        using var directory = new TestUtilities.TemporaryDirectory();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty), Encoding.UTF8.GetBytes("source")));
        var runner = new FakeWinPeProcessRunner();
        var failure = new COMException("Native inspection failed.", unchecked((int)0x80070002));
        var service = CreateService(runner, client, readImageInfo: (_, token) =>
        {
            Assert.Equal(caller.Token, token);
            if (!cancelled) throw failure;
            caller.Cancel();
            return Task.FromCanceled<IReadOnlyList<WindowsImageMetadata>>(token);
        });
        var options = new WinPeBootImagePreparationOptions
        {
            Artifact = new() { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(directory.Path, "boot.wim"), WorkingDirectoryPath = directory.Path },
            Tools = new() { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.Combine(directory.Path, "cache"),
            WinReCacheDirectoryPath = Path.Combine(directory.Path, "cache") + "-winre"
        };

        if (cancelled)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(options, caller.Token));
        }
        else
        {
            WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(options, caller.Token);
            Assert.Equal(WinPeBootImageSource.WinPe, result.Value?.BootImageSource);
            WinPeDiagnostic? cause = result.Value?.FallbackCause;
            Assert.Same(failure, cause?.Exception);
            Assert.Equal("Inspect Windows source image", cause?.Stage);
            Assert.Null(cause?.ExitCode);
            Assert.Null(cause?.ToolName);
        }

        Assert.Empty(runner.Executions);
        Assert.False(File.Exists(options.Artifact.BootWimPath));
    }

    [Fact]
    public async Task PrepareAsync_WhenProExportFails_UsesEnterpriseMetadataIndex()
    {
        using var directory = new TestUtilities.TemporaryDirectory();
        string catalog = CreateCatalogXml(string.Empty);
        string fallback = catalog.Replace("<Catalog>", "", StringComparison.Ordinal).Replace("</Catalog>", "", StringComparison.Ordinal)
            .Replace("Professional", "Enterprise", StringComparison.Ordinal).Replace("CLIENTCONSUMER", "CLIENTBUSINESS", StringComparison.Ordinal)
            .Replace("source.esd", "enterprise.esd", StringComparison.Ordinal);
        catalog = catalog.Replace("</Catalog>", fallback + "</Catalog>", StringComparison.Ordinal);
        using var client = new HttpClient(new StaticCatalogHandler(catalog, Encoding.UTF8.GetBytes("source")));
        var runner = new FakeWinPeProcessRunner { FailingOperation = "/SourceIndex:9", ExitCode = 112 };
        var inspectedSources = new List<string>();
        var service = CreateService(runner, client, readImageInfo: (path, token) =>
        {
            Assert.Equal(TestContext.Current.CancellationToken, token);
            Assert.True(File.Exists(path));
            inspectedSources.Add(Path.GetFileName(path));
            IReadOnlyList<WindowsImageMetadata> images = Path.GetFileName(path) == "enterprise.esd"
                ? [Image(7, "EnterpriseN", "Windows 11 Entreprise N"), Image(6, "Enterprise", "Windows 11 Entreprise")]
                : [Image(10, "ProfessionalN", "Windows 11 Professionnel N"), Image(9, "Professional", "Windows 11 Professionnel")];
            return Task.FromResult(images);
        });

        WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
        {
            Artifact = new() { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(directory.Path, "boot.wim"), WorkingDirectoryPath = directory.Path },
            Tools = new() { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.Combine(directory.Path, "cache"),
            WinReCacheDirectoryPath = Path.Combine(directory.Path, "cache") + "-winre",
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(new[] { "source.esd", "enterprise.esd" }, inspectedSources);
        Assert.Collection(runner.Executions.Where(execution => execution.Arguments.Contains("/Export-Image", StringComparison.Ordinal)),
            execution => Assert.Contains("/SourceIndex:9 ", execution.Arguments, StringComparison.Ordinal),
            execution => Assert.Contains("/SourceIndex:6 ", execution.Arguments, StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Executions, execution => execution.Arguments.Contains("/Get-ImageInfo", StringComparison.Ordinal));
        Assert.Equal("winre", await File.ReadAllTextAsync(Path.Combine(directory.Path, "boot.wim"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PrepareDependencyFiles_ForX64_CopiesWirelessFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-winre-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mounted");
        string sourceSystem32Path = Path.Combine(mountedImagePath, "Windows", "System32");
        string dependencyPath = Path.Combine(root, "wireless-support");
        Directory.CreateDirectory(sourceSystem32Path);
        File.WriteAllText(Path.Combine(sourceSystem32Path, "dmcmnutils.dll"), "dm");
        File.WriteAllText(Path.Combine(sourceSystem32Path, "mdmregistration.dll"), "mdm");

        try
        {
            WinPeResult result = WinPeBootImagePreparationService.PrepareDependencyFiles(mountedImagePath, dependencyPath, WinPeArchitecture.X64);

            Assert.True(result.IsSuccess);
            Assert.Equal(["dmcmnutils.dll", "mdmregistration.dll"], Directory.GetFiles(dependencyPath).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, 0, true)]
    [InlineData("/Export-Image", 2, true)]
    [InlineData("/Mount-Image", 2, true)]
    [InlineData(null, 0, false)]
    public async Task PrepareAsync_ValidatesProcessResultsAndExportedImage(string? failingOperation, int exitCode, bool createExport)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-winre-replace-{Guid.NewGuid():N}");
        string workingPath = Path.Combine(root, "workspace");
        string mediaPath = Path.Combine(workingPath, "media");
        string sourcesPath = Path.Combine(mediaPath, "sources");
        string cachePath = Path.Combine(root, "cache");
        Directory.CreateDirectory(sourcesPath);
        Directory.CreateDirectory(cachePath);

        string bootWimPath = Path.Combine(sourcesPath, "boot.wim");
        string cachedSourcePath = Path.Combine(cachePath, "source.esd");
        await File.WriteAllTextAsync(bootWimPath, "original", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(cachedSourcePath, "cached source", TestContext.Current.CancellationToken);
        string cachedSourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("cached source")));
        string catalogXml = CreateCatalogXml(cachedSourceHash);

        var runner = new FakeWinPeProcessRunner { FailingOperation = failingOperation, ExitCode = exitCode, CreateExport = createExport };
        var service = CreateService(
            runner,
            new HttpClient(new StaticCatalogHandler(catalogXml, Encoding.UTF8.GetBytes("cached source"))), new WinPeWorkspaceCleanupService(() => []));

        try
        {
            WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(
                new WinPeBootImagePreparationOptions
                {
                    Artifact = new WinPeBuildArtifact
                    {
                        Architecture = WinPeArchitecture.X64,
                        BootWimPath = bootWimPath,
                        WorkingDirectoryPath = workingPath
                    },
                    Tools = new WinPeToolPaths
                    {
                        DismPath = "dism.exe"
                    },
                    WinPeLanguage = "en-US",
                    CacheDirectoryPath = cachePath,
                    WinReCacheDirectoryPath = cachePath + "-winre",
                    IncludeWirelessSupport = true
                },
                CancellationToken.None);

            if (failingOperation is not null || !createExport)
            {
                Assert.Equal(WinPeBootImageSource.WinPe, result.Value?.BootImageSource);
                WinPeDiagnostic? cause = result.Value?.FallbackCause;
                Assert.Equal(failingOperation is null ? WinPeFailureReasons.ArtifactMissing : WinPeFailureReasons.NonZeroExit, cause?.FailureReason);
                Assert.Equal(exitCode, cause?.ExitCode);
                Assert.Equal(WinPeFailureKinds.Process, cause?.FailureKind);
                Assert.Equal("dism.exe", cause?.ToolName);
                Assert.Equal("original", await File.ReadAllTextAsync(bootWimPath, TestContext.Current.CancellationToken));
                if (failingOperation == "/Mount-Image") Assert.Equal(WinPeErrorCodes.WimMountFailed, cause?.Code);
                return;
            }
            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Equal("winre", await File.ReadAllTextAsync(bootWimPath, TestContext.Current.CancellationToken));
            Assert.NotNull(result.Value);
            Assert.Equal(2, result.Value.DependencyFiles.Count);
            Assert.Contains(runner.Executions, execution => execution.Arguments.Contains("/Export-Image", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(runner.Executions, execution => execution.Arguments.Contains("/Unmount-Image", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAsync_WhenSourceDiscardFails_RetainsSessionForDisposalRetry()
    {
        using var directory = new Foundry.Core.Tests.TestUtilities.TemporaryDirectory();
        string cache = Path.Combine(directory.Path, "cache");
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "source.esd"), "cached source", TestContext.Current.CancellationToken);
        var runner = new FakeWinPeProcessRunner { FailingOperation = "/Discard", ExitCode = 9 };
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty)));
        var service = CreateService(runner, client, new WinPeWorkspaceCleanupService(() => []));

        WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
        {
            Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(directory.Path, "boot.wim"), WorkingDirectoryPath = directory.Path },
            Tools = new WinPeToolPaths { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = cache,
            WinReCacheDirectoryPath = cache + "-winre",
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(9, result.Error?.ExitCode);
        Assert.Equal(WinPeFailureKinds.Process, result.Error?.FailureKind);
        Assert.Equal(2, runner.Executions.Count(execution => execution.Arguments.Contains("/Discard", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PrepareAsync_WhenFinalSourceCleanupIsUnsafe_StopsCandidateFallback(bool missingWinRe, bool exitUnconfirmed)
    {
        using var directory = new TestUtilities.TemporaryDirectory();
        int discards = 0;
        var runner = new FakeWinPeProcessRunner
        {
            FailingOperation = "/Discard",
            ExitCode = 9,
            IncludeWinRe = !missingWinRe,
            OnRun = (arguments, _) =>
            {
                if (arguments.Contains("/Discard", StringComparison.Ordinal) && ++discards == 2 && exitUnconfirmed)
                    throw new OperationCanceledException("DISM cleanup exit remains unconfirmed.");
            }
        };
        using var client = new HttpClient(new StaticCatalogHandler(CreateFallbackCatalogXml(), Encoding.UTF8.GetBytes("source")));
        var service = CreateService(runner, client, new WinPeWorkspaceCleanupService(() => []));

        WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
        {
            Artifact = new() { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(directory.Path, "boot.wim"), WorkingDirectoryPath = directory.Path },
            Tools = new() { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.Combine(directory.Path, "cache"),
            WinReCacheDirectoryPath = Path.Combine(directory.Path, "cache") + "-winre",
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimUnmountFailed, result.Error?.Code);
        Assert.Equal(exitUnconfirmed ? WinPeMountCleanupStatus.ExitUnconfirmed : WinPeMountCleanupStatus.Failed, result.Error?.MountCleanupStatus);
        Assert.Single(runner.Executions, execution => execution.Arguments.Contains("/Mount-Image", StringComparison.Ordinal));
        Assert.Equal(2, discards);
        if (missingWinRe) Assert.Contains("does not contain winre.wim", result.Error?.Details, StringComparison.Ordinal);
        if (exitUnconfirmed)
        {
            Assert.Null(result.Error?.ExitCode);
            Assert.Contains("exit remains unconfirmed", result.Error?.Details, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory.Path, ".foundry-mount-cleanup-*.pending"));
        }
        else Assert.Equal(9, result.Error?.ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_WhenSourceContentFailsAndCleanupCompletes_TriesCandidateFallback(bool discardRetry)
    {
        using var directory = new TestUtilities.TemporaryDirectory();
        int mounts = 0;
        var runner = new FakeWinPeProcessRunner
        {
            MissingWinReForFirstMount = true,
            FailingOperation = discardRetry ? "/Discard" : null,
            ExitCode = 9,
            DiscardFailuresBeforeSuccess = 1,
            OnRun = (arguments, _) =>
            {
                if (arguments.Contains("/Mount-Image", StringComparison.Ordinal)) mounts++;
            }
        };
        using var client = new HttpClient(new StaticCatalogHandler(CreateFallbackCatalogXml(), Encoding.UTF8.GetBytes("source")));
        var service = CreateService(runner, client, new WinPeWorkspaceCleanupService(() => []));

        WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
        {
            Artifact = new() { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(directory.Path, "boot.wim"), WorkingDirectoryPath = directory.Path },
            Tools = new() { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.Combine(directory.Path, "cache"),
            WinReCacheDirectoryPath = Path.Combine(directory.Path, "cache") + "-winre"
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(2, mounts);
    }

    private static string CreateFallbackCatalogXml()
    {
        string catalog = CreateCatalogXml(string.Empty);
        string fallback = catalog.Replace("<Catalog>", "", StringComparison.Ordinal).Replace("</Catalog>", "", StringComparison.Ordinal)
            .Replace("Professional", "Enterprise", StringComparison.Ordinal).Replace("CLIENTCONSUMER", "CLIENTBUSINESS", StringComparison.Ordinal)
            .Replace("source.esd", "enterprise.esd", StringComparison.Ordinal);
        return catalog.Replace("</Catalog>", fallback + "</Catalog>", StringComparison.Ordinal);
    }

    private static string CreateCatalogXml(string hash)
    {
        return $$"""
                 <Catalog>
                   <Item>
                     <WindowsRelease>11</WindowsRelease>
                     <ReleaseId>24H2</ReleaseId>
                     <BuildMajor>26100</BuildMajor>
                     <BuildUbr>2454</BuildUbr>
                     <Architecture>x64</Architecture>
                     <LanguageCode>en-us</LanguageCode>
                     <Edition>Professional</Edition>
                     <ClientType>CLIENTCONSUMER</ClientType>
                     <LicenseChannel>RET</LicenseChannel>
                     <FileName>source.esd</FileName>
                     <Url>https://example.test/source.esd</Url>
                     <Sha256>{{hash}}</Sha256>
                   </Item>
                 </Catalog>
                 """;
    }

    private static WindowsImageMetadata Image(int index, string editionId, string name = "Localized Windows name")
        => new(index, name, editionId, 18_123_456, "x64", new Version(10, 0, 26100, 2454), "Windows image", "WinNT", ["fr-FR"], 0);

    private static WinPeBootImagePreparationService CreateService(FakeWinPeProcessRunner runner, HttpClient client,
        WinPeWorkspaceCleanupService? cleanup = null,
        Func<string, CancellationToken, Task<IReadOnlyList<WindowsImageMetadata>>>? readImageInfo = null)
        => new(runner, client, cleanup ?? new WinPeWorkspaceCleanupService(() => []),
            readImageInfo ?? ((path, _) => Task.FromResult<IReadOnlyList<WindowsImageMetadata>>(
                Path.GetFileName(path) == "enterprise.esd" ? [Image(7, "Enterprise")] : [Image(6, "Professional")])));

    [Fact]
    public async Task PrepareAsync_WhenPreviousSourceIsMounted_DoesNotRecreateItsDirectory()
    {
        using var directory = new Foundry.Core.Tests.TestUtilities.TemporaryDirectory();
        string sourceDirectory = Path.Combine(directory.Path, "windows-source-Pro");
        Directory.CreateDirectory(sourceDirectory);
        string keep = Path.Combine(sourceDirectory, "keep.txt");
        File.WriteAllText(keep, "keep");
        var runner = new FakeWinPeProcessRunner();
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty)));
        var cleanup = new WinPeWorkspaceCleanupService(() =>
            [new WinPeMountedImage(Path.Combine(sourceDirectory, "install-mount"), Path.Combine(sourceDirectory, "export", "install.wim"))]);
        var service = CreateService(runner, client, cleanup);

        WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
        {
            Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(directory.Path, "boot.wim"), WorkingDirectoryPath = directory.Path },
            Tools = new WinPeToolPaths { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.Combine(directory.Path, "cache"),
            WinReCacheDirectoryPath = Path.Combine(directory.Path, "cache") + "-winre",
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(runner.Executions);
        Assert.Equal("keep", File.ReadAllText(keep));
    }

    private sealed class StaticCatalogHandler(string content, byte[]? payload = null) : HttpMessageHandler
    {
        public int PayloadRequests { get; private set; }
        public bool FailCatalog { get; init; }
        public bool FailPayload { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool source = request.RequestUri!.AbsolutePath.EndsWith(".esd", StringComparison.OrdinalIgnoreCase);
            if (source) PayloadRequests++;
            if (source ? FailPayload : FailCatalog) throw new HttpRequestException("Simulated network failure.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = source && payload is not null ? new ByteArrayContent(payload) : new StringContent(content, Encoding.UTF8, "application/xml")
            });
        }
    }

    private sealed class CancelledCatalogHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }

    private sealed class FakeWinPeProcessRunner : IWinPeProcessRunner
    {
        public Action<string, CancellationToken>? OnRun { get; init; }
        public List<WinPeProcessExecution> Executions { get; } = [];
        public string? FailingOperation { get; init; }
        public int ExitCode { get; init; }
        public bool CreateExport { get; init; } = true;
        public string? InvalidGraphicsFile { get; init; }
        public bool WrongGraphicsArchitecture { get; init; }
        public bool IncludeWinRe { get; init; } = true;
        public bool MissingWinReForFirstMount { get; init; }
        public int? DiscardFailuresBeforeSuccess { get; init; }

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            OnRun?.Invoke(arguments, cancellationToken);
            bool discardRecovered = arguments.Contains("/Discard", StringComparison.Ordinal) &&
                DiscardFailuresBeforeSuccess.HasValue && Executions.Count(execution =>
                    execution.Arguments.Contains("/Discard", StringComparison.Ordinal)) >= DiscardFailuresBeforeSuccess.Value;
            var execution = new WinPeProcessExecution
            {
                ExitCode = !discardRecovered && FailingOperation is not null && arguments.Contains(FailingOperation, StringComparison.Ordinal) ? ExitCode : 0,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = string.Empty
            };

            Executions.Add(execution);
            if (execution.IsSuccess && (CreateExport || !arguments.Contains("/Export-Image", StringComparison.Ordinal)))
            {
                HandleSideEffects(arguments);
            }
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

        private void HandleSideEffects(string arguments)
        {
            if (arguments.Contains("/Export-Image", StringComparison.OrdinalIgnoreCase))
            {
                string destination = ExtractArgumentPath(arguments, "/DestinationImageFile:");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllText(destination, "install");
                return;
            }

            if (arguments.Contains("/Mount-Image", StringComparison.OrdinalIgnoreCase))
            {
                string mountDirectory = ExtractArgumentPath(arguments, "/MountDir:");
                string recoveryPath = Path.Combine(mountDirectory, "Windows", "System32", "Recovery");
                string system32Path = Path.Combine(mountDirectory, "Windows", "System32");
                Directory.CreateDirectory(recoveryPath);
                Directory.CreateDirectory(system32Path);
                if (IncludeWinRe && (!MissingWinReForFirstMount ||
                    Executions.Count(execution => execution.Arguments.Contains("/Mount-Image", StringComparison.Ordinal)) > 1))
                {
                    File.WriteAllText(Path.Combine(recoveryPath, "winre.wim"), "winre");
                    File.WriteAllText(Path.Combine(system32Path, "dmcmnutils.dll"), "dm");
                    File.WriteAllText(Path.Combine(system32Path, "mdmregistration.dll"), "mdm");
                }
                foreach (string fileName in new[] { "d3dcompiler_47.dll", "d3d9.dll", "dwrite.dll", "winmm.dll" })
                {
                    if (fileName == InvalidGraphicsFile && !WrongGraphicsArchitecture)
                    {
                        continue;
                    }
                    WritePortableExecutable(Path.Combine(system32Path, fileName),
                        fileName == InvalidGraphicsFile ? (ushort)0x8664 : (ushort)0xAA64);
                }
            }
        }

        private static void WritePortableExecutable(string path, ushort machine)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            stream.SetLength(512);
            writer.Write((ushort)0x5A4D);
            stream.Position = 0x3C;
            writer.Write(0x80);
            stream.Position = 0x80;
            writer.Write(0x00004550);
            writer.Write(machine);
            stream.Position = 0x94;
            writer.Write((ushort)0xF0);
            writer.Write((ushort)0x2022);
            writer.Write((ushort)0x20B);
            stream.Position = 0x104;
            writer.Write(16);
        }

        private static string ExtractArgumentPath(string arguments, string name)
        {
            int start = arguments.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            Assert.True(start >= 0, $"Argument '{name}' was not found in '{arguments}'.");
            start += name.Length;

            if (arguments[start] == '"')
            {
                int end = arguments.IndexOf('"', start + 1);
                return arguments[(start + 1)..end];
            }

            int nextSpace = arguments.IndexOf(' ', start);
            return nextSpace < 0 ? arguments[start..] : arguments[start..nextSpace];
        }
    }
}
