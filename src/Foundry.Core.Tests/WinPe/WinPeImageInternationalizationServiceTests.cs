// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeImageInternationalizationServiceTests
{
    [Theory]
    [InlineData("fr-FR", "fr-fr")]
    [InlineData("es-ES", "es-es")]
    [InlineData("bg-BG", "bg-bg")]
    public async Task ApplyAsync_AddsPackagesThenAppliesLocaleDefaults(string locale, string packageLanguage)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-intl-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string ocRoot = CreateOptionalComponentRoot(root, "amd64", packageLanguage);
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);

        string languagePack = Path.Combine(ocRoot, packageLanguage, "lp.cab");
        string neutralWmi = Path.Combine(ocRoot, "WinPE-WMI.cab");
        string localizedWmi = Path.Combine(ocRoot, packageLanguage, $"WinPE-WMI_{packageLanguage}.cab");
        string secureStartup = Path.Combine(ocRoot, "WinPE-SecureStartup.cab");
        File.WriteAllText(languagePack, string.Empty);
        File.WriteAllText(neutralWmi, string.Empty);
        File.WriteAllText(localizedWmi, string.Empty);
        File.WriteAllText(secureStartup, string.Empty);

        var runner = new FakeInternationalizationRunner();
        var service = new WinPeImageInternationalizationService(runner);

        try
        {
            WinPeResult result = await service.ApplyAsync(
                new WinPeImageInternationalizationOptions
                {
                    MountedImagePath = mountedImagePath,
                    Architecture = WinPeArchitecture.X64,
                    Tools = new WinPeToolPaths
                    {
                        KitsRootPath = root,
                        DismPath = "dism.exe"
                    },
                    WinPeLanguage = locale,
                    WorkingDirectoryPath = workingDirectory
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Collection(
                runner.Executions,
                execution => Assert.Contains($"/PackagePath:{WinPeProcessRunner.Quote(languagePack)}", execution.Arguments),
                execution => Assert.Contains($"/PackagePath:{WinPeProcessRunner.Quote(neutralWmi)}", execution.Arguments),
                execution => Assert.Contains($"/PackagePath:{WinPeProcessRunner.Quote(localizedWmi)}", execution.Arguments),
                execution => Assert.Contains($"/PackagePath:{WinPeProcessRunner.Quote(secureStartup)}", execution.Arguments),
                execution => Assert.Equal($"/English /Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Set-AllIntl:{locale}", execution.Arguments),
                execution => Assert.Equal($"/English /Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Set-InputLocale:{locale}", execution.Arguments));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_AddsSecureStartupOptionalComponentByDefault()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-intl-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string ocRoot = CreateOptionalComponentRoot(root, "amd64", "en-us");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);

        string languagePack = Path.Combine(ocRoot, "en-us", "lp.cab");
        string wmi = Path.Combine(ocRoot, "WinPE-WMI.cab");
        string secureStartup = Path.Combine(ocRoot, "WinPE-SecureStartup.cab");
        File.WriteAllText(languagePack, string.Empty);
        File.WriteAllText(wmi, string.Empty);
        File.WriteAllText(secureStartup, string.Empty);

        var runner = new FakeInternationalizationRunner();
        var service = new WinPeImageInternationalizationService(runner);

        try
        {
            WinPeResult result = await service.ApplyAsync(
                new WinPeImageInternationalizationOptions
                {
                    MountedImagePath = mountedImagePath,
                    Architecture = WinPeArchitecture.X64,
                    Tools = new WinPeToolPaths
                    {
                        KitsRootPath = root,
                        DismPath = "dism.exe"
                    },
                    WinPeLanguage = "en-US",
                    WorkingDirectoryPath = workingDirectory
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Contains(
                runner.Executions,
                execution => execution.Arguments.Contains($"/PackagePath:{WinPeProcessRunner.Quote(secureStartup)}", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_WhenLanguagePackIsMissing_ReturnsToolNotFound()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-intl-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        CreateOptionalComponentRoot(root, "amd64", "fr-fr");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);

        var service = new WinPeImageInternationalizationService(new FakeInternationalizationRunner());

        try
        {
            WinPeResult result = await service.ApplyAsync(
                new WinPeImageInternationalizationOptions
                {
                    MountedImagePath = mountedImagePath,
                    Architecture = WinPeArchitecture.X64,
                    Tools = new WinPeToolPaths
                    {
                        KitsRootPath = root,
                        DismPath = "dism.exe"
                    },
                    WinPeLanguage = "fr-FR",
                    WorkingDirectoryPath = workingDirectory
                },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_WhenSecureStartupIsMissing_ReturnsToolNotFound()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-intl-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string ocRoot = CreateOptionalComponentRoot(root, "amd64", "en-us");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(ocRoot, "en-us", "lp.cab"), string.Empty);
        File.WriteAllText(Path.Combine(ocRoot, "WinPE-WMI.cab"), string.Empty);

        var service = new WinPeImageInternationalizationService(new FakeInternationalizationRunner());

        try
        {
            WinPeResult result = await service.ApplyAsync(
                new WinPeImageInternationalizationOptions
                {
                    MountedImagePath = mountedImagePath,
                    Architecture = WinPeArchitecture.X64,
                    Tools = new WinPeToolPaths
                    {
                        KitsRootPath = root,
                        DismPath = "dism.exe"
                    },
                    WinPeLanguage = "en-US",
                    WorkingDirectoryPath = workingDirectory
                },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
            Assert.Contains("WinPE-SecureStartup", result.Error?.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, "Install boot image package")]
    [InlineData("Set-AllIntl", "Apply boot image international settings (Set-AllIntl:en-US)")]
    [InlineData("Set-InputLocale", "Apply boot image international settings (Set-InputLocale:en-US)")]
    public async Task ApplyAsync_WhenDismFails_IdentifiesOperationAndPreservesClassification(string? failedOperation, string expectedStage)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-intl-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string ocRoot = CreateOptionalComponentRoot(root, "amd64", "en-us");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(ocRoot, "en-us", "lp.cab"), string.Empty);
        File.WriteAllText(Path.Combine(ocRoot, "WinPE-SecureStartup.cab"), string.Empty);

        var runner = new FakeInternationalizationRunner
        {
            PackageExitCode = failedOperation is null ? 1 : 0,
            InternationalSettingsExitCode = failedOperation is null ? 0 : 87,
            FailInternationalOperation = failedOperation,
            PackageStandardOutput = "The specified package is not applicable to this image.",
            FailPackagePathContains = "WinPE-SecureStartup"
        };
        var service = new WinPeImageInternationalizationService(runner);

        try
        {
            WinPeResult result = await service.ApplyAsync(
                new WinPeImageInternationalizationOptions
                {
                    MountedImagePath = mountedImagePath,
                    Architecture = WinPeArchitecture.X64,
                    Tools = new WinPeToolPaths
                    {
                        KitsRootPath = root,
                        DismPath = "dism.exe"
                    },
                    WinPeLanguage = "en-US",
                    WorkingDirectoryPath = workingDirectory
                },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeErrorCodes.BuildFailed, result.Error?.Code);
            Assert.Equal(failedOperation is null ? 1 : 87, result.Error?.ExitCode);
            Assert.Equal(expectedStage, result.Error?.Stage);
            Assert.Equal(WinPeFailureKinds.Process, result.Error?.FailureKind);
            Assert.Equal(WinPeFailureReasons.NonZeroExit, result.Error?.FailureReason);
            Assert.Equal("dism.exe", result.Error?.ToolName);
            Assert.Contains(failedOperation is null ? "The specified package is not applicable" : "Error: 87", result.Error?.Details, StringComparison.Ordinal);
            if (failedOperation is not null)
            {
                Assert.Contains($"/{failedOperation}:en-US", runner.Executions[^1].Arguments, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public async Task ApplyAsync_WhenNeutralComponentIsAlreadyInstalled_Continues()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-intl-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string ocRoot = CreateOptionalComponentRoot(root, "amd64", "en-us");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(ocRoot, "en-us", "lp.cab"), string.Empty);
        File.WriteAllText(Path.Combine(ocRoot, "WinPE-WMI.cab"), string.Empty);
        File.WriteAllText(Path.Combine(ocRoot, "WinPE-SecureStartup.cab"), string.Empty);

        var runner = new FakeInternationalizationRunner
        {
            PackageExitCode = 1,
            PackageStandardOutput = "The specified package is already installed."
        };
        var service = new WinPeImageInternationalizationService(runner);

        try
        {
            WinPeResult result = await service.ApplyAsync(
                new WinPeImageInternationalizationOptions
                {
                    MountedImagePath = mountedImagePath,
                    Architecture = WinPeArchitecture.X64,
                    Tools = new WinPeToolPaths
                    {
                        KitsRootPath = root,
                        DismPath = "dism.exe"
                    },
                    WinPeLanguage = "en-US",
                    WorkingDirectoryPath = workingDirectory
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_WhenNoNeutralComponentsExist_ReturnsToolNotFound()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-intl-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string ocRoot = CreateOptionalComponentRoot(root, "amd64", "en-us");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(ocRoot, "en-us", "lp.cab"), string.Empty);

        var service = new WinPeImageInternationalizationService(new FakeInternationalizationRunner());

        try
        {
            WinPeResult result = await service.ApplyAsync(
                new WinPeImageInternationalizationOptions
                {
                    MountedImagePath = mountedImagePath,
                    Architecture = WinPeArchitecture.X64,
                    Tools = new WinPeToolPaths
                    {
                        KitsRootPath = root,
                        DismPath = "dism.exe"
                    },
                    WinPeLanguage = "en-US",
                    WorkingDirectoryPath = workingDirectory
                },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeErrorCodes.ToolNotFound, result.Error?.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateOptionalComponentRoot(string kitsRootPath, string architecture, string language)
    {
        string ocRoot = Path.Combine(
            kitsRootPath,
            "Assessment and Deployment Kit",
            "Windows Preinstallation Environment",
            architecture,
            "WinPE_OCs");

        Directory.CreateDirectory(Path.Combine(ocRoot, language));
        return ocRoot;
    }

    private sealed class FakeInternationalizationRunner : IWinPeProcessRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];
        public int PackageExitCode { get; init; }
        public int InternationalSettingsExitCode { get; init; }
        public string? FailInternationalOperation { get; init; }
        public string PackageStandardOutput { get; init; } = string.Empty;
        public string? FailPackagePathContains { get; init; }

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            bool shouldFailPackage = arguments.Contains("/Add-Package", StringComparison.OrdinalIgnoreCase) &&
                                     (string.IsNullOrWhiteSpace(FailPackagePathContains) ||
                                      arguments.Contains(FailPackagePathContains, StringComparison.OrdinalIgnoreCase));
            int exitCode = shouldFailPackage
                ? PackageExitCode
                : FailInternationalOperation is not null && arguments.Contains($"/{FailInternationalOperation}:", StringComparison.Ordinal)
                    ? InternationalSettingsExitCode : 0;

            var execution = new WinPeProcessExecution
            {
                ExitCode = exitCode,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = exitCode == 0 ? string.Empty : shouldFailPackage ? PackageStandardOutput : $"Error: {exitCode}"
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
}
