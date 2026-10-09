// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeWorkspaceCleanupRecoveryTests
{
    private const string DismPath = @"C:\Windows\System32\dism.exe";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recover_WhenImageIsNoLongerMounted_RemovesMarkerSoTheWorkspaceCanBeDeleted(bool markerInOperationRoot)
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path, markerInOperationRoot);
        var runner = new FakeWinPeProcessRunner();
        var service = new WinPeWorkspaceCleanupService(
            () => [new WinPeMountedImage(Path.Combine(temp.Path, "unrelated-mount"), Path.Combine(temp.Path, "unrelated.wim"))],
            runner);

        await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

        Assert.False(File.Exists(operation.MarkerPath));
        Assert.Empty(runner.Executions);
        Assert.True(Directory.Exists(operation.MountDirectory));
        Assert.True(service.EnsureServicingCanStart(temp.Path).IsSuccess);
        Assert.True(service.DeleteOwnedOperation(temp.Path, operation.Path).IsSuccess);
        Assert.False(Directory.Exists(operation.Path));
    }

    [Fact]
    public async Task Recover_WhenImageIsStillMountedAndDiscardClearsIt_RemovesMarker()
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        var mounted = new List<WinPeMountedImage> { Mounted(operation) };
        var protectedWhile = new List<(string Step, bool LeaseIsExclusive, bool MarkerExists)>();
        void Observe(string step) =>
            protectedWhile.Add((step, IsLeaseHeldExclusively(operation), File.Exists(operation.MarkerPath)));
        var runner = new FakeWinPeProcessRunner
        {
            OnDiscardAsync = _ =>
            {
                Observe("discard");
                mounted.Clear();
                return Task.FromResult(new WinPeProcessExecution());
            }
        };
        var service = new WinPeWorkspaceCleanupService(
            () =>
            {
                Observe("inventory");
                return mounted.ToArray();
            },
            runner);

        await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

        // The lease and the marker must cover the first inventory read, the discard and the confirming read.
        (string, bool, bool)[] expected = [("inventory", true, true), ("discard", true, true), ("inventory", true, true)];
        Assert.Equal(expected, protectedWhile);
        Assert.False(IsLeaseHeldExclusively(operation));
        WinPeProcessExecution discard = Assert.Single(runner.Executions);
        Assert.Equal(DismPath, discard.FileName);
        Assert.Equal(
            $"/English /Unmount-Image /MountDir:{WinPeProcessRunner.Quote(operation.MountDirectory)} /Discard",
            discard.Arguments);
        Assert.False(File.Exists(operation.MarkerPath));
        Assert.True(Directory.Exists(operation.Path));
        Assert.True(service.EnsureServicingCanStart(temp.Path).IsSuccess);
    }

    [Theory]
    [InlineData("nonzero-exit")]
    [InlineData("runner-throws")]
    [InlineData("still-mounted")]
    [InlineData("inventory-fails-after-discard")]
    public async Task Recover_WhenDiscardRetryFailsOrCannotBeConfirmed_KeepsMarkerAndExplainsManualRecovery(string outcome)
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        var mounted = new List<WinPeMountedImage> { Mounted(operation) };
        int inventoryReads = 0;
        var runner = new FakeWinPeProcessRunner
        {
            OnDiscardAsync = _ =>
            {
                switch (outcome)
                {
                    case "nonzero-exit":
                        // A failed DISM run is never trusted, even when Windows stops reporting the mount.
                        mounted.Clear();
                        return Task.FromResult(new WinPeProcessExecution { ExitCode = 50 });
                    case "runner-throws":
                        throw new IOException("dism.exe could not be started");
                    default:
                        return Task.FromResult(new WinPeProcessExecution());
                }
            }
        };
        var service = new WinPeWorkspaceCleanupService(
            () => ++inventoryReads > 1 && outcome == "inventory-fails-after-discard"
                ? throw new IOException("Inventory unavailable.")
                : mounted.ToArray(),
            runner);

        await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

        Assert.Single(runner.Executions);
        WinPeResult servicing = AssertStillBlocked(service, temp.Path, operation);
        Assert.Equal(WinPeMountCleanupStatus.ExitUnconfirmed, servicing.Error?.MountCleanupStatus);
        string details = servicing.Error!.Details!;
        Assert.Contains("Restart Windows", details, StringComparison.Ordinal);
        Assert.Contains($"dism /Unmount-Image /MountDir:\"{operation.MountDirectory}\" /Discard", details, StringComparison.Ordinal);
        Assert.Contains("dism /Cleanup-Mountpoints", details, StringComparison.Ordinal);
        Assert.Contains("start the operation again", details, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("different-case")]
    [InlineData("trailing-separator")]
    public async Task Recover_WhenInventoryReportsTheMountInAnotherForm_StillTreatsTheImageAsMounted(string form)
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        string reportedMountPath = form == "different-case"
            ? operation.MountDirectory.ToUpperInvariant()
            : operation.MountDirectory + Path.DirectorySeparatorChar;
        Assert.NotEqual(operation.MountDirectory, reportedMountPath);
        var runner = new FakeWinPeProcessRunner();
        var service = new WinPeWorkspaceCleanupService(
            () => [new WinPeMountedImage(reportedMountPath, Path.Combine(operation.Path, "WinPe", "media", "sources", "boot.wim"))],
            runner);

        await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

        Assert.Single(runner.Executions);
        AssertStillBlocked(service, temp.Path, operation);
    }

    [Fact]
    public async Task Recover_WhenDiscardExceedsTheCleanupDeadline_KeepsMarker()
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        var clock = new ControlledTimeProvider();
        var completion = new TaskCompletionSource<WinPeProcessExecution>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken cleanupToken = default;
        var runner = new FakeWinPeProcessRunner
        {
            OnDiscardAsync = token =>
            {
                cleanupToken = token;
                return completion.Task;
            }
        };
        var service = new WinPeWorkspaceCleanupService(() => [Mounted(operation)], runner, clock);

        Task recovery = service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);
        try
        {
            clock.Advance(WinPeMountSession.CleanupTimeout - TimeSpan.FromSeconds(1));
            Assert.False(cleanupToken.IsCancellationRequested);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(cleanupToken.IsCancellationRequested);
            Assert.False(recovery.IsCompleted);
            completion.SetException(new OperationCanceledException(cleanupToken));

            await recovery;
            AssertStillBlocked(service, temp.Path, operation);
        }
        finally
        {
            completion.TrySetException(new OperationCanceledException(cleanupToken));
            await recovery;
        }
    }

    [Fact]
    public async Task Recover_WhenSeveralMarkersNameTheSameMount_AttemptsDiscardOnce()
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        string secondMarker = Path.Combine(operation.Path, ".foundry-mount-cleanup-second.pending");
        File.WriteAllText(secondMarker, operation.MountDirectory);
        var runner = new FakeWinPeProcessRunner
        {
            OnDiscardAsync = _ => Task.FromResult(new WinPeProcessExecution { ExitCode = 50 })
        };
        var service = new WinPeWorkspaceCleanupService(() => [Mounted(operation)], runner);

        await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

        Assert.Single(runner.Executions);
        Assert.True(File.Exists(secondMarker));
        AssertStillBlocked(service, temp.Path, operation);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("relative")]
    [InlineData("outside")]
    [InlineData("traversal")]
    [InlineData("operation-root")]
    [InlineData("device-path")]
    [InlineData("command-injection")]
    public async Task Recover_WhenMarkerDoesNotNameAMountInsideItsOperation_KeepsMarker(string kind)
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        string content = kind switch
        {
            "empty" => string.Empty,
            "whitespace" => " \r\n",
            "relative" => Path.Combine("WinPe", "mount"),
            "outside" => Path.Combine(temp.Path, "outside", "mount"),
            "traversal" => Path.Combine(operation.Path, "WinPe", "..", "..", "outside", "mount"),
            "operation-root" => operation.Path,
            "device-path" => @"\\?\" + operation.MountDirectory,
            _ => operation.MountDirectory + "\" & calc & \""
        };
        File.WriteAllText(operation.MarkerPath, content);
        var runner = new FakeWinPeProcessRunner();

        // Neither an empty inventory nor one that reports the named path may act on such a marker.
        WinPeMountedImage[][] inventories =
        [
            [],
            Path.IsPathFullyQualified(content) ? [new WinPeMountedImage(content, Path.Combine(temp.Path, "boot.wim"))] : []
        ];
        foreach (WinPeMountedImage[] inventory in inventories)
        {
            var service = new WinPeWorkspaceCleanupService(() => inventory, runner);

            await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

            Assert.Empty(runner.Executions);
            WinPeResult servicing = AssertStillBlocked(service, temp.Path, operation, content);
            Assert.DoesNotContain("/MountDir:\"", servicing.Error!.Details!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Recover_WhenMarkerCannotBeRead_KeepsMarker()
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        var runner = new FakeWinPeProcessRunner();
        var service = new WinPeWorkspaceCleanupService(() => [], runner);

        using (new FileStream(operation.MarkerPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);
            Assert.False(service.EnsureServicingCanStart(temp.Path).IsSuccess);
        }

        Assert.Empty(runner.Executions);
        AssertStillBlocked(service, temp.Path, operation);
    }

    [Fact]
    public async Task Recover_WhenMountPathCrossesAReparsePoint_KeepsMarker()
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        string target = Path.Combine(temp.Path, "elsewhere");
        string link = Path.Combine(operation.Path, "WinPe", "windows-source-Pro");
        Directory.CreateDirectory(Path.Combine(target, "install-mount"));
        CreateJunction(link, target);
        try
        {
            string content = Path.Combine(link, "install-mount");
            File.WriteAllText(operation.MarkerPath, content);
            var runner = new FakeWinPeProcessRunner();
            var service = new WinPeWorkspaceCleanupService(() => [], runner);

            await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

            Assert.Empty(runner.Executions);
            AssertStillBlocked(service, temp.Path, operation, content);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recover_WhenInventoryCannotBeRead_KeepsMarker(bool inventoryUsesDevicePath)
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        var runner = new FakeWinPeProcessRunner();
        var service = new WinPeWorkspaceCleanupService(
            () => inventoryUsesDevicePath
                ? [new WinPeMountedImage(@"\\?\" + operation.MountDirectory, Path.Combine(temp.Path, "boot.wim"))]
                : throw new IOException("Inventory unavailable."),
            runner);

        await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

        Assert.Empty(runner.Executions);
        AssertStillBlocked(service, temp.Path, operation);
    }

    [Theory]
    [InlineData("unowned", false)]
    [InlineData("unowned", true)]
    [InlineData("foreign-owner", false)]
    [InlineData("foreign-owner", true)]
    [InlineData("lease-held", false)]
    [InlineData("lease-held", true)]
    public async Task Recover_WhenOperationIsNotOwnedOrStillActive_LeavesItUntouched(string scenario, bool imageIsMounted)
    {
        using var temp = new TemporaryDirectory();
        RetainedOperation operation = CreateRetainedOperation(temp.Path);
        string ownershipPath = Path.Combine(operation.Path, "operation.json");
        if (scenario == "unowned") File.Delete(ownershipPath);
        if (scenario == "foreign-owner") File.WriteAllText(ownershipPath, """{"Id":"other","Kind":"Foundry.OSD.Media"}""");
        using FileStream? activeLease = scenario == "lease-held"
            ? new FileStream(Path.Combine(operation.Path, ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete)
            : null;
        var runner = new FakeWinPeProcessRunner();
        var service = new WinPeWorkspaceCleanupService(() => imageIsMounted ? [Mounted(operation)] : [], runner);

        await service.RecoverUnresolvedMountCleanupsAsync(temp.Path, DismPath);

        Assert.Empty(runner.Executions);
        Assert.Equal(operation.MountDirectory, File.ReadAllText(operation.MarkerPath));
        Assert.True(Directory.Exists(operation.MountDirectory));
        Assert.Equal(scenario != "lease-held", service.EnsureServicingCanStart(temp.Path).IsSuccess);
    }

    private static RetainedOperation CreateRetainedOperation(string workspaceRoot, bool markerInOperationRoot = false)
    {
        string operationPath;
        string winPeDirectory;
        using (WinPeWorkspaceLease lease = WinPeWorkspaceLease.Create(workspaceRoot))
        {
            operationPath = lease.OperationDirectoryPath;
            winPeDirectory = lease.WinPeDirectoryPath;
        }

        string mountDirectory = Path.Combine(winPeDirectory, "mount");
        Directory.CreateDirectory(mountDirectory);
        string markerPath = Path.Combine(markerInOperationRoot ? operationPath : winPeDirectory,
            ".foundry-mount-cleanup-test.pending");
        File.WriteAllText(markerPath, mountDirectory);
        return new RetainedOperation(operationPath, mountDirectory, markerPath);
    }

    private static WinPeMountedImage Mounted(RetainedOperation operation) =>
        new(operation.MountDirectory, Path.Combine(operation.Path, "WinPe", "media", "sources", "boot.wim"));

    private static WinPeResult AssertStillBlocked(
        WinPeWorkspaceCleanupService service,
        string workspaceRoot,
        RetainedOperation operation,
        string? expectedMarkerContent = null)
    {
        Assert.Equal(expectedMarkerContent ?? operation.MountDirectory, File.ReadAllText(operation.MarkerPath));
        WinPeResult servicing = service.EnsureServicingCanStart(workspaceRoot);
        Assert.False(servicing.IsSuccess);
        Assert.Equal(WinPeErrorCodes.WimUnmountFailed, servicing.Error?.Code);
        Assert.False(service.DeleteOwnedOperation(workspaceRoot, operation.Path).IsSuccess);
        Assert.True(Directory.Exists(operation.MountDirectory));
        return servicing;
    }

    /// <summary>Reports whether another opener is refused the read/write access a live operation would need.</summary>
    private static bool IsLeaseHeldExclusively(RetainedOperation operation)
    {
        try
        {
            using var lease = new FileStream(Path.Combine(operation.Path, ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, target }) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"Junction creation failed: {output} {error}");
    }

    private sealed record RetainedOperation(string Path, string MountDirectory, string MarkerPath);

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
        /// <summary>Gets every started process, including attempts whose result is a failure or an exception.</summary>
        public List<WinPeProcessExecution> Executions { get; } = [];

        public Func<CancellationToken, Task<WinPeProcessExecution>> OnDiscardAsync { get; init; } =
            _ => Task.FromResult(new WinPeProcessExecution());

        public Task<WinPeProcessExecution> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            Executions.Add(new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            });
            return OnDiscardAsync(cancellationToken);
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
