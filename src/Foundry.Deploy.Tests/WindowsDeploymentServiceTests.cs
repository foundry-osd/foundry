// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class WindowsDeploymentServiceTests
{
    [Fact]
    public async Task ResolveImageIndexAsync_WhenRequestedEditionIsMissing_ThrowsBeforeImageApplication()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "consumer.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains("/Index:4", StringComparison.OrdinalIgnoreCase)
                ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 4\nEdition : Core" }
                : arguments.Contains("/Index:9", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 9\nEdition : Professional" }
                    : new ProcessExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = """
                    Index : 1
                    Name : Windows Setup Media

                    Index : 4
                    Name : Windows 11 Home

                    Index : 9
                    Name : Windows 11 Pro
                    """
                    }
        };
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveImageIndexAsync(
                imagePath,
                "Enterprise",
                workspace.RootPath,
                TestContext.Current.CancellationToken));

        Assert.Contains("Enterprise", exception.Message, StringComparison.Ordinal);
        Assert.Contains("4: Core", exception.Message, StringComparison.Ordinal);
        Assert.Contains("9: Professional", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveImageIndexAsync_WhenSingleImageDoesNotMatchRequestedEdition_Throws()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "setup-media.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            Result = new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = """
                    Index : 1
                    Name : Windows Setup Media
                    """
            }
        };
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveImageIndexAsync(
                imagePath,
                "Enterprise",
                workspace.RootPath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveImageIndexAsync_DoesNotSelectNVariantForNonNEdition()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "consumer.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains("/Index:5", StringComparison.OrdinalIgnoreCase)
                ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 5\nEdition : ProfessionalN" }
                : arguments.Contains("/Index:9", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 9\nEdition : Professional" }
                    : new ProcessExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = """
                    Index : 5
                    Name : Windows 11 Pro N

                    Index : 9
                    Name : Windows 11 Pro
                    """
                    }
        };
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        int imageIndex = await service.ResolveImageIndexAsync(
            imagePath,
            "Pro",
            workspace.RootPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(9, imageIndex);
    }

    [Theory]
    [InlineData("Home", "Core", 4)]
    [InlineData("Home N", "CoreN", 5)]
    [InlineData("Home Single Language", "CoreSingleLanguage", 6)]
    [InlineData("Home China", "CoreCountrySpecific", 7)]
    [InlineData("Education", "Education", 8)]
    [InlineData("Education N", "EducationN", 9)]
    [InlineData("Pro", "Professional", 10)]
    [InlineData("Pro N", "ProfessionalN", 11)]
    [InlineData("Enterprise", "Enterprise", 12)]
    [InlineData("Enterprise N", "EnterpriseN", 13)]
    public async Task ResolveImageIndexAsync_ResolvesExactEditionIdFromDetailedImageMetadata(
        string edition,
        string editionId,
        int expectedIndex)
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "windows.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains($"/Index:{expectedIndex}", StringComparison.OrdinalIgnoreCase)
                ? new ProcessExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = $"""
                        Index : {expectedIndex}
                        Name : Nom Windows localise arbitraire
                        Edition : {editionId}
                        """
                }
                : arguments.Contains("/Index:", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = """
                            Index : 1
                            Name : Windows Setup Media
                            """
                    }
                    : new ProcessExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = $"""
                        Index : 1
                        Name : Windows Setup Media

                        Index : {expectedIndex}
                        Name : Nom Windows localise arbitraire
                        """
                    }
        };
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        int imageIndex = await service.ResolveImageIndexAsync(
            imagePath,
            edition,
            workspace.RootPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedIndex, imageIndex);
        Assert.Contains(processRunner.Calls, call => call.Contains($"/Index:{expectedIndex}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveImageIndexAsync_WhenEditionIdOccursMoreThanOnce_ThrowsWithoutFallback()
    {
        using var workspace = new TemporaryWorkspace();
        string imagePath = Path.Combine(workspace.RootPath, "windows.esd");
        await File.WriteAllTextAsync(imagePath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            ResultFactory = arguments => arguments.Contains("/Index:", StringComparison.OrdinalIgnoreCase)
                ? new ProcessExecutionResult { ExitCode = 0, StandardOutput = $"Index : {ParseRequestedIndex(arguments)}\nEdition : Professional" }
                : new ProcessExecutionResult { ExitCode = 0, StandardOutput = "Index : 8\nName : Pro first\n\nIndex : 9\nName : Pro second" }
        };
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveImageIndexAsync(
                imagePath,
                "Pro",
                workspace.RootPath,
                TestContext.Current.CancellationToken));

        Assert.Contains("found 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareTargetDiskAsync_CreatesPartitionsInExpectedOrder_Efi_Msr_Recovery_Windows()
    {
        using var workspace = new TemporaryWorkspace();
        string workingDirectory = Path.Combine(workspace.RootPath, "Work");
        var processRunner = new RecordingProcessRunner();
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        await service.PrepareTargetDiskAsync(1, workingDirectory, TestContext.Current.CancellationToken);

        string scriptPath = Path.Combine(workingDirectory, "diskpart-os-target.txt");
        string[] scriptLines = await File.ReadAllLinesAsync(scriptPath, TestContext.Current.CancellationToken);
        int efiIndex = Array.IndexOf(scriptLines, "create partition efi size=260");
        int msrIndex = Array.IndexOf(scriptLines, "create partition msr size=16");
        int recoveryIndex = Array.IndexOf(scriptLines, "create partition primary size=5120");
        int windowsIndex = Array.IndexOf(scriptLines, "create partition primary");
        int recoveryFormatIndex = Array.IndexOf(scriptLines, "format quick fs=ntfs label=Recovery");
        int windowsFormatIndex = Array.IndexOf(scriptLines, "format quick fs=ntfs label=Windows");

        Assert.True(efiIndex >= 0);
        Assert.True(msrIndex > efiIndex);
        Assert.True(recoveryIndex > msrIndex);
        Assert.True(windowsIndex > recoveryIndex);
        Assert.True(recoveryFormatIndex > recoveryIndex);
        Assert.True(windowsFormatIndex > recoveryFormatIndex);
        Assert.DoesNotContain(scriptLines, line => line.StartsWith("shrink ", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(26199, "/c /v")]
    [InlineData(26200, "/c /bootex /v")]
    public async Task ConfigureBootAsync_UsesAppliedWindowsBcdBootWithExpectedArguments(
        int operatingSystemBuildMajor,
        string expectedArguments)
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = Path.Combine(workspace.RootPath, "Target Windows");
        string windowsPath = Path.Combine(windowsRoot, "Windows");
        string bcdBootPath = Path.Combine(windowsPath, "System32", "bcdboot.exe");
        string workingDirectory = Path.Combine(workspace.RootPath, "Work");
        const string systemRoot = @"S:\";
        Directory.CreateDirectory(Path.GetDirectoryName(bcdBootPath)!);
        await File.WriteAllTextAsync(bcdBootPath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner();
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureBootAsync(
            windowsRoot,
            systemRoot,
            operatingSystemBuildMajor,
            workingDirectory,
            TestContext.Current.CancellationToken);

        Assert.Equal(bcdBootPath, processRunner.LastFileName);
        Assert.Equal(
            $"\"{windowsPath}\" /s \"{systemRoot}\" /f UEFI {expectedArguments}",
            processRunner.LastArguments);
        Assert.Equal(workingDirectory, processRunner.LastWorkingDirectory);
    }

    [Fact]
    public async Task ConfigureBootAsync_WhenAppliedBcdBootIsMissing_ThrowsFileNotFoundException()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = Path.Combine(workspace.RootPath, "WindowsRoot");
        string expectedBcdBootPath = Path.Combine(windowsRoot, "Windows", "System32", "bcdboot.exe");
        var processRunner = new RecordingProcessRunner();
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.ConfigureBootAsync(
                windowsRoot,
                @"S:\",
                26200,
                Path.Combine(workspace.RootPath, "Work"),
                TestContext.Current.CancellationToken));

        Assert.Equal(expectedBcdBootPath, exception.FileName);
        Assert.Null(processRunner.LastFileName);
    }

    [Fact]
    public async Task ConfigureBootAsync_WhenAppliedBcdBootFails_PropagatesDiagnostic()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = Path.Combine(workspace.RootPath, "WindowsRoot");
        string bcdBootPath = Path.Combine(windowsRoot, "Windows", "System32", "bcdboot.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(bcdBootPath)!);
        await File.WriteAllTextAsync(bcdBootPath, string.Empty, TestContext.Current.CancellationToken);
        var processRunner = new RecordingProcessRunner
        {
            Result = new ProcessExecutionResult
            {
                ExitCode = 193,
                StandardOutput = "Failure when attempting to copy boot files.",
                StandardError = "diagnostic"
            }
        };
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        DeploymentProcessException exception = await Assert.ThrowsAsync<DeploymentProcessException>(() =>
            service.ConfigureBootAsync(
                windowsRoot,
                @"S:\",
                26200,
                Path.Combine(workspace.RootPath, "Work"),
                TestContext.Current.CancellationToken));

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Equal(bcdBootPath, processRunner.LastFileName);
        Assert.Contains("BCDBoot configuration failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ExitCode=193", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Failure when attempting to copy boot files.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("diagnostic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigureOfflineComputerNameAsync_WhenDefaultTimeZoneIdIsProvided_WritesUnattendTimeZone()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = Path.Combine(workspace.RootPath, "WindowsRoot");
        Directory.CreateDirectory(windowsRoot);

        var service = new WindowsDeploymentService(new NoOpProcessRunner(), NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureOfflineComputerNameAsync(
            windowsRoot,
            "LAB01",
            "amd64",
            "Romance Standard Time");

        string unattendPath = Path.Combine(windowsRoot, "Windows", "Panther", "unattend.xml");
        XDocument document = XDocument.Load(unattendPath);
        XNamespace ns = "urn:schemas-microsoft-com:unattend";

        Assert.Equal("LAB01", document.Descendants(ns + "ComputerName").Single().Value);
        Assert.Equal("Romance Standard Time", document.Descendants(ns + "TimeZone").Single().Value);
    }

    [Fact]
    public async Task ConfigureOfflineComputerNameAsync_WhenIanaTimeZoneIdIsProvided_WritesWindowsTimeZoneId()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = Path.Combine(workspace.RootPath, "WindowsRoot");
        Directory.CreateDirectory(windowsRoot);

        var service = new WindowsDeploymentService(new NoOpProcessRunner(), NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureOfflineComputerNameAsync(
            windowsRoot,
            "LAB01",
            "amd64",
            "Europe/Paris");

        string unattendPath = Path.Combine(windowsRoot, "Windows", "Panther", "unattend.xml");
        XDocument document = XDocument.Load(unattendPath);
        XNamespace ns = "urn:schemas-microsoft-com:unattend";

        Assert.Equal("Romance Standard Time", document.Descendants(ns + "TimeZone").Single().Value);
    }

    [Fact]
    public async Task ConfigureOfflineOobeAsync_WhenEnabled_WritesUnattendAndPrivacyPolicies()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = CreateWindowsRoot(workspace);
        string workingDirectory = Path.Combine(workspace.RootPath, "Work");
        var processRunner = new RecordingProcessRunner();
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureOfflineOobeAsync(
            windowsRoot,
            new DeployOobeSettings
            {
                IsEnabled = true,
                SkipLicenseTerms = true,
                DiagnosticDataLevel = DeployOobeDiagnosticDataLevel.Off,
                HidePrivacySetup = true,
                AllowTailoredExperiences = false,
                AllowAdvertisingId = false,
                AllowOnlineSpeechRecognition = false,
                AllowInkingAndTypingDiagnostics = false,
                LocationAccess = DeployOobeLocationAccessMode.ForceOff
            },
            "amd64",
            workingDirectory);

        string unattendPath = Path.Combine(windowsRoot, "Windows", "Panther", "unattend.xml");
        XDocument document = XDocument.Load(unattendPath);
        XNamespace ns = "urn:schemas-microsoft-com:unattend";

        Assert.Equal("true", document.Descendants(ns + "HideEULAPage").Single().Value);
        Assert.Equal("3", document.Descendants(ns + "ProtectYourPC").Single().Value);
        Assert.Contains(processRunner.Calls, call => call.Contains(@"AllowTelemetry", StringComparison.Ordinal) && call.Contains("/d 0", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"DisablePrivacyExperience", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"DisabledByGroupPolicy", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"AllowInputPersonalization", StringComparison.Ordinal) && call.Contains("/d 0", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"AllowLinguisticDataCollection", StringComparison.Ordinal) && call.Contains("/d 0", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"LetAppsAccessLocation", StringComparison.Ordinal) && call.Contains("/d 2", StringComparison.Ordinal));
        Assert.DoesNotContain(processRunner.Calls, call => call.Contains(@"DisableLocation", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"DisableTailoredExperiencesWithDiagnosticData", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigureOfflineOobeAsync_WhenDisabled_DoesNotWriteUnattendOrPolicies()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = CreateWindowsRoot(workspace);
        string workingDirectory = Path.Combine(workspace.RootPath, "Work");
        var processRunner = new RecordingProcessRunner();
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureOfflineOobeAsync(
            windowsRoot,
            new DeployOobeSettings(),
            "amd64",
            workingDirectory);

        string unattendPath = Path.Combine(windowsRoot, "Windows", "Panther", "unattend.xml");

        Assert.False(File.Exists(unattendPath));
        Assert.Empty(processRunner.Calls);
    }

    [Fact]
    public async Task ConfigureOfflineAiComponentRemovalAsync_WhenEnabled_WritesOfflinePolicies()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = CreateWindowsRoot(workspace);
        string workingDirectory = Path.Combine(workspace.RootPath, "Work");
        var processRunner = new RecordingProcessRunner();
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureOfflineAiComponentRemovalAsync(
            windowsRoot,
            new DeployAiComponentRemovalSettings
            {
                IsEnabled = true,
                RemoveCopilot = true,
                RemoveAiHub = true,
                DisableRecall = true,
                DisableClickToDo = true,
                DisableAiServiceAutoStart = true,
                DisableEdgeAi = true,
                DisablePaintAi = true,
                DisableNotepadAi = true
            },
            workingDirectory);

        Assert.Contains(processRunner.Calls, call => call.Contains(@"LOAD HKLM\FoundrySoftware", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"LOAD HKLM\FoundrySystem", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"LOAD HKU\FoundryDefault", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"WindowsCopilot", StringComparison.Ordinal) && call.Contains("TurnOffWindowsCopilot", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"WindowsAI", StringComparison.Ordinal) && call.Contains("DisableAIDataAnalysis", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"WindowsAI", StringComparison.Ordinal) && call.Contains("DisableClickToDo", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"ControlSet001\Services\WSAIFabricSvc", StringComparison.Ordinal) && call.Contains("Start", StringComparison.Ordinal) && call.Contains("/d 3", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"Policies\Microsoft\Edge", StringComparison.Ordinal) && call.Contains("CopilotPageContext", StringComparison.Ordinal) && call.Contains("/d 0", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"Policies\Paint", StringComparison.Ordinal) && call.Contains("DisableCocreator", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"Policies\WindowsNotepad", StringComparison.Ordinal) && call.Contains("DisableAIFeatures", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"FoundryDefault\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", StringComparison.Ordinal) && call.Contains("ShowCopilotButton", StringComparison.Ordinal) && call.Contains("/d 0", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"UNLOAD HKLM\FoundrySoftware", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"UNLOAD HKLM\FoundrySystem", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"UNLOAD HKU\FoundryDefault", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigureOfflineAiComponentRemovalAsync_WhenDisabled_DoesNotWritePolicies()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = CreateWindowsRoot(workspace);
        string workingDirectory = Path.Combine(workspace.RootPath, "Work");
        var processRunner = new RecordingProcessRunner();
        var service = new WindowsDeploymentService(processRunner, NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureOfflineAiComponentRemovalAsync(
            windowsRoot,
            new DeployAiComponentRemovalSettings(),
            workingDirectory);

        Assert.Empty(processRunner.Calls);
    }

    private static string CreateWindowsRoot(TemporaryWorkspace workspace)
    {
        string windowsRoot = Path.Combine(workspace.RootPath, "WindowsRoot");
        Directory.CreateDirectory(Path.Combine(windowsRoot, "Windows", "System32", "config"));
        Directory.CreateDirectory(Path.Combine(windowsRoot, "Users", "Default"));
        File.WriteAllText(Path.Combine(windowsRoot, "Windows", "System32", "config", "SOFTWARE"), string.Empty);
        File.WriteAllText(Path.Combine(windowsRoot, "Windows", "System32", "config", "SYSTEM"), string.Empty);
        File.WriteAllText(Path.Combine(windowsRoot, "Users", "Default", "NTUSER.DAT"), string.Empty);
        return windowsRoot;
    }

    private static int ParseRequestedIndex(string arguments)
    {
        string value = arguments[(arguments.LastIndexOf("/Index:", StringComparison.OrdinalIgnoreCase) + 7)..];
        return int.Parse(value);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"foundry-deploy-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class NoOpProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];
        public string? LastFileName { get; private set; }
        public string? LastArguments { get; private set; }
        public string? LastWorkingDirectory { get; private set; }
        public ProcessExecutionResult Result { get; init; } = new() { ExitCode = 0 };
        public Func<string, ProcessExecutionResult>? ResultFactory { get; init; }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"{fileName} {arguments}");
            LastFileName = fileName;
            LastArguments = arguments;
            LastWorkingDirectory = workingDirectory;
            return Task.FromResult(ResultFactory?.Invoke(arguments) ?? Result);
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"{fileName} {string.Join(' ', arguments)}");
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"{fileName} {string.Join(' ', arguments)}");
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }
    }
}
