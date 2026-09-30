// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class NativeDeploymentWorkflowTests
{
    [Fact]
    public async Task ApplyImageAsync_WhenWimApplies_UsesNativeServicing()
    {
        using var fixture = new WorkflowFixture();

        await fixture.Service.ApplyImageAsync("install.wim", 7, fixture.Root, fixture.Scratch, fixture.Working, TestContext.Current.CancellationToken);

        Assert.Equal(["wim:7"], fixture.Native.Calls);
        Assert.Empty(fixture.Process.Calls);
    }

    [Fact]
    public async Task ApplyImageAsync_WhenNativeApplyFails_DoesNotReplayThroughDism()
    {
        using var fixture = new WorkflowFixture();
        var primaryFailure = Failure("wim_apply_failure");
        fixture.Native.OnApplyWim = (_, _) => throw primaryFailure;

        await Assert.ThrowsAsync<DeploymentOperationException>(() => fixture.Service.ApplyImageAsync("install.wim", 7, fixture.Root, fixture.Scratch, fixture.Working, TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Process.Calls);
        Assert.Equal(["wim:7"], fixture.Native.Calls);
    }

    [Fact]
    public async Task ApplyImageAsync_WhenEsdApplies_UsesExplicitIntegrityCheckedDismRoute()
    {
        using var fixture = new WorkflowFixture();

        await fixture.Service.ApplyImageAsync("install.esd", 7, fixture.Root, fixture.Scratch, fixture.Working, TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Native.Calls);
        string call = Assert.Single(fixture.Process.Calls);
        Assert.Contains("/Apply-Image", call, StringComparison.Ordinal);
        Assert.Contains("/Index:7", call, StringComparison.Ordinal);
        Assert.Contains("/CheckIntegrity", call, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyOfflineDriversAsync_WhenDriversAreAvailable_UsesNativeInjection()
    {
        using var fixture = new WorkflowFixture();

        await fixture.Service.ApplyOfflineDriversAsync(fixture.Root, fixture.DriverRoot, fixture.Scratch, fixture.Working, TestContext.Current.CancellationToken);

        Assert.Equal(["drivers"], fixture.Native.Calls);
        Assert.Equal(fixture.Root, fixture.Native.LastDriverWindowsRoot);
        Assert.Empty(fixture.Process.Calls);
    }

    [Fact]
    public async Task ApplyRecoveryDriversAsync_WhenInjectionSucceeds_CommitsAndRemovesOwnedDirectory()
    {
        using var fixture = new WorkflowFixture();

        await fixture.ApplyRecoveryAsync();

        Assert.Equal(["mount", "drivers", "unmount:commit"], fixture.Native.Calls.Where(call => call != "inventory"));
        Assert.Equal(fixture.MountPath, fixture.Native.LastDriverWindowsRoot);
        Assert.False(Directory.Exists(fixture.MountPath));
        Assert.False(fixture.Native.LastUnmountToken.CanBeCanceled);
        Assert.Empty(fixture.Process.Calls);
    }

    [Fact]
    public async Task ApplyRecoveryDriversAsync_WhenInjectionFails_DiscardsAndPreservesPrimaryFailure()
    {
        using var fixture = new WorkflowFixture();
        var primaryFailure = Failure("driver_failure");
        fixture.Native.OnAddDrivers = () => throw primaryFailure;

        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() => fixture.ApplyRecoveryAsync());

        Assert.Same(primaryFailure, exception);
        Assert.Contains("unmount:discard", fixture.Native.Calls);
        Assert.DoesNotContain("unmount:commit", fixture.Native.Calls);
        Assert.False(Directory.Exists(fixture.MountPath));
    }

    [Fact]
    public async Task ApplyRecoveryDriversAsync_WhenCallerCancelsDuringInjection_CleansUpWithIndependentToken()
    {
        using var fixture = new WorkflowFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Native.OnAddDrivers = () =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ApplyRecoveryAsync(cancellation.Token));

        Assert.Contains("unmount:discard", fixture.Native.Calls);
        Assert.False(fixture.Native.LastUnmountToken.CanBeCanceled);
        Assert.False(Directory.Exists(fixture.MountPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyRecoveryDriversAsync_WhenDiscardFails_PreservesPrimaryFailureAndRegisteredMount(bool inventoryFails)
    {
        using var fixture = new WorkflowFixture();
        var primaryFailure = Failure("driver_failure");
        fixture.Native.OnAddDrivers = () => throw primaryFailure;
        fixture.Native.OnUnmount = () => throw Failure("cleanup_failure");
        fixture.Native.ReadMountState = () => inventoryFails && fixture.Native.Calls.Contains("unmount:discard", StringComparer.Ordinal)
            ? throw Failure("inventory_failure")
            : fixture.Native.Registered;

        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() => fixture.ApplyRecoveryAsync());

        Assert.Same(primaryFailure, exception);
        Assert.True(fixture.Native.Registered);
        Assert.True(File.Exists(Path.Combine(fixture.MountPath, "mounted-image.txt")));
    }

    [Fact]
    public async Task ApplyRecoveryDriversAsync_WhenMountRegistrationCannotBeRead_PreservesDirectory()
    {
        using var fixture = new WorkflowFixture();
        fixture.Native.ReadMountState = () => fixture.Native.Calls.Contains("unmount:commit", StringComparer.Ordinal)
            ? throw Failure("inventory_failure")
            : fixture.Native.Registered;

        await Assert.ThrowsAsync<DeploymentOperationException>(() => fixture.ApplyRecoveryAsync());

        Assert.True(File.Exists(Path.Combine(fixture.MountPath, "mounted-image.txt")));
    }

    [Fact]
    public async Task ApplyRecoveryDriversAsync_WhenMountIsAlreadyRegistered_DoesNotDeleteItsContents()
    {
        using var fixture = new WorkflowFixture();
        fixture.Native.Registered = true;
        Directory.CreateDirectory(fixture.MountPath);
        string existingContent = Path.Combine(fixture.MountPath, "existing.txt");
        await File.WriteAllTextAsync(existingContent, "preserve", TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.ApplyRecoveryAsync());

        Assert.Equal("preserve", await File.ReadAllTextAsync(existingContent, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("mount", fixture.Native.Calls);
        Assert.DoesNotContain("drivers", fixture.Native.Calls);
    }

    [Fact]
    public async Task ApplyRecoveryDriversAsync_WhenMountFailsAfterRegistering_DiscardsPartialMount()
    {
        using var fixture = new WorkflowFixture();
        var primaryFailure = Failure("mount_failure");
        fixture.Native.OnMount = () => throw primaryFailure;

        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() => fixture.ApplyRecoveryAsync());

        Assert.Same(primaryFailure, exception);
        Assert.Contains("unmount:discard", fixture.Native.Calls);
        Assert.DoesNotContain("drivers", fixture.Native.Calls);
        Assert.False(Directory.Exists(fixture.MountPath));
    }

    private static DeploymentOperationException Failure(string code) => new(
        DeploymentFailure.Guard(DeploymentOperationNames.ApplyRecoveryDrivers, DeploymentFailureReasons.InvalidState, code),
        code);

    private sealed class WorkflowFixture : IDisposable
    {
        public WorkflowFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"foundry-native-workflow-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "Recovery", "WindowsRE"));
            File.WriteAllText(Path.Combine(Root, "Recovery", "WindowsRE", "winre.wim"), "image");
            Service = new WindowsDeploymentService(Process, NullLogger<WindowsDeploymentService>.Instance, new StubWindowsImageInfoReader(), nativeService: Native);
        }

        public string Root { get; }
        public string Working => Path.Combine(Root, "Temp", "Deployment");
        public string Scratch => Path.Combine(Root, "Temp", "Dism");
        public string DriverRoot => Path.Combine(Root, "Drivers");
        public string MountPath => Path.Combine(Working, "Mount-WindowsRE");
        public RecordingNativeDeploymentService Native { get; } = new();
        public WorkflowProcessRunner Process { get; } = new();
        public WindowsDeploymentService Service { get; }

        public Task ApplyRecoveryAsync(CancellationToken? cancellationToken = null) =>
            Service.ApplyRecoveryDriversAsync(Root, DriverRoot, Scratch, Working, cancellationToken ?? TestContext.Current.CancellationToken);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class WorkflowProcessRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
        {
            Calls.Add($"{fileName} {arguments}");
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0 });
        }

        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken = default) =>
            RunAsync(fileName, string.Join(' ', arguments), workingDirectory, cancellationToken);

        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, Action<string>? onOutputData, Action<string>? onErrorData, CancellationToken cancellationToken = default) =>
            RunAsync(fileName, arguments, workingDirectory, cancellationToken);
    }
}

