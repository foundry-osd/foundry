// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class BootConfigurationServiceTests
{
    [Fact]
    public async Task ConfigureBootAsync_RejectsMissingRetainedLayout()
    {
        var runner = new RecordingProcessRunner();
        var service = new BootRecoveryService(runner, NullLogger<BootRecoveryService>.Instance, new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ConfigureBootAsync(null!, @"W:\", @"S:\", 26200,
            Path.GetTempPath(), TestContext.Current.CancellationToken));
        Assert.Empty(runner.Calls);
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
        var service = new BootRecoveryService(processRunner, NullLogger<BootRecoveryService>.Instance, new WindowsImagingService(processRunner, NullLogger<WindowsImagingService>.Instance));

        await service.RunBcdBootAsync(
            windowsRoot,
            systemRoot,
            operatingSystemBuildMajor,
            workingDirectory,
            TestContext.Current.CancellationToken);

        Assert.Equal(bcdBootPath, processRunner.LastFileName);
        Assert.Equal(
            new[] { windowsPath, "/s", "S:", "/f", "UEFI" }.Concat(expectedArguments.Split(' ')),
            processRunner.LastArgumentTokens);
        Assert.Equal(workingDirectory, processRunner.LastWorkingDirectory);
    }

    [Fact]
    public async Task ConfigureBootAsync_PreservesSeparateArgumentTokens()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = Path.Combine(workspace.RootPath, "Target Windows");
        string bcdBootPath = Path.Combine(windowsRoot, "Windows", "System32", "bcdboot.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(bcdBootPath)!);
        await File.WriteAllTextAsync(bcdBootPath, string.Empty, TestContext.Current.CancellationToken);
        var runner = new RecordingProcessRunner();
        var service = new BootRecoveryService(runner, NullLogger<BootRecoveryService>.Instance, new WindowsImagingService(runner, NullLogger<WindowsImagingService>.Instance));
        await service.RunBcdBootAsync(windowsRoot, @"S:\", 26200, workspace.RootPath, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { Path.Combine(windowsRoot, "Windows"), "/s", "S:", "/f", "UEFI", "/c", "/bootex", "/v" }, runner.LastArgumentTokens);
    }

    [Fact]
    public async Task ConfigureBootAsync_WhenAppliedBcdBootIsMissing_ThrowsFileNotFoundException()
    {
        using var workspace = new TemporaryWorkspace();
        string windowsRoot = Path.Combine(workspace.RootPath, "WindowsRoot");
        string expectedBcdBootPath = Path.Combine(windowsRoot, "Windows", "System32", "bcdboot.exe");
        var processRunner = new RecordingProcessRunner();
        var service = new BootRecoveryService(processRunner, NullLogger<BootRecoveryService>.Instance, new WindowsImagingService(processRunner, NullLogger<WindowsImagingService>.Instance));

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.RunBcdBootAsync(
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
        var service = new BootRecoveryService(processRunner, NullLogger<BootRecoveryService>.Instance, new WindowsImagingService(processRunner, NullLogger<WindowsImagingService>.Instance));

        DeploymentProcessException exception = await Assert.ThrowsAsync<DeploymentProcessException>(() =>
            service.RunBcdBootAsync(
                windowsRoot,
                @"S:\",
                26200,
                Path.Combine(workspace.RootPath, "Work"),
                TestContext.Current.CancellationToken));

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Equal(bcdBootPath, processRunner.LastFileName);
        Assert.Contains("BCDBoot configuration failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ExitCode: 193", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Failure when attempting to copy boot files.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("diagnostic", exception.Message, StringComparison.Ordinal);
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

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];
        public string? LastFileName { get; private set; }
        public string? LastArguments { get; private set; }
        public IReadOnlyList<string>? LastArgumentTokens { get; private set; }
        public string? LastWorkingDirectory { get; private set; }
        public ProcessExecutionResult Result { get; init; } = new() { ExitCode = 0 };
        public Func<string, ProcessExecutionResult>? ResultFactory { get; init; }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null)
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
            CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null)
        {
            LastArgumentTokens = arguments.ToArray();
            string joinedArguments = string.Join(' ', LastArgumentTokens);
            Calls.Add($"{fileName} {joinedArguments}");
            LastFileName = fileName;
            LastArguments = joinedArguments;
            LastWorkingDirectory = workingDirectory;
            return Task.FromResult(ResultFactory?.Invoke(joinedArguments) ?? Result);
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken = default, TimeSpan? executionTimeout = null)
        {
            LastArgumentTokens = arguments.ToArray();
            string joinedArguments = string.Join(' ', LastArgumentTokens);
            Calls.Add($"{fileName} {joinedArguments}");
            LastFileName = fileName;
            LastArguments = joinedArguments;
            LastWorkingDirectory = workingDirectory;
            ProcessExecutionResult result = ResultFactory?.Invoke(joinedArguments) ?? Result;
            if (!string.IsNullOrEmpty(result.StandardOutput))
            {
                onOutputData?.Invoke(result.StandardOutput);
            }

            if (!string.IsNullOrEmpty(result.StandardError))
            {
                onErrorData?.Invoke(result.StandardError);
            }

            return Task.FromResult(result);
        }
    }
}
