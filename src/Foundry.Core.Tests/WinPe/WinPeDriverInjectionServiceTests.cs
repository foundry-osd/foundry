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

    private sealed class FakeInjectionRunner : IWinPeProcessRunner
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
                WorkingDirectory = workingDirectory
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
