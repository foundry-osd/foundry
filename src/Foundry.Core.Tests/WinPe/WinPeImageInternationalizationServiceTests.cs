using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeImageInternationalizationServiceTests
{
    [Fact]
    public async Task ApplyAsync_AddsLanguagePackNeutralAndLocalizedComponentsBeforeIntlSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-intl-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string ocRoot = CreateOptionalComponentRoot(root, "amd64", "fr-fr");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);

        string languagePack = Path.Combine(ocRoot, "fr-fr", "lp.cab");
        string neutralWmi = Path.Combine(ocRoot, "WinPE-WMI.cab");
        string localizedWmi = Path.Combine(ocRoot, "fr-fr", "WinPE-WMI_fr-fr.cab");
        File.WriteAllText(languagePack, string.Empty);
        File.WriteAllText(neutralWmi, string.Empty);
        File.WriteAllText(localizedWmi, string.Empty);

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
                    WinPeLanguage = "fr-FR",
                    WorkingDirectoryPath = workingDirectory
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Collection(
                runner.Executions,
                execution => Assert.Contains($"/PackagePath:{WinPeProcessRunner.Quote(languagePack)}", execution.Arguments),
                execution => Assert.Contains($"/PackagePath:{WinPeProcessRunner.Quote(neutralWmi)}", execution.Arguments),
                execution => Assert.Contains($"/PackagePath:{WinPeProcessRunner.Quote(localizedWmi)}", execution.Arguments),
                execution => Assert.Contains("/Set-AllIntl:fr-FR", execution.Arguments),
                execution => Assert.Contains("/Set-InputLocale:", execution.Arguments));
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
        public string PackageStandardOutput { get; init; } = string.Empty;

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            int exitCode = arguments.Contains("/Add-Package", StringComparison.OrdinalIgnoreCase)
                ? PackageExitCode
                : 0;

            var execution = new WinPeProcessExecution
            {
                ExitCode = exitCode,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = exitCode == 0 ? string.Empty : PackageStandardOutput
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
