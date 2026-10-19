// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeBuildServiceTests
{
    [Fact]
    public async Task BuildAsync_WhenExistingWorkspaceIsMounted_DoesNotRecreateIt()
    {
        using TempWinPeBuildWorkspace workspace = TempWinPeBuildWorkspace.Create();
        string working = Path.Combine(workspace.OutputDirectoryPath, "existing");
        Directory.CreateDirectory(working);
        string keep = Path.Combine(working, "keep.txt");
        File.WriteAllText(keep, "keep");
        var cleanup = new WinPeWorkspaceCleanupService(() =>
            [new WinPeMountedImage(Path.Combine(working, "mount"), Path.Combine(working, "boot.wim"))]);
        var service = new WinPeBuildService(new WinPeToolResolver(() => workspace.KitsRootPath), new FakeBuildRunner(), cleanup);

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(new WinPeBuildOptions
        {
            OutputDirectoryPath = workspace.OutputDirectoryPath,
            WorkingDirectoryPath = working,
            CleanExistingWorkingDirectory = true,
            Architecture = WinPeArchitecture.X64
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimUnmountFailed, result.Error?.Code);
        Assert.Equal("keep", File.ReadAllText(keep));
        Assert.False(File.Exists(Path.Combine(working, "media", "sources", "boot.wim")));
    }

    [Fact]
    public async Task BuildAsync_WhenOptionsAreNull_ReturnsValidationFailure()
    {
        var service = new WinPeBuildService();

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public async Task BuildAsync_WhenOutputDirectoryIsMissing_ReturnsValidationFailure()
    {
        var service = new WinPeBuildService();

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(new WinPeBuildOptions(), cancellationToken: TestContext.Current.CancellationToken);

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
        }, cancellationToken: TestContext.Current.CancellationToken);

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

    [Theory]
    [InlineData(9, WinPeFailureReasons.NonZeroExit)]
    [InlineData(0, WinPeFailureReasons.ArtifactMissing)]
    public async Task BuildAsync_WhenCopypeDoesNotProduceImage_ReturnsStructuredProcessDiagnostic(int exitCode, string reason)
    {
        using TempWinPeBuildWorkspace workspace = TempWinPeBuildWorkspace.Create();
        var service = new WinPeBuildService(
            new WinPeToolResolver(() => workspace.KitsRootPath),
            new FakeBuildRunner(exitCode, createImage: false));

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(
            new WinPeBuildOptions
            {
                OutputDirectoryPath = workspace.OutputDirectoryPath,
                Architecture = WinPeArchitecture.X64
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeFailureKinds.Process, result.Error?.FailureKind);
        Assert.Equal(reason, result.Error?.FailureReason);
        Assert.Equal("copype", result.Error?.ToolName);
        Assert.Equal(exitCode, result.Error?.ExitCode);
    }

    [Theory]
    [InlineData(5, WinPeFailureReasons.AccessDenied)]
    [InlineData(50, WinPeFailureReasons.NonZeroExit)]
    public async Task BuildAsync_WhenCopypeCannotMountImage_ReportsReplayedDismMountOutcome(int dismExitCode, string expectedReason)
    {
        using TempWinPeBuildWorkspace workspace = TempWinPeBuildWorkspace.Create();
        var runner = new CopypeMountFailureRunner(dismExitCode);
        var service = new WinPeBuildService(new WinPeToolResolver(() => workspace.KitsRootPath), runner);

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(
            new WinPeBuildOptions
            {
                OutputDirectoryPath = workspace.OutputDirectoryPath,
                Architecture = WinPeArchitecture.X64
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.BuildFailed, result.Error?.Code);
        Assert.Equal("copype", result.Error?.ToolName);
        Assert.Equal(1, result.Error?.ExitCode);
        Assert.Equal(WinPeCopypeFailureDetails.WimMountFailed, result.Error?.FailureDetail);
        Assert.Equal(expectedReason, result.Error?.FailureReason);
        Assert.Contains("Failed to mount the WinPE WIM file", result.Error?.Details);
        Assert.Contains(CopypeMountFailureRunner.DismError, result.Error?.Details);
        Assert.Contains(runner.DismArguments, arguments =>
            arguments.Contains("/Mount-Image", StringComparison.Ordinal) &&
            arguments.Contains(Path.Combine("media", "sources", "boot.wim"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsync_WhenReplayedDismMountSucceeds_DiscardsItAndKeepsCopypeFailure()
    {
        using TempWinPeBuildWorkspace workspace = TempWinPeBuildWorkspace.Create();
        var runner = new CopypeMountFailureRunner(dismExitCode: 0);
        var service = new WinPeBuildService(new WinPeToolResolver(() => workspace.KitsRootPath), runner);

        WinPeResult<WinPeBuildArtifact> result = await service.BuildAsync(
            new WinPeBuildOptions
            {
                OutputDirectoryPath = workspace.OutputDirectoryPath,
                Architecture = WinPeArchitecture.X64
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeFailureReasons.NonZeroExit, result.Error?.FailureReason);
        Assert.Equal(WinPeCopypeFailureDetails.WimMountFailed, result.Error?.FailureDetail);
        Assert.Contains("did not reproduce", result.Error?.Details);
        Assert.Contains(runner.DismArguments, arguments =>
            arguments.Contains("/Unmount-Image", StringComparison.Ordinal) &&
            arguments.Contains("/Discard", StringComparison.Ordinal));
    }

    /// <summary>
    /// Simulates copype failing at its WIM mount step after staging boot.wim, then answers the DISM replay.
    /// </summary>
    private sealed class CopypeMountFailureRunner(int dismExitCode) : IWinPeProcessRunner
    {
        public const string DismError = "Error: 0x800f0816 The mount request was rejected.";

        public List<string> DismArguments { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            DismArguments.Add(arguments);
            bool isMount = arguments.Contains("/Mount-Image", StringComparison.Ordinal);
            return Task.FromResult(new WinPeProcessExecution
            {
                ExitCode = isMount ? dismExitCode : 0,
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = isMount && dismExitCode != 0 ? DismError : string.Empty
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
                ExitCode = 1,
                FileName = scriptPath,
                Arguments = scriptArguments,
                WorkingDirectory = workingDirectory,
                StandardOutput = $"Mounting \"{bootWimPath}\"\r\nERROR: Failed to mount the WinPE WIM file. Check logs at C:\\WINDOWS\\Logs\\DISM for more details."
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

    private sealed class FakeBuildRunner(int exitCode = 0, bool createImage = true) : IWinPeProcessRunner
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
            if (exitCode == 0 && createImage)
            {
                string workingRoot = scriptArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last().Trim('"');
                string bootWimPath = Path.Combine(workingRoot, "media", "sources", "boot.wim");
                Directory.CreateDirectory(Path.GetDirectoryName(bootWimPath)!);
                File.WriteAllText(bootWimPath, "boot");
            }

            return Task.FromResult(new WinPeProcessExecution
            {
                ExitCode = exitCode,
                FileName = scriptPath,
                Arguments = scriptArguments,
                WorkingDirectory = workingDirectory,
                StandardError = exitCode == 0 ? string.Empty : "copype failed"
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
