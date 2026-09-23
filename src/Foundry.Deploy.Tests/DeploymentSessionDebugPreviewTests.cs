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
using Microsoft.Extensions.Logging.Abstractions;
using DeployUnattendFile = Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentSessionDebugPreviewTests
{
    [Theory]
    [InlineData(DeploymentMode.Iso, false)]
    [InlineData(DeploymentMode.Iso, true)]
    [InlineData(DeploymentMode.Usb, false)]
    [InlineData(DeploymentMode.Usb, true)]
    public Task Progress_UsesSelectedWorkflowAndAlignsActiveRowWithStatus(DeploymentMode mode, bool customUnattend) =>
        WithSession(session =>
        {
            DeploymentContext request = Request() with
            {
                Mode = mode,
                Unattend = customUnattend ? new UnattendSelection(new DeployUnattendFile(), "answer.enc") : null,
                Oobe = new DeployOobeSettings { IsEnabled = true }
            };
            string[] expected =
            [
                .. customUnattend ? new[] { DeploymentStepNames.ValidateCustomUnattend } : [],
                DeploymentStepNames.ValidateTargetConfiguration,
                .. mode == DeploymentMode.Iso ? new[] { DeploymentStepNames.PrepareTargetDiskLayout } : [],
                DeploymentStepNames.DownloadOperatingSystemImage,
                DeploymentStepNames.CheckWindowsImage,
                .. mode == DeploymentMode.Usb ? new[] { DeploymentStepNames.PrepareTargetDiskLayout } : [],
                DeploymentStepNames.ApplyOperatingSystemImage,
                DeploymentStepNames.ConfigureWindowsBoot,
                .. customUnattend
                    ? new[] { DeploymentStepNames.StageCustomUnattend }
                    : [DeploymentStepNames.ConfigureTargetComputerName, DeploymentStepNames.ConfigureOobeSettings],
                DeploymentStepNames.ConfigureRecoveryEnvironment,
                DeploymentStepNames.SealRecoveryPartition,
                DeploymentStepNames.FinalizeDeploymentAndWriteLogs
            ];

            session.ShowDebugProgress(request);

            Assert.Equal(expected, session.TimelineEntries.Select(entry => entry.RawName));
            Assert.Equal(DeploymentPage.Progress, session.CurrentPage);
            Assert.Equal(request.TargetComputerName, session.ComputerNameText);
            AssertActiveStep(session, DeploymentStepState.Running, customUnattend ? 6 : 5, 11);
            AssertCachedImage(session);
            Assert.All(session.TimelineEntries.Where(entry => entry.StepIndex > (customUnattend ? 6 : 5)),
                entry => Assert.Equal(DeploymentStepState.Pending, entry.State));
        });

    [Theory]
    [InlineData(DriverPackSelectionKind.None, 10)]
    [InlineData(DriverPackSelectionKind.MicrosoftUpdateCatalog, 14)]
    [InlineData(DriverPackSelectionKind.OemCatalog, 13)]
    public Task Progress_OnlyDisplaysWorkForSelectedDriverStrategy(DriverPackSelectionKind selection, int count) =>
        WithSession(session =>
        {
            session.ShowDebugProgress(Request() with
            {
                DriverPackSelectionKind = selection,
                DriverPack = selection == DriverPackSelectionKind.OemCatalog
                    ? new DriverPackCatalogItem { Manufacturer = "Lenovo", FileName = "drivers.exe" }
                    : null
            });

            string[] driverSteps = session.TimelineEntries.Select(entry => entry.RawName)
                .Where(name => name is DeploymentStepNames.DownloadDriverPack or DeploymentStepNames.ExtractDriverPack
                    or DeploymentStepNames.ApplyDriverPack or DeploymentStepNames.StageDriverInstaller
                    or DeploymentStepNames.ApplyRecoveryDrivers or DeploymentStepNames.StagePreOobeCustomization).ToArray();
            string[] expected = selection switch
            {
                DriverPackSelectionKind.None => [],
                DriverPackSelectionKind.MicrosoftUpdateCatalog =>
                    [DeploymentStepNames.DownloadDriverPack, DeploymentStepNames.ExtractDriverPack,
                        DeploymentStepNames.ApplyDriverPack, DeploymentStepNames.ApplyRecoveryDrivers],
                _ => [DeploymentStepNames.DownloadDriverPack, DeploymentStepNames.StageDriverInstaller,
                    DeploymentStepNames.StagePreOobeCustomization]
            };
            Assert.Equal(expected, driverSteps);
            AssertActiveStep(session, DeploymentStepState.Running, 5, count);
        });

    [Theory]
    [InlineData(AutopilotProvisioningMode.JsonProfile, "Step.CopyAutopilotProfile")]
    [InlineData(AutopilotProvisioningMode.HardwareHashUpload, "Step.RegisterAutopilotDevice")]
    [InlineData(AutopilotProvisioningMode.InteractiveHardwareHashUpload, "Step.PrepareAutopilotAssistant")]
    public Task Success_PreservesSelectedAutopilotLabelAndCachedOutcome(AutopilotProvisioningMode mode, string labelKey) =>
        WithSession(session =>
        {
            session.ShowDebugSuccess(Request() with { IsAutopilotEnabled = true, AutopilotProvisioningMode = mode });

            DeploymentTimelineEntryViewModel autopilot = Assert.Single(session.TimelineEntries,
                entry => entry.RawName == DeploymentStepNames.ProvisionAutopilot);
            Assert.Equal(LocalizationText.GetString(labelKey), autopilot.DisplayName);
            Assert.Equal(DeploymentPage.Success, session.CurrentPage);
            Assert.Equal(11, session.TimelineEntries.Count);
            Assert.Equal(100, session.DeploymentProgress);
            Assert.Equal(LocalizationText.GetString("Step.FinalizeDeploymentAndWriteLogs"), session.CurrentStepName);
            Assert.Equal(LocalizationText.Format("Status.StepCounterFormat", 11, 11), session.StepCounterText);
            AssertCachedImage(session);
            Assert.All(session.TimelineEntries.Where(entry => entry.RawName != DeploymentStepNames.DownloadOperatingSystemImage),
                entry => Assert.Equal(DeploymentStepState.Succeeded, entry.State));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TerminalPreview_KeepsApplyStepAlignedAndProgressReentryClearsFailure(bool cancelled) =>
        WithSession(session =>
        {
            DeploymentContext request = Request();
            session.ShowDebugSuccess(request);
            if (cancelled)
                session.ShowDebugCancelled(request);
            else
                session.ShowDebugError(request, "Image preview failed.");

            Assert.Equal(DeploymentPage.Error, session.CurrentPage);
            Assert.Equal(cancelled, session.IsCancelled);
            Assert.Equal(!cancelled, session.HasDeploymentFailure);
            AssertActiveStep(session, cancelled ? DeploymentStepState.Cancelled : DeploymentStepState.Failed, 5, 10);
            AssertCachedImage(session);
            if (!cancelled)
            {
                Assert.Equal(session.CurrentStepName, session.FailedStepName);
                Assert.Equal("Image preview failed.", session.FailedStepErrorMessage);
            }

            session.ShowDebugProgress(request);

            Assert.Equal(DeploymentPage.Progress, session.CurrentPage);
            Assert.False(session.IsCancelled);
            Assert.False(session.HasDeploymentFailure);
            Assert.Empty(session.FailedStepName);
            Assert.Empty(session.FailedStepErrorMessage);
            AssertActiveStep(session, DeploymentStepState.Running, 5, 10);
            Assert.All(session.TimelineEntries.Skip(5), entry => Assert.Equal(DeploymentStepState.Pending, entry.State));
        });

    [Fact]
    public Task ReenterPreview_RebuildsPlanAfterSelectionsChange() => WithSession(session =>
    {
        session.ShowDebugSuccess(Request() with
        {
            Unattend = new UnattendSelection(new DeployUnattendFile(), "answer.enc"),
            IsAutopilotEnabled = true,
            DriverPackSelectionKind = DriverPackSelectionKind.MicrosoftUpdateCatalog
        });

        session.ShowDebugProgress(Request());

        Assert.DoesNotContain(session.TimelineEntries, entry => entry.RawName is DeploymentStepNames.ValidateCustomUnattend
            or DeploymentStepNames.StageCustomUnattend or DeploymentStepNames.ProvisionAutopilot
            or DeploymentStepNames.DownloadDriverPack or DeploymentStepNames.ApplyRecoveryDrivers);
        Assert.Contains(session.TimelineEntries, entry => entry.RawName == DeploymentStepNames.ConfigureTargetComputerName);
        AssertActiveStep(session, DeploymentStepState.Running, 5, 10);
        AssertCachedImage(session);
    });

    private static void AssertActiveStep(DeploymentSessionViewModel session, DeploymentStepState state, int index, int count)
    {
        DeploymentTimelineEntryViewModel active = Assert.Single(session.TimelineEntries, entry => entry.IsActive);
        Assert.Equal(DeploymentStepNames.ApplyOperatingSystemImage, active.RawName);
        Assert.Equal(state, active.State);
        Assert.Equal(index, active.StepIndex);
        Assert.Equal(count, session.TimelineEntries.Count);
        Assert.Equal(LocalizationText.GetString("Step.ApplyOperatingSystemImage"), active.DisplayName);
        Assert.Equal(active.DisplayName, session.CurrentStepName);
        Assert.Equal(LocalizationText.Format("Status.StepCounterFormat", index, count), session.StepCounterText);
        Assert.Equal(65, session.CurrentStepProgress);
        Assert.InRange(session.DeploymentProgress, 1, 99);
    }

    private static void AssertCachedImage(DeploymentSessionViewModel session)
    {
        DeploymentTimelineEntryViewModel download = Assert.Single(session.TimelineEntries,
            entry => entry.RawName == DeploymentStepNames.DownloadOperatingSystemImage);
        Assert.Equal(DeploymentStepState.Skipped, download.State);
        Assert.Equal(LocalizationText.GetString("StepResult.OperatingSystemImageResolvedFromCache"), download.DetailText);
    }

    private static DeploymentContext Request() => new()
    {
        Mode = DeploymentMode.Usb,
        CacheRootPath = "cache",
        TargetDiskNumber = 1,
        TargetComputerName = "PREVIEW-PC",
        OperatingSystem = new OperatingSystemCatalogItem { LicenseChannel = "VOL" },
        DriverPackSelectionKind = DriverPackSelectionKind.None,
        ApplyFirmwareUpdates = false
    };

    private static Task WithSession(Action<DeploymentSessionViewModel> test)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                using DeploymentSessionViewModel session = new(dispatcher, NullLogger.Instance,
                    new OperationProgressService(), new PreviewOnlyOrchestrator(), new RejectingProcessRunner(),
                    new LocalizationService(), isDebugSafeMode: true, new EmptyNetworkSnapshotProvider());
                test(session);
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                dispatcher.InvokeShutdown();
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private sealed class PreviewOnlyOrchestrator : IDeploymentOrchestrator
    {
        public event EventHandler<DeploymentStepProgress>? StepProgressChanged { add { } remove { } }
        public event EventHandler? CompletionStarting { add { } remove { } }
        public Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Previews must not execute deployment.");
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
