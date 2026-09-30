// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.Deployment.Native;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Adapts deployment operations to the same executable's isolated native worker.</summary>
public sealed class WindowsNativeDeploymentService(IProcessRunner processRunner) : IWindowsNativeDeploymentService
{
    private readonly NativeWorkerClient _worker = new(processRunner);

    /// <inheritdoc />
    public async Task EnsureAvailableAsync(bool requiresWim, string workingDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            await _worker.ExecuteAsync(new() { Operation = NativeWorkerOperation.ProbeDism }, workingDirectory,
                DeploymentOperationNames.PreflightDeployment, null, cancellationToken).ConfigureAwait(false);
            if (requiresWim)
                await _worker.ExecuteAsync(new() { Operation = NativeWorkerOperation.ProbeWim }, workingDirectory,
                    DeploymentOperationNames.PreflightDeployment, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new DeploymentOperationException(DeploymentFailure.Guard(DeploymentOperationNames.PreflightDeployment,
                DeploymentFailureReasons.MissingResource, "native_api_unavailable"),
                "Required native deployment APIs are unavailable. Rebuild the WinPE media with the supported servicing components.", exception);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, OfflineWindowsFeatureState>> ReadFeatureStatesAsync(string windowsRoot,
        string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default)
    {
        NativeWorkerResult result = await ExecuteAsync(CreateRequest(NativeWorkerOperation.ReadFeatures, scratchDirectory, workingDirectory)
            with
        { WindowsRoot = windowsRoot }, workingDirectory, DeploymentOperationNames.InspectWindowsOptionalFeatures, null, cancellationToken).ConfigureAwait(false);
        var states = new Dictionary<string, OfflineWindowsFeatureState>(StringComparer.OrdinalIgnoreCase);
        foreach (NativeWindowsFeature feature in result.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Name) || !Enum.IsDefined(feature.State) || !states.TryAdd(feature.Name, feature.State))
                throw new InvalidDataException("The native feature inventory is invalid or ambiguous.");
        }
        if (states.Count == 0) throw new InvalidDataException("The native feature inventory is empty.");
        return states;
    }

    /// <inheritdoc />
    public async Task DisableFeatureAsync(string windowsRoot, string featureName, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(CreateRequest(NativeWorkerOperation.DisableFeature, scratchDirectory, workingDirectory)
            with
        { WindowsRoot = windowsRoot, FeatureName = featureName }, workingDirectory,
            DeploymentOperationNames.ConfigureWindowsOptionalFeatures, null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AddDriversAsync(string windowsRoot, string driverRoot, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(CreateRequest(NativeWorkerOperation.AddDrivers, scratchDirectory, workingDirectory)
            with
        { WindowsRoot = windowsRoot, DriverRoot = driverRoot }, workingDirectory,
            DeploymentOperationNames.ApplyDriverPack, progress, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task MountImageAsync(string imagePath, string mountPath, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(CreateRequest(NativeWorkerOperation.MountImage, scratchDirectory, workingDirectory)
            with
        { ImagePath = imagePath, MountPath = mountPath }, workingDirectory,
            DeploymentOperationNames.MountRecoveryImage, progress, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task UnmountImageAsync(string mountPath, bool commit, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(CreateRequest(NativeWorkerOperation.UnmountImage, scratchDirectory, workingDirectory)
            with
        { MountPath = mountPath, Commit = commit }, workingDirectory,
            DeploymentOperationNames.UnmountRecoveryImage, progress, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> IsMountedAsync(string mountPath, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default)
    {
        NativeWorkerResult result = await ExecuteAsync(CreateRequest(NativeWorkerOperation.InspectMount, scratchDirectory, workingDirectory)
            with
        { MountPath = mountPath }, workingDirectory, DeploymentOperationNames.MountRecoveryImage, null, cancellationToken).ConfigureAwait(false);
        return result.MountRegistered;
    }

    /// <inheritdoc />
    public async Task ApplyWimAsync(string imagePath, int imageIndex, string windowsRoot, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(CreateRequest(NativeWorkerOperation.ApplyWim, scratchDirectory, workingDirectory)
            with
        { ImagePath = imagePath, ImageIndex = imageIndex, WindowsRoot = windowsRoot }, workingDirectory,
            DeploymentOperationNames.ApplyOperatingSystemImage, progress, cancellationToken).ConfigureAwait(false);

    private Task<NativeWorkerResult> ExecuteAsync(NativeWorkerRequest request, string workingDirectory, string operationName, IProgress<double>? progress, CancellationToken cancellationToken) =>
        _worker.ExecuteAsync(request, workingDirectory, operationName, progress, cancellationToken);

    private static NativeWorkerRequest CreateRequest(NativeWorkerOperation operation, string scratchDirectory, string workingDirectory)
    {
        Directory.CreateDirectory(scratchDirectory);
        DirectoryInfo? parent = Directory.GetParent(Path.GetFullPath(workingDirectory));
        string logDirectory = parent?.Name.Equals("Temp", StringComparison.OrdinalIgnoreCase) == true && parent.Parent is not null
            ? Path.Combine(parent.Parent.FullName, "Logs", "Native")
            : Path.Combine(workingDirectory, "NativeLogs");
        Directory.CreateDirectory(logDirectory);
        return new()
        {
            Operation = operation,
            ScratchDirectory = scratchDirectory,
            LogFilePath = Path.Combine(logDirectory, operation == NativeWorkerOperation.ApplyWim ? "Wimgapi.log" : "Dism.log")
        };
    }
}
