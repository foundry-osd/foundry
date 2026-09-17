// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeBootImagePreparationServiceTests
{
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
            ExitCode = 5,
            OnRun = (arguments, _) => { if (arguments.Contains(stage, StringComparison.Ordinal)) caller.Cancel(); }
        };
        using var client = new HttpClient(new StaticCatalogHandler(catalog));
        var service = new WinPeBootImagePreparationService(runner, client);
        try
        {
            WinPeResult<WinPeBootImagePreparationResult> result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
            {
                Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(root, "boot.wim"), WorkingDirectoryPath = root },
                Tools = new WinPeToolPaths { DismPath = "dism.exe" },
                WinPeLanguage = "en-US",
                CacheDirectoryPath = cache,
                BootImageSource = WinPeBootImageSource.WinReWifi
            }, caller.Token);
            Assert.False(result.IsSuccess);
            Assert.Equal(5, result.Error?.ExitCode);
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
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new CancelledCatalogHandler(cancellation));
        var service = new WinPeBootImagePreparationService(new FakeWinPeProcessRunner(), client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(new WinPeBootImagePreparationOptions
        {
            Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.X64, BootWimPath = "boot.wim", WorkingDirectoryPath = Path.GetTempPath() },
            Tools = new WinPeToolPaths { DismPath = "dism.exe" },
            WinPeLanguage = "en-US",
            CacheDirectoryPath = Path.GetTempPath(),
            BootImageSource = WinPeBootImageSource.WinReWifi
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
                    Assert.False(token.CanBeCanceled);
                }
            }
        };
        using var client = new HttpClient(new StaticCatalogHandler(CreateCatalogXml(string.Empty)));
        var service = new WinPeBootImagePreparationService(runner, client);
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.PrepareAsync(new WinPeBootImagePreparationOptions
            {
                Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.X64, BootWimPath = Path.Combine(root, "boot.wim"), WorkingDirectoryPath = root },
                Tools = new WinPeToolPaths { DismPath = "dism.exe" },
                WinPeLanguage = "en-US",
                CacheDirectoryPath = cache,
                BootImageSource = WinPeBootImageSource.WinReWifi
            }, cancellation.Token));
            Assert.Equal(expectsDiscard, runner.Executions.Any(execution => execution.Arguments.Contains("/Discard", StringComparison.Ordinal)));
            Assert.False(File.Exists(Path.Combine(root, "boot.wim")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("x64", "en-us", 26100)]
    [InlineData("arm64", "fr-fr", 26100)]
    [InlineData("arm64", "en-us", 26200)]
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
    [InlineData(WinPeBootImageSource.WinPe, null, false)]
    [InlineData(WinPeBootImageSource.WinReWifi, null, false)]
    [InlineData(WinPeBootImageSource.WinReWifi, "dwrite.dll", false)]
    [InlineData(WinPeBootImageSource.WinReWifi, "dwrite.dll", true)]
    public async Task PrepareAsync_ForArm64_StagesGraphicsOrFailsBeforeReplacement(WinPeBootImageSource bootImageSource, string? invalidFile, bool wrongArchitecture)
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
            WrongGraphicsArchitecture = wrongArchitecture,
            IncludeWirelessSupport = bootImageSource == WinPeBootImageSource.WinReWifi
        };
        var service = new WinPeBootImagePreparationService(runner, new HttpClient(new StaticCatalogHandler(catalog)));

        try
        {
            var result = await service.PrepareAsync(new WinPeBootImagePreparationOptions
            {
                Artifact = new WinPeBuildArtifact { Architecture = WinPeArchitecture.Arm64, BootWimPath = bootWim, WorkingDirectoryPath = root },
                Tools = new WinPeToolPaths { DismPath = "dism.exe" },
                WinPeLanguage = "en-US",
                CacheDirectoryPath = cache,
                BootImageSource = bootImageSource
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
            Assert.Equal(bootImageSource == WinPeBootImageSource.WinReWifi ? "winre" : "original", File.ReadAllText(bootWim));
            Assert.Equal(new[] { "d3d9.dll", "d3dcompiler_47.dll", "dwrite.dll", "winmm.dll" },
                result.Value!.DependencyFiles.Where(file => !file.OverwriteExisting).Select(file => file.FileName).Order(StringComparer.Ordinal));
            Assert.Equal(bootImageSource == WinPeBootImageSource.WinReWifi ? 2 : 0,
                result.Value.DependencyFiles.Count(file => file.OverwriteExisting));
            Assert.All(result.Value.DependencyFiles, file => Assert.True(File.Exists(file.StagedPath)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectCatalogCandidates_Filters24H2ArchitectureAndLanguage()
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
                                      <ReleaseId>23H2</ReleaseId>
                                      <BuildMajor>22631</BuildMajor>
                                      <BuildUbr>5337</BuildUbr>
                                      <Architecture>x64</Architecture>
                                      <LanguageCode>fr-fr</LanguageCode>
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

    [Fact]
    public void ResolveImageIndexFromOutput_MatchesEditionId()
    {
        const string dismOutput = """
                                  Deployment Image Servicing and Management tool

                                  Index : 1
                                  Name : Windows 11 Home
                                  Description : Windows 11 Home
                                  Size : 17,123,456 bytes

                                  Index : 6
                                  Name : Windows 11 Pro
                                  Description : Windows 11 Pro
                                  Edition : Professional
                                  Edition ID : Professional
                                  Size : 18,123,456 bytes
                                  """;

        WinPeResult<int> result = WinPeBootImagePreparationService.ResolveImageIndexFromOutput(dismOutput, "Pro");

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value);
    }

    [Fact]
    public async Task ValidateHashIfRequestedAsync_WhenHashMatches_ReturnsSuccess()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"foundry-hash-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(filePath, "foundry", TestContext.Current.CancellationToken);

        try
        {
            WinPeResult result = await WinPeBootImagePreparationService.ValidateHashIfRequestedAsync(
                filePath,
                "DFB316701857783DAC69A14D1FE3FD60CFF21D56E830BAF7F0E3871BD73EEE39",
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void PrepareDependencyFiles_ForX64Wifi_StagesWirelessFiles()
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
            WinPeResult<WinPeBootImagePreparationResult> result =
                WinPeBootImagePreparationService.PrepareDependencyFiles(mountedImagePath, dependencyPath, WinPeArchitecture.X64, WinPeBootImageSource.WinReWifi);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(2, result.Value.DependencyFiles.Count);
            Assert.All(result.Value.DependencyFiles, dependency => Assert.True(File.Exists(dependency.StagedPath)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, 0, true)]
    [InlineData("/Get-ImageInfo", 5, true)]
    [InlineData("/Export-Image", 2, true)]
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
        var service = new WinPeBootImagePreparationService(
            runner,
            new HttpClient(new StaticCatalogHandler(catalogXml)));

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
                    CacheDirectoryPath = cachePath
                },
                CancellationToken.None);

            if (failingOperation is not null || !createExport)
            {
                Assert.False(result.IsSuccess);
                Assert.Equal(failingOperation is null ? WinPeFailureReasons.ArtifactMissing : WinPeFailureReasons.NonZeroExit, result.Error?.FailureReason);
                Assert.Equal(exitCode, result.Error?.ExitCode);
                Assert.Equal(WinPeFailureKinds.Process, result.Error?.FailureKind);
                Assert.Equal("dism.exe", result.Error?.ToolName);
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

    private sealed class StaticCatalogHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/xml")
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
        public bool IncludeWirelessSupport { get; init; } = true;

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
                ExitCode = FailingOperation is not null && arguments.Contains(FailingOperation, StringComparison.Ordinal) ? ExitCode : 0,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = CreateOutput(arguments)
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

        private static string CreateOutput(string arguments)
        {
            if (!arguments.Contains("/Get-ImageInfo", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return """
                   Index : 6
                   Name : Windows 11 Pro
                   Edition : Professional
                   Edition ID : Professional
                   """;
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
                if (IncludeWirelessSupport)
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
