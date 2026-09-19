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
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution { ExitCode = 5, StandardError = "mount failed" });

        WinPeResult<WinPeMountSession> result = await WinPeMountSession.MountAsync(
            runner,
            "dism.exe",
            "boot.wim",
            Path.Combine(tempDirectory.Path, "mount"),
            tempDirectory.Path,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimMountFailed, result.Error?.Code);
        Assert.Equal(5, result.Error?.ExitCode);
        Assert.Equal(WinPeFailureKinds.Process, result.Error?.FailureKind);
        Assert.Equal("dism.exe", result.Error?.ToolName);
        Assert.Single(runner.Executions);
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
        Assert.Equal(7, result.Error?.ExitCode);
        Assert.Equal(WinPeFailureReasons.NonZeroExit, result.Error?.FailureReason);
        Assert.Contains("commit failed", result.Error?.Details, StringComparison.Ordinal);
        Assert.Contains("Commit diagnostics", result.Error?.Details, StringComparison.Ordinal);
        Assert.Contains("Discard diagnostics", result.Error?.Details, StringComparison.Ordinal);
        Assert.Equal(3, runner.Executions.Count);
        Assert.Contains("/Discard", runner.Executions[2].Arguments, StringComparison.Ordinal);
        Assert.All(runner.Executions, execution => Assert.StartsWith("/English ", execution.Arguments));
    }

    [Fact]
    public async Task DiscardAsync_WhenDismFails_PreservesProcessDetails()
    {
        using var tempDirectory = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution(), new WinPeProcessExecution { ExitCode = 9 });
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(tempDirectory.Path, "mount"), tempDirectory.Path, TestContext.Current.CancellationToken)).Value!;

        WinPeResult result = await session.DiscardAsync();

        Assert.Equal(9, result.Error?.ExitCode);
        Assert.Equal(WinPeFailureKinds.Process, result.Error?.FailureKind);
        Assert.Equal("dism.exe", result.Error?.ToolName);
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
        Assert.All(runner.Executions, execution => Assert.StartsWith("/English ", execution.Arguments));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedUnmount_DisposeRetriesUntilAnAttemptSucceeds(bool commit)
    {
        using var directory = new TemporaryDirectory();
        var runner = new FakeWinPeProcessRunner(commit
            ? [new(), new() { ExitCode = 7 }, new() { ExitCode = 9 }, new()]
            : [new(), new() { ExitCode = 9 }, new()]);
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(directory.Path, "mount"), directory.Path, TestContext.Current.CancellationToken)).Value!;

        WinPeResult result = commit
            ? await session.CommitAsync(TestContext.Current.CancellationToken)
            : await session.DiscardAsync();
        Assert.Empty(Directory.GetFiles(directory.Path, ".foundry-mount-cleanup-*.pending"));
        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(commit ? 7 : 9, result.Error?.ExitCode);
        Assert.Equal(2, runner.Executions.Count(execution => execution.Arguments.Contains("/Discard", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CommitAsync_WhenFallbackThrows_PreservesCommitFailureAndBlocksAnotherAttempt()
    {
        using var directory = new TemporaryDirectory();
        int discards = 0;
        var runner = new FakeWinPeProcessRunner(new(), new() { ExitCode = 7, StandardError = "commit failed" }, new())
        {
            OnRun = (arguments, _) =>
            {
                if (arguments.Contains("/Discard", StringComparison.Ordinal) && ++discards == 1)
                {
                    throw new IOException("discard failed to start");
                }
            }
        };
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(directory.Path, "mount"), directory.Path, TestContext.Current.CancellationToken)).Value!;

        WinPeResult result = await session.CommitAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        Assert.Equal(7, result.Error?.ExitCode);
        Assert.Contains("commit failed", result.Error?.Details, StringComparison.Ordinal);
        Assert.Contains("discard failed to start", result.Error?.Details, StringComparison.Ordinal);
        Assert.Equal(1, discards);
        string marker = Assert.Single(Directory.GetFiles(directory.Path, ".foundry-mount-cleanup-*.pending"));
        Assert.Contains(marker, result.Error?.Details, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAsync_WhenCleanupThrows_PreservesPrimaryException(bool cancellation)
    {
        using var directory = new TemporaryDirectory();
        Exception primary = cancellation ? new OperationCanceledException("cancelled") : new InvalidOperationException("primary failure");
        int discards = 0;
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution())
        {
            OnRun = (arguments, _) =>
            {
                if (arguments.Contains("/Discard", StringComparison.Ordinal))
                {
                    discards++;
                    throw new IOException("cleanup failed");
                }
            }
        };
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(directory.Path, "mount"), directory.Path, TestContext.Current.CancellationToken)).Value!;

        Exception? observed = await Record.ExceptionAsync(async () =>
        {
            await using (session)
            {
                throw primary;
            }
        });

        Assert.Same(primary, observed);
        Assert.Equal(1, discards);
    }

    [Fact]
    public async Task CommitAsync_WhenCallerCancelsDuringFailure_UsesIndependentCleanupLifetime()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        CancellationToken cleanupToken = default;
        var runner = new FakeWinPeProcessRunner(new(), new() { ExitCode = 7 }, new())
        {
            OnRun = (arguments, token) =>
            {
                if (arguments.Contains("/Commit", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
                if (arguments.Contains("/Discard", StringComparison.Ordinal))
                {
                    cleanupToken = token;
                    token.ThrowIfCancellationRequested();
                }
            }
        };
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(directory.Path, "mount"), directory.Path, TestContext.Current.CancellationToken)).Value!;

        WinPeResult result = await session.CommitAsync(cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(7, result.Error?.ExitCode);
        Assert.True(cleanupToken.CanBeCanceled);
        Assert.NotEqual(cancellation.Token, cleanupToken);
    }

    [Fact]
    public async Task DiscardAsync_WhenDeadlineExpires_RetainsMarkerAndBlocksAnotherAttemptAfterRunnerReturns()
    {
        using var directory = new TemporaryDirectory();
        var clock = new ControlledTimeProvider();
        var completion = new TaskCompletionSource<WinPeProcessExecution>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken cleanupToken = default;
        int discards = 0;
        var runner = new FakeWinPeProcessRunner(new WinPeProcessExecution())
        {
            OnDiscardAsync = token =>
            {
                cleanupToken = token;
                return ++discards == 1 ? completion.Task : Task.FromResult(new WinPeProcessExecution());
            }
        };
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(directory.Path, "mount"), directory.Path, TestContext.Current.CancellationToken, clock)).Value!;

        Task<WinPeResult> discard = session.DiscardAsync();
        try
        {
            string marker = Assert.Single(Directory.GetFiles(directory.Path, ".foundry-mount-cleanup-*.pending"));
            clock.Advance(TimeSpan.FromMinutes(4));
            Assert.False(cleanupToken.IsCancellationRequested);
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True(cleanupToken.IsCancellationRequested);
            Assert.False(discard.IsCompleted);
            completion.SetException(new OperationCanceledException(cleanupToken));

            WinPeResult result = await discard;
            Assert.False(result.IsSuccess);
            Assert.Equal(WinPeFailureKinds.Process, result.Error?.FailureKind);
            Assert.Equal(WinPeFailureReasons.Timeout, result.Error?.FailureReason);
            Assert.True(File.Exists(marker));
            Assert.Contains(marker, result.Error?.Details, StringComparison.Ordinal);
            await session.DisposeAsync();
            WinPeResult retryResult = await session.DiscardAsync();
            Assert.False(retryResult.IsSuccess);
            Assert.Equal(result.Error, retryResult.Error);
            WinPeResult commitResult = await session.CommitAsync(TestContext.Current.CancellationToken);
            Assert.False(commitResult.IsSuccess);
            Assert.Equal(result.Error, commitResult.Error);
            Assert.Equal(1, discards);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            completion.TrySetException(new OperationCanceledException(cleanupToken));
            await discard;
        }
    }

    [Fact]
    public async Task DiscardAsync_WhenMarkerCannotBeCreated_DoesNotStartCleanupProcess()
    {
        using var directory = new TemporaryDirectory();
        string workingDirectory = Path.Combine(directory.Path, "working");
        Directory.CreateDirectory(workingDirectory);
        var runner = new FakeWinPeProcessRunner(new(), new());
        WinPeMountSession session = (await WinPeMountSession.MountAsync(runner, "dism.exe", "boot.wim",
            Path.Combine(directory.Path, "mount"), workingDirectory, TestContext.Current.CancellationToken)).Value!;
        Directory.Delete(workingDirectory);
        await File.WriteAllTextAsync(workingDirectory, "occupied", TestContext.Current.CancellationToken);

        WinPeResult result = await session.DiscardAsync();

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error?.Exception);
        Assert.Single(runner.Executions);
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private TimeSpan _elapsed;
        private readonly List<ControlledTimer> _timers = [];

        public void Advance(TimeSpan elapsed)
        {
            _elapsed += elapsed;
            foreach (ControlledTimer timer in _timers.ToArray())
            {
                if (!timer.Disposed && timer.DueAt <= _elapsed) timer.Fire();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(this, callback, state, dueTime);
            _timers.Add(timer);
            return timer;
        }

        private sealed class ControlledTimer(ControlledTimeProvider clock, TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            public TimeSpan DueAt { get; private set; } = clock._elapsed + dueTime;
            public bool Disposed { get; private set; }
            public void Fire() => callback(state);
            public bool Change(TimeSpan nextDueTime, TimeSpan period)
            {
                DueAt = clock._elapsed + nextDueTime;
                return true;
            }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class FakeWinPeProcessRunner : IWinPeProcessRunner
    {
        private readonly Queue<WinPeProcessExecution> _results;

        public FakeWinPeProcessRunner(params WinPeProcessExecution[] results)
        {
            _results = new Queue<WinPeProcessExecution>(results);
        }

        public List<WinPeProcessExecution> Executions { get; } = [];
        public Action<string, CancellationToken>? OnRun { get; init; }
        public Func<CancellationToken, Task<WinPeProcessExecution>>? OnDiscardAsync { get; init; }

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            OnRun?.Invoke(arguments, cancellationToken);
            if (OnDiscardAsync is not null && arguments.Contains("/Discard", StringComparison.Ordinal))
            {
                return OnDiscardAsync(cancellationToken);
            }
            WinPeProcessExecution result = _results.Dequeue() with
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
            CancellationToken cancellationToken)
        {
            return RunAsync(scriptPath, scriptArguments, workingDirectory, cancellationToken);
        }

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
            string scriptPath,
            string scriptArguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            return RunAsync(scriptPath, scriptArguments, workingDirectory, cancellationToken);
        }
    }
}
