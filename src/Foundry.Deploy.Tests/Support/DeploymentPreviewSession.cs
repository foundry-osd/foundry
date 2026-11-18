// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Threading;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.ViewModels;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Processes;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

/// <summary>
/// Builds a debug-safe Deploy session that can only preview its pages. Also compiled into Foundry.WinPE.Tests, whose progress
/// page binding test uses it.
/// </summary>
internal static class DeploymentPreviewSession
{
    /// <summary>A deployment request for the debug previews.</summary>
    internal static DeploymentContext Request() => new()
    {
        Mode = DeploymentMode.Usb,
        CacheRootPath = "cache",
        TargetDiskNumber = 1,
        TargetComputerName = "PREVIEW-PC",
        OperatingSystem = new OperatingSystemCatalogItem { LicenseChannel = "VOL" },
        DriverPackSelectionKind = DriverPackSelectionKind.None,
        ApplyFirmwareUpdates = false
    };

    /// <summary>Creates a debug-safe session that can only preview: it never runs a deployment step or a process.</summary>
    internal static DeploymentSessionViewModel Create(Dispatcher dispatcher) =>
        new(dispatcher, NullLogger.Instance, new OperationProgressService(), new PreviewOnlyOrchestrator(), new RejectingProcessRunner(),
            new ResourceLocalizationService(LocalizationText.ResourceManager), isDebugSafeMode: true, new EmptyNetworkSnapshotProvider());

    private sealed class PreviewOnlyOrchestrator : IDeploymentOrchestrator
    {
        public event EventHandler<DeploymentStepProgress>? StepProgressChanged { add { } remove { } }
        public event EventHandler? CompletionStarting { add { } remove { } }
        public Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Previews must not execute deployment.");
        public Task<DeploymentResult> RunAsync(DeploymentContext context, Foundry.Deploy.Services.DomainJoin.DomainJoinPreparedInput? input,
            CancellationToken cancellationToken = default) => RunAsync(context, cancellationToken);
    }

    private sealed class EmptyNetworkSnapshotProvider : INetworkAdapterSnapshotProvider
    {
        public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters() => [];
    }

    private sealed class RejectingProcessRunner : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, string workingDirectory,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Previews must not run processes.");

        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Previews must not run processes.");

        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory,
            Action<string>? onOutputData, Action<string>? onErrorData, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Previews must not run processes.");
    }
}
