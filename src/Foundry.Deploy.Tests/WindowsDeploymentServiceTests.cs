using System.Xml.Linq;
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
        string workingDirectory = Path.Combine(workspace.RootPath, "Work");
        Directory.CreateDirectory(windowsRoot);

        var service = new WindowsDeploymentService(new NoOpProcessRunner(), NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureOfflineComputerNameAsync(
            windowsRoot,
            "LAB01",
            "amd64",
            workingDirectory,
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
        string workingDirectory = Path.Combine(workspace.RootPath, "Work");
        Directory.CreateDirectory(windowsRoot);

        var service = new WindowsDeploymentService(new NoOpProcessRunner(), NullLogger<WindowsDeploymentService>.Instance);

        await service.ConfigureOfflineComputerNameAsync(
            windowsRoot,
            "LAB01",
            "amd64",
            workingDirectory,
            "Europe/Paris");

        string unattendPath = Path.Combine(windowsRoot, "Windows", "Panther", "unattend.xml");
        XDocument document = XDocument.Load(unattendPath);
        XNamespace ns = "urn:schemas-microsoft-com:unattend";

        Assert.Equal("Romance Standard Time", document.Descendants(ns + "TimeZone").Single().Value);
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
}
