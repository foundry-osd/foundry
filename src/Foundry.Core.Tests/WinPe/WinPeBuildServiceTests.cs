using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeBuildServiceTests
{
    [Fact]
    public async Task BuildAsync_WhenOptionsAreNull_ReturnsValidationFailure()
    {
        var service = new WinPeBuildService();

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(null!);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task BuildAsync_WhenOutputDirectoryIsMissing_ReturnsValidationFailure()
    {
        var service = new WinPeBuildService();

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(new WinPeBuildOptions());

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task BuildAsync_WhenArchitectureIsInvalid_ReturnsValidationFailure()
    {
        var service = new WinPeBuildService();

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(new WinPeBuildOptions
        {
            OutputDirectoryPath = "C:\\Temp",
            Architecture = (WinPeArchitecture)999
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task BuildAsync_WhenCopypeSucceeds_CreatesExpectedWorkspaceFolders()
    {
        using TempWinPeBuildWorkspace workspace = TempWinPeBuildWorkspace.Create();
        var runner = new FakeBuildRunner();
        var service = new WinPeBuildService(
            new WinPeToolResolver(() => workspace.KitsRootPath),
            runner);

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(
            new WinPeBuildOptions
            {
                OutputDirectoryPath = workspace.OutputDirectoryPath,
                Architecture = WinPeArchitecture.X64
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(Directory.Exists(Path.Combine(result.Value!.WorkingDirectoryPath, "media")));
        Assert.True(Directory.Exists(Path.Combine(result.Value.WorkingDirectoryPath, "mount")));
        Assert.True(Directory.Exists(Path.Combine(result.Value.WorkingDirectoryPath, "drivers")));
        Assert.True(Directory.Exists(Path.Combine(result.Value.WorkingDirectoryPath, "logs")));
        Assert.True(Directory.Exists(Path.Combine(result.Value.WorkingDirectoryPath, "temp")));
    }

    private sealed class FakeBuildRunner : IWinPeProcessRunner
    {
        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            return Task.FromResult(new WinPeProcessExecution
            {
                ExitCode = 0,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            });
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            string workingRoot = scriptArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last().Trim('"');
            string bootWimPath = Path.Combine(workingRoot, "media", "sources", "boot.wim");
            Directory.CreateDirectory(Path.GetDirectoryName(bootWimPath)!);
            File.WriteAllText(bootWimPath, "boot");

            return Task.FromResult(new WinPeProcessExecution
            {
                ExitCode = 0,
                FileName = scriptPath,
                Arguments = scriptArguments,
                WorkingDirectory = workingDirectory
            });
        }

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            return RunCmdScriptAsync(scriptPath, scriptArguments, workingDirectory, cancellationToken);
        }
    }

    private sealed class TempWinPeBuildWorkspace : IDisposable
    {
        private TempWinPeBuildWorkspace(string rootPath)
        {
            RootPath = rootPath;
            OutputDirectoryPath = Path.Combine(rootPath, "Workspaces", "WinPe");
            KitsRootPath = Path.Combine(rootPath, "Kits");

            string winPeRoot = Path.Combine(
                KitsRootPath,
                "Assessment and Deployment Kit",
                "Windows Preinstallation Environment");
            Directory.CreateDirectory(winPeRoot);
            File.WriteAllText(Path.Combine(winPeRoot, "copype.cmd"), "copype");
            File.WriteAllText(Path.Combine(winPeRoot, "MakeWinPEMedia.cmd"), "makewinpemedia");
        }

        public string RootPath { get; }
        public string OutputDirectoryPath { get; }
        public string KitsRootPath { get; }

        public static TempWinPeBuildWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-winpe-build-{Guid.NewGuid():N}");
            return new TempWinPeBuildWorkspace(rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
