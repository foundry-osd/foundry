// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeMountSessionTests
{
    [Fact]
    public async Task MountAsync_WhenDismFails_ReturnsMountFailure()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution { ExitCode = 5, StandardError = "mount failed" },
            EmptyInventory());

        WinPeResult<WinPeMountSession> result = await WinPeMountSession.MountAsync(
            runner,
            "dism.exe",
            "boot.wim",
            Path.Combine(tempDirectory.Path, "mount"),
            tempDirectory.Path,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimMountFailed, result.Error?.Code);
        Assert.Equal(2, runner.Executions.Count);
        Assert.Contains("/Mount-Image", runner.Executions[0].Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAsync_WhenCommitSucceeds_DoesNotDiscard()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(
            new WinPeProcessExecution { ExitCode = 0 },
            new WinPeProcessExecution { ExitCode = 0 });

        WinPeMountSession session = (await WinPeMountSession.MountAsync(
            runner,
            "dism.exe",
            "boot.wim",
            Path.Combine(tempDirectory.Path, "mount"),
            tempDirectory.Path,
            CancellationToken.None)).Value!;

        WinPeResult result = await session.CommitAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, runner.Executions.Count);
        Assert.Contains("/Commit", runner.Executions[1].Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAsync_WhenCommitFails_AttemptsDiscardAndReturnsCombinedDiagnostics()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(
            new WinPeProcessExecution { ExitCode = 0 },
            new WinPeProcessExecution { ExitCode = 7, StandardError = "commit failed" },
            new WinPeProcessExecution { ExitCode = 0, StandardOutput = "discarded" });

        WinPeMountSession session = (await WinPeMountSession.MountAsync(
            runner,
            "dism.exe",
            "boot.wim",
            Path.Combine(tempDirectory.Path, "mount"),
            tempDirectory.Path,
            CancellationToken.None)).Value!;

        WinPeResult result = await session.CommitAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimUnmountFailed, result.Error?.Code);
        Assert.Contains("Commit diagnostics", result.Error?.Details, StringComparison.Ordinal);
        Assert.False(result.Error!.RecoveryRequired);
        Assert.Equal(WinPeMountState.Unmounted, session.State);
        Assert.Equal(3, runner.Executions.Count);
        Assert.Contains("/Discard", runner.Executions[2].Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsync_WhenSessionIsStillMounted_Discards()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(
            new WinPeProcessExecution { ExitCode = 0 },
            new WinPeProcessExecution { ExitCode = 0 });

        WinPeMountSession session = (await WinPeMountSession.MountAsync(
            runner,
            "dism.exe",
            "boot.wim",
            Path.Combine(tempDirectory.Path, "mount"),
            tempDirectory.Path,
            CancellationToken.None)).Value!;

        await session.DisposeAsync();

        Assert.Equal(2, runner.Executions.Count);
        Assert.Contains("/Discard", runner.Executions[1].Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscardAsync_WhenFirstDiscardFails_RetryStillInvokesDism()
    {
        using var tempDirectory = new TemporaryDirectory();
        string mount = Path.Combine(tempDirectory.Path, "mount");
        string image = Path.Combine(tempDirectory.Path, "boot.wim");
        var runner = new FakeWinPeProcessRunner(
            new WinPeProcessExecution(), new WinPeProcessExecution { ExitCode = 5 },
            MountedInventory(image, mount), MountedInventory(image, mount), new WinPeProcessExecution());
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", image,
            mount, tempDirectory.Path, CancellationToken.None)).Value!;
        WinPeResult failed = await session.DiscardAsync(CancellationToken.None);
        Assert.False(failed.IsSuccess);
        Assert.True(failed.Error!.RecoveryRequired);
        Assert.False(session.CanDeleteMountDirectory);
        Assert.True((await session.DiscardAsync(CancellationToken.None)).IsSuccess);
        Assert.Equal(5, runner.Executions.Count);
        Assert.True(session.CanDeleteMountDirectory);
    }

    [Fact]
    public async Task DiscardAsync_WhenCallerCancelled_UsesIndependentCleanupToken()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution(), new WinPeProcessExecution());
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(tempDirectory.Path, "mount"), tempDirectory.Path, CancellationToken.None)).Value!;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.True((await session.DiscardAsync(cancelled.Token)).IsSuccess);
        Assert.False(runner.LastToken.IsCancellationRequested);
        Assert.True(runner.LastToken.CanBeCanceled);
    }

    [Fact]
    public async Task MountAsync_WhenPartialMountRegistered_DiscardsExactOwnedMount()
    {
        using var temp = new TemporaryDirectory();
        string image = Path.Combine(temp.Path, "boot.wim"), mount = Path.Combine(temp.Path, "mount");
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution { ExitCode = 5 },
            MountedInventory(image, mount), new WinPeProcessExecution());
        var result = await WinPeMountSession.MountAsync(runner, "dism.exe", image, mount, temp.Path, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.False(result.Error!.RecoveryRequired);
        Assert.Contains("/Discard", runner.Executions[2].Arguments);
        Assert.Contains("/MountDir:" + mount, runner.Executions[2].Arguments);
    }

    [Fact]
    public async Task CommitAsync_WhenCommitAndDiscardFail_RetainsBothFailuresAndState()
    {
        using var temp = new TemporaryDirectory();
        string image = Path.Combine(temp.Path, "boot.wim"), mount = Path.Combine(temp.Path, "mount");
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution(),
            new WinPeProcessExecution { ExitCode = 7, StandardError = "commit failure" },
            new WinPeProcessExecution { ExitCode = 8, StandardError = "discard failure" }, MountedInventory(image, mount));
        var session = (await WinPeMountSession.MountAsync(runner, "dism.exe", image, mount, temp.Path, CancellationToken.None)).Value!;
        var result = await session.CommitAsync(CancellationToken.None);
        Assert.Equal(7, result.Error!.ExitCode);
        Assert.Equal(8, result.Error.CleanupDiagnostic!.ExitCode);
        Assert.True(result.Error.RecoveryRequired);
        Assert.Equal(mount, result.Error.OwnedMountPath);
        Assert.Equal(WinPeMountState.RecoveryRequired, session.State);
        await session.DisposeAsync();
        Assert.Equal(4, runner.Executions.Count);
    }

    [Fact]
    public async Task MountAsync_WhenInterruptedAndAbsent_DoesNotTreatCancellationAsTermination()
    {
        using var temp = new TemporaryDirectory();
        var error = new OperationCanceledException("Native execution interrupted.");
        error.Data["ProcessRootExitConfirmed"] = true;
        error.Data["ProcessTreeTerminationConfirmed"] = false;
        var runner = new FakeWinPeProcessRunner(error, EmptyInventory());
        string image = Path.Combine(temp.Path, "boot.wim"), mount = Path.Combine(temp.Path, "mount");
        var result = await WinPeMountSession.MountAsync(runner, "dism.exe", image, mount, temp.Path, CancellationToken.None);
        Assert.Same(error, result.Error!.Exception);
        Assert.True(result.Error.RecoveryRequired);
        Assert.False(result.Error.NativeTerminationConfirmed);
        Assert.Contains(mount, result.Error.RetainedPaths);
        Assert.Equal(2, runner.Executions.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("The operation completed successfully.")]
    [InlineData("Mounted images:\nInvalid record\nThe operation completed successfully.")]
    [InlineData("Mounted images:\nMount Dir : C:\\mount\nThe operation completed successfully.")]
    public async Task ReconcileAsync_WhenInventoryUnreadable_RetainsOwnership(string output)
    {
        using var temp = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution { StandardOutput = output });
        var result = await WinPeMountRecovery.ReconcileOwnedMountAsync(runner, "dism.exe",
            Path.Combine(temp.Path, "boot.wim"), Path.Combine(temp.Path, "mount"), temp.Path, CancellationToken.None, true);
        Assert.True(result.Error!.RecoveryRequired);
        Assert.Single(runner.Executions);
    }

    [Fact]
    public async Task ReconcileAsync_WhenMountPathBelongsToDifferentImage_DoesNotDiscard()
    {
        using var temp = new TemporaryDirectory();
        string mount = Path.Combine(temp.Path, "mount");
        var runner = new FakeWinPeProcessRunner(MountedInventory(Path.Combine(temp.Path, "other.wim"), mount));
        var result = await WinPeMountRecovery.ReconcileOwnedMountAsync(runner, "dism.exe",
            Path.Combine(temp.Path, "boot.wim"), mount, temp.Path, CancellationToken.None, true);
        Assert.True(result.Error!.RecoveryRequired);
        Assert.Single(runner.Executions);
    }

    [Fact]
    public async Task ReconcileAsync_WhenOnlyPrefixSiblingMounted_ConfirmsOwnedPathAbsent()
    {
        using var temp = new TemporaryDirectory();
        string mount = Path.Combine(temp.Path, "mount"), image = Path.Combine(temp.Path, "boot.wim");
        var runner = new FakeWinPeProcessRunner(MountedInventory(Path.Combine(temp.Path, "other.wim"), mount + "-other"));
        var result = await WinPeMountRecovery.ReconcileOwnedMountAsync(runner, "dism.exe", image,
            mount, temp.Path, CancellationToken.None, true);
        Assert.True(result.IsSuccess);
        Assert.Single(runner.Executions);
    }

    [Fact]
    public async Task ReconcileAsync_WhenOwnedImageMountedElsewhere_PreservesImageAndDoesNotDiscard()
    {
        using var temp = new TemporaryDirectory();
        string mount = Path.Combine(temp.Path, "mount"), image = Path.Combine(temp.Path, "boot.wim");
        var runner = new FakeWinPeProcessRunner(MountedInventory(image, mount + "-other"));
        var result = await WinPeMountRecovery.ReconcileOwnedMountAsync(runner, "dism.exe", image,
            mount, temp.Path, CancellationToken.None, true);
        Assert.True(result.Error!.RecoveryRequired);
        Assert.Contains(image, result.Error.RetainedPaths);
        Assert.Single(runner.Executions);
    }

    [Fact]
    public async Task DiscardAsync_WhenFailedButConfirmedAbsent_AllowsDeletionWithoutHidingFailure()
    {
        using var temp = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution(), new WinPeProcessExecution { ExitCode = 5 }, EmptyInventory());
        var session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(temp.Path, "mount"), temp.Path, CancellationToken.None)).Value!;
        var result = await session.DiscardAsync(CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.False(result.Error!.RecoveryRequired);
        Assert.True(session.CanDeleteMountDirectory);
    }

    [Fact]
    public void ParseOwnedMount_WhenOutputTruncatedOrOversized_RejectsAbsence()
    {
        Assert.Throws<InvalidDataException>(() => WinPeMountRecovery.ParseOwnedMount("Mounted images:", @"C:\boot.wim", @"C:\mount"));
        Assert.Throws<InvalidDataException>(() => WinPeMountRecovery.ParseOwnedMount(new string(' ', 1024 * 1024 + 1), @"C:\boot.wim", @"C:\mount"));
    }

    private static WinPeProcessExecution EmptyInventory() => new()
    {
        StandardOutput = "Deployment Image Servicing and Management tool\nVersion: 10.0.26100.1\nMounted images:\n\nThe operation completed successfully."
    };

    private static WinPeProcessExecution MountedInventory(string image, string mount) => new()
    {
        StandardOutput = $"Mounted images:\nMount Dir : {mount}\nImage File : {image}\nImage Index : 1\nMounted Read/Write : Yes\nStatus : Ok\nThe operation completed successfully."
    };

    private sealed class FakeWinPeProcessRunner : IWinPeProcessRunner
    {
        private readonly Queue<object> _results;

        public FakeWinPeProcessRunner(params object[] results)
        {
            _results = new Queue<object>(results);
        }

        public List<WinPeProcessExecution> Executions { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null,
            TimeSpan? executionTimeout = null)
        {
            throw new NotSupportedException("Executable calls must pass argument tokens.");
        }

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            IReadOnlyList<string> argumentList,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null,
            TimeSpan? executionTimeout = null)
        {
            LastToken = cancellationToken;
            string arguments = string.Join(' ', argumentList);
            object next = _results.Dequeue();
            if (next is Exception error)
            {
                Executions.Add(new WinPeProcessExecution { Arguments = arguments });
                return Task.FromException<WinPeProcessExecution>(error);
            }
            WinPeProcessExecution result = (WinPeProcessExecution)next with
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            };

            Executions.Add(result);
            return Task.FromResult(result);
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            TimeSpan? executionTimeout = null)
        {
            return RunAsync(scriptPath, scriptArguments, workingDirectory, cancellationToken);
        }

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            TimeSpan? executionTimeout = null)
        {
            return RunAsync(scriptPath, scriptArguments, workingDirectory, cancellationToken);
        }
    }
}
