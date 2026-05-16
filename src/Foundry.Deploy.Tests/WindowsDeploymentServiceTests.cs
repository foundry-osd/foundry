using System.Xml.Linq;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class WindowsDeploymentServiceTests
{
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
        Assert.Contains(processRunner.Calls, call => call.Contains(@"AllowTelemetry", StringComparison.Ordinal) && call.Contains("/d 0", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"DisablePrivacyExperience", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"DisabledByGroupPolicy", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"AllowInputPersonalization", StringComparison.Ordinal) && call.Contains("/d 0", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"AllowLinguisticDataCollection", StringComparison.Ordinal) && call.Contains("/d 0", StringComparison.Ordinal));
        Assert.Contains(processRunner.Calls, call => call.Contains(@"DisableLocation", StringComparison.Ordinal) && call.Contains("/d 1", StringComparison.Ordinal));
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

    private static string CreateWindowsRoot(TemporaryWorkspace workspace)
    {
        string windowsRoot = Path.Combine(workspace.RootPath, "WindowsRoot");
        Directory.CreateDirectory(Path.Combine(windowsRoot, "Windows", "System32", "config"));
        Directory.CreateDirectory(Path.Combine(windowsRoot, "Users", "Default"));
        File.WriteAllText(Path.Combine(windowsRoot, "Windows", "System32", "config", "SOFTWARE"), string.Empty);
        File.WriteAllText(Path.Combine(windowsRoot, "Users", "Default", "NTUSER.DAT"), string.Empty);
        return windowsRoot;
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

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"{fileName} {arguments}");
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
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