internal sealed class RecordingNativeDeploymentService : IWindowsNativeDeploymentService
{
    public List<string> Calls { get; } = [];
    public Func<IReadOnlyDictionary<string, OfflineWindowsFeatureState>>? ReadFeatures { get; init; }
    public Action<string>? OnDisable { get; init; }
    public Action<string, int>? OnApplyWim { get; set; }
    public Action? OnAddDrivers { get; set; }
    public Action? OnMount { get; set; }
    public Action? OnUnmount { get; set; }
    public Func<bool>? ReadMountState { get; set; }
    public bool Registered { get; set; }
    public string? LastDriverWindowsRoot { get; private set; }
    public CancellationToken LastUnmountToken { get; private set; }

    public Task EnsureAvailableAsync(bool requiresWim, string workingDirectory, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyDictionary<string, OfflineWindowsFeatureState>> ReadFeatureStatesAsync(string windowsRoot, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default)
    {
        Calls.Add("features");
        return Task.FromResult(ReadFeatures?.Invoke() ?? (IReadOnlyDictionary<string, OfflineWindowsFeatureState>)new Dictionary<string, OfflineWindowsFeatureState>());
    }

    public Task DisableFeatureAsync(string windowsRoot, string featureName, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default)
    {
        Calls.Add($"disable:{featureName}");
        OnDisable?.Invoke(featureName);
        return Task.CompletedTask;
    }

    public Task AddDriversAsync(string windowsRoot, string driverRoot, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        Calls.Add("drivers");
        LastDriverWindowsRoot = windowsRoot;
        OnAddDrivers?.Invoke();
        return Task.CompletedTask;
    }

    public Task MountImageAsync(string imagePath, string mountPath, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        Calls.Add("mount");
        Registered = true;
        File.WriteAllText(Path.Combine(mountPath, "mounted-image.txt"), "retain while mounted");
        OnMount?.Invoke();
        return Task.CompletedTask;
    }

    public Task UnmountImageAsync(string mountPath, bool commit, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        Calls.Add(commit ? "unmount:commit" : "unmount:discard");
        LastUnmountToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        OnUnmount?.Invoke();
        Registered = false;
        return Task.CompletedTask;
    }

    public Task<bool> IsMountedAsync(string mountPath, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default)
    {
        Calls.Add("inventory");
        return Task.FromResult(ReadMountState?.Invoke() ?? Registered);
    }

    public Task ApplyWimAsync(string imagePath, int imageIndex, string windowsRoot, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        Calls.Add($"wim:{imageIndex}");
        OnApplyWim?.Invoke(windowsRoot, imageIndex);
        return Task.CompletedTask;
    }
}
