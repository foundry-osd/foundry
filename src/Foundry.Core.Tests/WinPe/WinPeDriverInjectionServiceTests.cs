// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeDriverInjectionServiceTests
{
    [Fact]
    public async Task InjectAsync_AddsEachDriverPathWithRecurse()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-driver-injection-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string driverDirectory = Path.Combine(root, "drivers");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(driverDirectory);

        var runner = new FakeInjectionRunner();
        var service = new WinPeDriverInjectionService(runner);

        try
        {
            WinPeResult result = await service.InjectAsync(
                new WinPeDriverInjectionOptions
                {
                    MountedImagePath = mountedImagePath,
                    WorkingDirectoryPath = workingDirectory,
                    DriverPackagePaths = [driverDirectory],
                    DismExecutablePath = "dism.exe",
                    RecurseSubdirectories = true
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            WinPeProcessExecution execution = Assert.Single(runner.Executions);
            Assert.Equal("dism.exe", execution.FileName);
            Assert.Contains("/Add-Driver", execution.Arguments);
            Assert.Contains("/Recurse", execution.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InjectAsync_WhenMountedImageIsMissing_ReturnsValidationFailure()
    {
        var service = new WinPeDriverInjectionService(new FakeInjectionRunner());

        WinPeResult result = await service.InjectAsync(
            new WinPeDriverInjectionOptions
            {
                MountedImagePath = "missing",
                WorkingDirectoryPath = Path.GetTempPath(),
                DriverPackagePaths = [Path.GetTempPath()]
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task InjectAsync_ReportsDismProgressFromProcessOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-driver-injection-{Guid.NewGuid():N}");
        string mountedImagePath = Path.Combine(root, "mount");
        string workingDirectory = Path.Combine(root, "work");
        string driverDirectory = Path.Combine(root, "drivers");
        Directory.CreateDirectory(mountedImagePath);
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(driverDirectory);

        var runner = new FakeInjectionRunner(["25%", "100%"]);
        var progress = new CollectingProgress<WinPeDismProgress>();
        var service = new WinPeDriverInjectionService(runner);

        try
        {
            WinPeResult result = await service.InjectAsync(
                new WinPeDriverInjectionOptions
                {
                    MountedImagePath = mountedImagePath,
                    WorkingDirectoryPath = workingDirectory,
                    DriverPackagePaths = [driverDirectory],
                    DismExecutablePath = "dism.exe",
                    RecurseSubdirectories = true,
                    DismProgress = progress
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Collection(
                progress.Reports,
                report => Assert.Equal(25, report.Percent),
                report => Assert.Equal(100, report.Percent));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeInjectionRunner(IReadOnlyList<string>? outputLines = null, int exitCode = 0) : IWinPeProcessOutputRunner
    {
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            var execution = new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                ExitCode = exitCode
            };

            Executions.Add(execution);
            return Task.FromResult(execution);
        }

        public Task<WinPeProcessExecution> RunWithOutputAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            foreach (string line in outputLines ?? [])
            {
                onOutputData?.Invoke(line);
            }

            return RunAsync(fileName, arguments, workingDirectory, cancellationToken, environmentOverrides);
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

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];

        public void Report(T value)
        {
            Reports.Add(value);
        }
    }

    [Fact]
    public async Task InjectAsync_WhenExitCodeIs50_TreatsAsSuccess()
    {
        using DriverInjectionFixture fixture = DriverInjectionFixture.Create();
        var service = new WinPeDriverInjectionService(new FakeInjectionRunner(exitCode: 50));

        WinPeResult result = await service.InjectAsync(fixture.Options(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
    }

    [Fact]
    public async Task InjectAsync_WhenPackageFailsAndContinueOnError_SkipsAndSucceeds()
    {
        using DriverInjectionFixture fixture = DriverInjectionFixture.Create();
        var service = new WinPeDriverInjectionService(new FakeInjectionRunner(exitCode: 1));

        WinPeResult result = await service.InjectAsync(
            fixture.Options() with { ContinueOnError = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
    }

    [Fact]
    public async Task InjectAsync_WhenPackageFailsAndNotContinueOnError_Fails()
    {
        using DriverInjectionFixture fixture = DriverInjectionFixture.Create();
        var service = new WinPeDriverInjectionService(new FakeInjectionRunner(exitCode: 1));

        WinPeResult result = await service.InjectAsync(
            fixture.Options() with { ContinueOnError = false },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.DriverInjectionFailed, result.Error?.Code);
    }

    private sealed class DriverInjectionFixture : IDisposable
    {
        private readonly string _root;

        private DriverInjectionFixture(string root, string mountedImagePath, string workingDirectory, string driverDirectory)
        {
            _root = root;
            MountedImagePath = mountedImagePath;
            WorkingDirectory = workingDirectory;
            DriverDirectory = driverDirectory;
        }

        public string MountedImagePath { get; }

        public string WorkingDirectory { get; }

        public string DriverDirectory { get; }

        public static DriverInjectionFixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), $"foundry-driver-injection-{Guid.NewGuid():N}");
            string mountedImagePath = Path.Combine(root, "mount");
            string workingDirectory = Path.Combine(root, "work");
            string driverDirectory = Path.Combine(root, "drivers");
            Directory.CreateDirectory(mountedImagePath);
            Directory.CreateDirectory(workingDirectory);
            Directory.CreateDirectory(driverDirectory);
            return new DriverInjectionFixture(root, mountedImagePath, workingDirectory, driverDirectory);
        }

        public WinPeDriverInjectionOptions Options()
        {
            return new WinPeDriverInjectionOptions
            {
                MountedImagePath = MountedImagePath,
                WorkingDirectoryPath = WorkingDirectory,
                DriverPackagePaths = [DriverDirectory],
                DismExecutablePath = "dism.exe",
                RecurseSubdirectories = true
            };
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
