using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Services.WinPe.OsRecovery;

namespace Foundry.Core.Tests.WinPe;

public sealed class OsRecoveryPayloadProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_StagesRequiredFilesAndExcludesWinPeOnlyArtifacts()
    {
        using TempOsRecoveryWorkspace workspace = TempOsRecoveryWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe", "connect");
        string bootstrapScript = "Write-Host 'Bootstrap'";

        var service = new OsRecoveryPayloadProvisioningService(
            new EmbeddedLanguageRegistryService(),
            new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()));

        WinPeResult<OsRecoveryPayloadProvisioningResult> result = await service.ProvisionAsync(
            new OsRecoveryPayloadProvisioningOptions
            {
                MountedImagePath = workspace.MountedImagePath,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = bootstrapScript,
                FoundryConnectConfigurationJson = "{\"schemaVersion\":1}",
                DeployConfigurationJson = "{\"schemaVersion\":2}",
                IanaWindowsTimeZoneMapJson = "{\"zones\":[]}",
                SevenZipSourceDirectoryPath = workspace.SevenZipSourcePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath
                },
                BootMenuLocalizations = CreateBootMenuLocalizations()
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.NotNull(result.Value);

        string toolsPath = Path.Combine(workspace.MountedImagePath, "Sources", "Recovery", "Tools");
        string system32Path = Path.Combine(workspace.MountedImagePath, "Windows", "System32");
        string configPath = Path.Combine(workspace.MountedImagePath, "Foundry", "Config");
        string connectRuntimePath = Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Connect", "win-x64");
        string sevenZipToolsPath = Path.Combine(workspace.MountedImagePath, "Foundry", "Tools", "7zip");

        Assert.True(File.Exists(Path.Combine(toolsPath, "FoundryRecoveryLauncher.cmd")));
        Assert.True(File.Exists(Path.Combine(toolsPath, "WinREConfig.xml")));
        Assert.Equal(bootstrapScript, await File.ReadAllTextAsync(Path.Combine(system32Path, "FoundryBootstrap.ps1")));
        Assert.Equal("{\"schemaVersion\":1}", await File.ReadAllTextAsync(Path.Combine(configPath, "foundry.connect.config.json")));
        Assert.Equal("{\"schemaVersion\":2}", await File.ReadAllTextAsync(Path.Combine(configPath, "foundry.deploy.config.json")));
        Assert.Equal("{\"zones\":[]}", await File.ReadAllTextAsync(Path.Combine(configPath, "iana-windows-timezones.json")));
        Assert.Equal("connect", await File.ReadAllTextAsync(Path.Combine(connectRuntimePath, "Foundry.Connect.exe")));
        Assert.Equal("7za", await File.ReadAllTextAsync(Path.Combine(sevenZipToolsPath, "x64", "7za.exe")));
        Assert.Equal("license", await File.ReadAllTextAsync(Path.Combine(sevenZipToolsPath, "License.txt")));
        Assert.Equal("readme", await File.ReadAllTextAsync(Path.Combine(sevenZipToolsPath, "readme.txt")));

        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "Foundry.Deploy")));
        Assert.False(File.Exists(Path.Combine(configPath, "foundry.connect.provisioning-source.txt")));
        Assert.False(File.Exists(Path.Combine(configPath, "foundry.deploy.provisioning-source.txt")));
        Assert.False(Directory.Exists(Path.Combine(configPath, "Secrets")));
        Assert.False(Directory.Exists(Path.Combine(configPath, "Network")));
        Assert.False(Directory.Exists(Path.Combine(configPath, "Autopilot")));
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Runtime", "AutopilotHash")));
        Assert.False(Directory.Exists(Path.Combine(workspace.MountedImagePath, "Foundry", "Tools", "OA3")));
    }

    [Fact]
    public async Task ProvisionAsync_WritesWinReConfigXmlAsUtf8WithoutBom()
    {
        using TempOsRecoveryWorkspace workspace = TempOsRecoveryWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe", "connect");
        var service = new OsRecoveryPayloadProvisioningService(
            new EmbeddedLanguageRegistryService(),
            new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()));

        WinPeResult<OsRecoveryPayloadProvisioningResult> result = await service.ProvisionAsync(
            new OsRecoveryPayloadProvisioningOptions
            {
                MountedImagePath = workspace.MountedImagePath,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                FoundryConnectConfigurationJson = "{}",
                DeployConfigurationJson = "{}",
                IanaWindowsTimeZoneMapJson = "{}",
                SevenZipSourceDirectoryPath = workspace.SevenZipSourcePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath
                },
                BootMenuLocalizations = CreateBootMenuLocalizations()
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);

        string winReConfigPath = Path.Combine(workspace.MountedImagePath, "Sources", "Recovery", "Tools", "WinREConfig.xml");
        byte[] bytes = await File.ReadAllBytesAsync(winReConfigPath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        XDocument document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        Assert.Equal("Recovery", document.Root?.Name.LocalName);
        Assert.Equal(
            "FoundryRecoveryLauncher.cmd",
            document.Root?.Element("RecoveryTools")?.Element("RelativeFilePath")?.Value);
    }

    [Fact]
    public async Task ProvisionAsync_ReturnsBootMenuConfigurationForEverySupportedCulture()
    {
        using TempOsRecoveryWorkspace workspace = TempOsRecoveryWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe", "connect");
        IReadOnlyList<OsRecoveryBootMenuLocalization> localizations = CreateBootMenuLocalizations();
        var service = new OsRecoveryPayloadProvisioningService(
            new EmbeddedLanguageRegistryService(),
            new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()));

        WinPeResult<OsRecoveryPayloadProvisioningResult> result = await service.ProvisionAsync(
            new OsRecoveryPayloadProvisioningOptions
            {
                MountedImagePath = workspace.MountedImagePath,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                FoundryConnectConfigurationJson = "{}",
                DeployConfigurationJson = "{}",
                IanaWindowsTimeZoneMapJson = "{}",
                SevenZipSourceDirectoryPath = workspace.SevenZipSourcePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath
                },
                BootMenuLocalizations = localizations
            },
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);

        XDocument document = XDocument.Parse(result.Value!.BootMenuConfigurationXml);
        XElement[] entries = document.Root!.Elements("WinRETool").ToArray();
        Assert.Equal(localizations.Count, entries.Length);
        Assert.All(entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Attribute("locale")?.Value));
            Assert.False(string.IsNullOrWhiteSpace(entry.Element("Name")?.Value));
            Assert.False(string.IsNullOrWhiteSpace(entry.Element("Description")?.Value));
        });
    }

    [Fact]
    public async Task ProvisionAsync_WhenBootMenuLocalizationIsMissingSupportedCulture_ReturnsFailure()
    {
        using TempOsRecoveryWorkspace workspace = TempOsRecoveryWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe", "connect");
        List<OsRecoveryBootMenuLocalization> localizations = [.. CreateBootMenuLocalizations()];
        localizations.RemoveAt(localizations.Count - 1);
        var service = new OsRecoveryPayloadProvisioningService(
            new EmbeddedLanguageRegistryService(),
            new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()));

        WinPeResult<OsRecoveryPayloadProvisioningResult> result = await service.ProvisionAsync(
            new OsRecoveryPayloadProvisioningOptions
            {
                MountedImagePath = workspace.MountedImagePath,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                FoundryConnectConfigurationJson = "{}",
                DeployConfigurationJson = "{}",
                IanaWindowsTimeZoneMapJson = "{}",
                SevenZipSourceDirectoryPath = workspace.SevenZipSourcePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath
                },
                BootMenuLocalizations = localizations
            },
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("supported culture", result.Error?.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_WhenBootMenuTextExceedsThirtyCharacters_ReturnsFailure()
    {
        using TempOsRecoveryWorkspace workspace = TempOsRecoveryWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe", "connect");
        List<OsRecoveryBootMenuLocalization> localizations = [.. CreateBootMenuLocalizations()];
        localizations[0] = localizations[0] with
        {
            Name = "Foundry OS Recovery Utility Name",
            Description = "Foundry OS Recovery Utility Description"
        };

        var service = new OsRecoveryPayloadProvisioningService(
            new EmbeddedLanguageRegistryService(),
            new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()));

        WinPeResult<OsRecoveryPayloadProvisioningResult> result = await service.ProvisionAsync(
            new OsRecoveryPayloadProvisioningOptions
            {
                MountedImagePath = workspace.MountedImagePath,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                FoundryConnectConfigurationJson = "{}",
                DeployConfigurationJson = "{}",
                IanaWindowsTimeZoneMapJson = "{}",
                SevenZipSourceDirectoryPath = workspace.SevenZipSourcePath,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath
                },
                BootMenuLocalizations = localizations
            },
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("30 characters", result.Error?.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_WhenManagedPayloadExceedsBudget_ReturnsFailure()
    {
        using TempOsRecoveryWorkspace workspace = TempOsRecoveryWorkspace.Create();
        string connectArchivePath = workspace.CreateArchive("connect.zip", "Foundry.Connect.exe", new string('c', 256));
        var service = new OsRecoveryPayloadProvisioningService(
            new EmbeddedLanguageRegistryService(),
            new WinPeRuntimePayloadProvisioningService(new FakeRuntimeProcessRunner()));

        WinPeResult<OsRecoveryPayloadProvisioningResult> result = await service.ProvisionAsync(
            new OsRecoveryPayloadProvisioningOptions
            {
                MountedImagePath = workspace.MountedImagePath,
                WorkingDirectoryPath = workspace.WorkingDirectoryPath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                FoundryConnectConfigurationJson = "{}",
                DeployConfigurationJson = "{}",
                IanaWindowsTimeZoneMapJson = "{}",
                SevenZipSourceDirectoryPath = workspace.SevenZipSourcePath,
                MaxManagedPayloadSizeBytes = 32,
                Connect = new WinPeRuntimePayloadApplicationOptions
                {
                    IsEnabled = true,
                    ArchivePath = connectArchivePath
                },
                BootMenuLocalizations = CreateBootMenuLocalizations()
            },
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("256 MiB", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("32", result.Error?.Details, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<OsRecoveryBootMenuLocalization> CreateBootMenuLocalizations()
    {
        return new EmbeddedLanguageRegistryService()
            .GetLanguages()
            .Select(language => new OsRecoveryBootMenuLocalization
            {
                Culture = language.Code,
                Name = "Foundry Recovery",
                Description = "Recover this device"
            })
            .ToArray();
    }

    private sealed class FakeRuntimeProcessRunner : IWinPeProcessRunner
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
            throw new NotSupportedException();
        }
    }

    private sealed class TempOsRecoveryWorkspace : IDisposable
    {
        private TempOsRecoveryWorkspace(string rootPath)
        {
            RootPath = rootPath;
            MountedImagePath = Path.Combine(rootPath, "mount");
            WorkingDirectoryPath = Path.Combine(rootPath, "work");
            SevenZipSourcePath = Path.Combine(rootPath, "7z");

            Directory.CreateDirectory(MountedImagePath);
            Directory.CreateDirectory(WorkingDirectoryPath);
            Directory.CreateDirectory(Path.Combine(SevenZipSourcePath, "x64"));
            File.WriteAllText(Path.Combine(SevenZipSourcePath, "x64", "7za.exe"), "7za");
            File.WriteAllText(Path.Combine(SevenZipSourcePath, "License.txt"), "license");
            File.WriteAllText(Path.Combine(SevenZipSourcePath, "readme.txt"), "readme");
        }

        public string RootPath { get; }
        public string MountedImagePath { get; }
        public string WorkingDirectoryPath { get; }
        public string SevenZipSourcePath { get; }

        public static TempOsRecoveryWorkspace Create()
        {
            return new TempOsRecoveryWorkspace(Path.Combine(Path.GetTempPath(), $"foundry-osrecovery-{Guid.NewGuid():N}"));
        }

        public string CreateArchive(string archiveName, string executableName, string content)
        {
            string payloadPath = Path.Combine(RootPath, "payloads", Path.GetFileNameWithoutExtension(archiveName));
            Directory.CreateDirectory(payloadPath);
            File.WriteAllText(Path.Combine(payloadPath, executableName), content);

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
