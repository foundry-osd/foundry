// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class BootRecoveryServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryFailure_PreservesOriginalFailureAndOwnedMount(bool partialMount)
    {
        using var fixture = new Fixture();
        var primary = new InvalidOperationException("injection failed");
        fixture.Runner.MountFailure = partialMount ? primary : null;
        fixture.Runner.ApplyFailure = partialMount ? null : primary;
        fixture.Runner.UnmountFailure = new InvalidOperationException("cleanup failed");
        var service = new BootRecoveryService(fixture.Runner, NullLogger<BootRecoveryService>.Instance, new WindowsImagingService(fixture.Runner, NullLogger<WindowsImagingService>.Instance));
        Exception? actual = await Record.ExceptionAsync(() => service.ApplyRecoveryDriversAsync(fixture.Root,
            fixture.Root, fixture.Scratch, fixture.Work, TestContext.Current.CancellationToken));
        Assert.Same(primary, actual);
        Assert.NotNull(actual!.Data["FoundryCleanupFailure"]);
        Assert.True(Directory.Exists(fixture.Runner.MountPath));
        Assert.Contains(fixture.Runner.Calls, args => args.Contains("/Get-MountedImageInfo"));
    }

    [Fact]
    public async Task Success_CommitsAndVerifiesAbsenceBeforeDeletingUniqueMount()
    {
        using var fixture = new Fixture();
        string unrelated = Path.Combine(fixture.Work, "Mount-WindowsRE");
        Directory.CreateDirectory(unrelated);
        string marker = Path.Combine(unrelated, "keep.txt");
        File.WriteAllText(marker, "unrelated");
        var service = Create(fixture);
        await service.ApplyRecoveryDriversAsync(fixture.Root, fixture.Root, fixture.Scratch, fixture.Work, TestContext.Current.CancellationToken);
        Assert.Null(service.RecoveryDiagnostic);
        Assert.False(Directory.Exists(fixture.Runner.MountPath));
        Assert.True(File.Exists(marker));
        Assert.Contains(fixture.Runner.Calls, args => args.Contains("/Commit"));
        Assert.Equal(3, fixture.Runner.Calls.Count(args => args.Contains("/Get-MountedImageInfo")));
    }

    [Fact]
    public async Task CallerCancellation_UsesIndependentCleanupAndDiscards()
    {
        using var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        var primary = new OperationCanceledException(caller.Token);
        primary.Data["ProcessRootExitConfirmed"] = true;
        primary.Data["ProcessTreeTerminationConfirmed"] = true;
        fixture.Runner.ApplyFailure = primary;
        fixture.Runner.BeforeApply = caller.Cancel;
        Exception? actual = await Record.ExceptionAsync(() => Create(fixture).ApplyRecoveryDriversAsync(fixture.Root,
            fixture.Root, fixture.Scratch, fixture.Work, caller.Token));
        Assert.Same(primary, actual);
        Assert.Contains(fixture.Runner.Calls, args => args.Contains("/Discard"));
        Assert.False(fixture.Runner.CleanupTokenWasCancelled);
        Assert.False(Directory.Exists(fixture.Runner.MountPath));
    }

    [Fact]
    public async Task UncertainNativeExit_RetainsMountAndBlocksSubsequentMutation()
    {
        using var fixture = new Fixture();
        var primary = new TimeoutException("synthetic native uncertainty");
        fixture.Runner.MountFailure = primary;
        var service = Create(fixture);
        Assert.Same(primary, await Record.ExceptionAsync(() => service.ApplyRecoveryDriversAsync(fixture.Root,
            fixture.Root, fixture.Scratch, fixture.Work, TestContext.Current.CancellationToken)));
        Assert.Equal(RecoveryResourceState.RecoveryRequired, service.RecoveryDiagnostic?.State);
        Assert.True(Directory.Exists(fixture.Runner.MountPath));
        Assert.DoesNotContain(fixture.Runner.Calls, args => args.Contains("/Unmount-Image"));
        int calls = fixture.Runner.Calls.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfigureRecoveryEnvironmentAsync(fixture.Root, fixture.Root,
            fixture.Work, TestContext.Current.CancellationToken));
        Assert.Equal(calls, fixture.Runner.Calls.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidFinalInventory_RetainsMount(bool truncated)
    {
        using var fixture = new Fixture();
        fixture.Runner.InvalidFinalInventory = true;
        fixture.Runner.TruncatedInventory = truncated;
        var service = Create(fixture);
        await Assert.ThrowsAnyAsync<Exception>(() => service.ApplyRecoveryDriversAsync(fixture.Root, fixture.Root,
            fixture.Scratch, fixture.Work, TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(fixture.Runner.MountPath));
        Assert.Equal(RecoveryResourceState.RecoveryRequired, service.RecoveryDiagnostic?.State);
    }

    [Fact]
    public async Task ForeignImageAtOwnedMount_IsNotUnmountedOrDeleted()
    {
        using var fixture = new Fixture();
        fixture.Runner.ForeignMountedImage = true;
        var service = Create(fixture);
        await Assert.ThrowsAnyAsync<Exception>(() => service.ApplyRecoveryDriversAsync(fixture.Root, fixture.Root,
            fixture.Scratch, fixture.Work, TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(fixture.Runner.MountPath));
        Assert.DoesNotContain(fixture.Runner.Calls, args => args.Contains("/Unmount-Image"));
        Assert.Equal(RecoveryResourceState.RecoveryRequired, service.RecoveryDiagnostic?.State);
    }

    [Fact]
    public async Task CleanupTimeout_RetainsImageAndMount()
    {
        using var fixture = new Fixture();
        fixture.Runner.UnmountFailure = new TimeoutException("synthetic cleanup deadline");
        var service = Create(fixture);
        Assert.Same(fixture.Runner.UnmountFailure, await Record.ExceptionAsync(() => service.ApplyRecoveryDriversAsync(fixture.Root,
            fixture.Root, fixture.Scratch, fixture.Work, TestContext.Current.CancellationToken)));
        Assert.True(File.Exists(fixture.Runner.ImagePath));
        Assert.True(Directory.Exists(fixture.Runner.MountPath));
        Assert.Equal(RecoveryResourceState.RecoveryRequired, service.RecoveryDiagnostic?.State);
    }

    private static BootRecoveryService Create(Fixture fixture) => new(fixture.Runner, NullLogger<BootRecoveryService>.Instance,
        new WindowsImagingService(fixture.Runner, NullLogger<WindowsImagingService>.Instance));

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FoundryBootTests", Guid.NewGuid().ToString("N"));
        public string Work => Path.Combine(Root, "work");
        public string Scratch => Path.Combine(Root, "scratch");
        public Runner Runner { get; } = new();
        public Fixture()
        {
            string image = Path.Combine(Root, "Recovery", "WindowsRE", "winre.wim");
            Directory.CreateDirectory(Path.GetDirectoryName(image)!);
            File.WriteAllText(image, "owned synthetic fixture");
            Runner.ImagePath = image;
        }
        public void Dispose() { Directory.Delete(Root, true); }
    }

    private sealed class Runner : IProcessRunner
    {
        public List<string[]> Calls { get; } = [];
        public string ImagePath { get; set; } = "";
        public string? MountPath { get; private set; }
        public Exception? MountFailure { get; set; }
        public Exception? ApplyFailure { get; set; }
        public Exception? UnmountFailure { get; set; }
        public Action? BeforeApply { get; set; }
        public bool CleanupTokenWasCancelled { get; private set; }
        public bool ForeignMountedImage { get; set; }
        public bool InvalidFinalInventory { get; set; }
        public bool TruncatedInventory { get; set; }
        private bool unmounted;
        private bool mounted;
        public Task<ProcessExecutionResult> RunAsync(string file, string arguments, string work, CancellationToken token = default, TimeSpan? executionTimeout = null)
            => throw new NotSupportedException();
        public Task<ProcessExecutionResult> RunAsync(string file, IEnumerable<string> arguments, string work, CancellationToken token = default, TimeSpan? executionTimeout = null)
            => RunAsync(file, arguments, work, null, null, token, executionTimeout);
        public Task<ProcessExecutionResult> RunAsync(string file, IEnumerable<string> arguments, string work,
            Action<string>? output, Action<string>? error, CancellationToken token = default, TimeSpan? executionTimeout = null)
        {
            string[] args = arguments.ToArray();
            Calls.Add(args);
            if (args.Contains("/Mount-Image"))
            {
                MountPath = args.Single(a => a.StartsWith("/MountDir:"))[10..];
                mounted = true;
                if (MountFailure is not null) throw MountFailure;
            }
            if (args.Contains("/Add-Driver"))
            {
                BeforeApply?.Invoke();
                if (ApplyFailure is not null) throw ApplyFailure;
            }
            if (args.Contains("/Unmount-Image"))
            {
                CleanupTokenWasCancelled = token.IsCancellationRequested;
                unmounted = true;
                if (UnmountFailure is not null) throw UnmountFailure;
                mounted = false;
            }
            string inventory = "Mounted images:\n" + (mounted ? $"Mount Dir : {MountPath}\nImage File : {(ForeignMountedImage ? ImagePath + ".foreign" : ImagePath)}\nImage Index : 1\nMounted Read/Write : Yes\nStatus : Ok\n" : "") + "The operation completed successfully.";
            return Task.FromResult(new ProcessExecutionResult { StandardOutput = unmounted && InvalidFinalInventory && !TruncatedInventory ? "incomplete" : inventory, StandardOutputTruncated = unmounted && InvalidFinalInventory && TruncatedInventory });
        }
    }
}
