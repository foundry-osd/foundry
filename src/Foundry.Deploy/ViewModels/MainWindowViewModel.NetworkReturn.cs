// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Startup;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.ViewModels;

/// <summary>
/// What a return from the Foundry Connect page runs again, and the Network choice of the startup failure dialogs.
/// </summary>
public partial class MainWindowViewModel
{
    private bool _autopilotGroupTagsFailed;
    private BootMediaLatestReleaseCheck? _latestReleaseCheck;
    private Task? _releaseNoticeTask;
    private bool _isReleaseNoticeResolved;
    private bool _isNetworkPageOpenFromDialog;
    private bool _isRefreshingAfterNetworkReturn;

    /// <summary>What the operator's choices in the network cache failure dialog led to.</summary>
    private enum NetworkCacheFailureResolution
    {
        /// <summary>A retry resolved the network cache without a failure.</summary>
        Resolved,

        /// <summary>The operator continues without the network cache and its content.</summary>
        ContinueWithout,

        /// <summary>The operator closed Foundry Deploy.</summary>
        Closed
    }

    /// <summary>Gets whether the failure dialogs offer Network: only when the host can show the Foundry Connect page.</summary>
    private bool CanOfferNetworkInDialogs => _networkPageNavigator?.IsAvailable == true;

    /// <summary>
    /// Runs again, after the Foundry Connect page opened from the menu returned, what may have failed for lack of network:
    /// the network cache when its session has no usable share or the local addresses changed (with the startup failure dialog,
    /// whose Close ends Deploy as at startup), the Autopilot group tag discovery when it failed, and the newer release notice
    /// when it has no result yet. Nothing else is applied again, so the operator's choices are kept. Does nothing when the page
    /// was opened from a failure dialog, whose retry follows, or before the wizard is shown. Called on the UI thread.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the phase ends.</param>
    internal async Task RefreshAfterNetworkReturnAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed || !_isInitialized || _isNetworkPageOpenFromDialog || Session.CurrentPage != DeploymentPage.Wizard)
        {
            return;
        }

        bool networkCache = _deploymentStartupCoordinator.ShouldResolveNetworkCacheAgain();
        bool groupTags = _autopilotGroupTagsFailed;
        bool releaseNotice = _latestReleaseCheck is not null && !Volatile.Read(ref _isReleaseNoticeResolved)
            && _releaseNoticeTask is not { IsCompleted: false };
        _logger.LogInformation(
            "Refreshing after the Network page returned. NetworkCache={NetworkCache}, AutopilotGroupTags={AutopilotGroupTags}, ReleaseNotice={ReleaseNotice}",
            networkCache,
            groupTags,
            releaseNotice);
        if (releaseNotice)
        {
            _releaseNoticeTask = ShowNewerReleaseNoticeAsync(_latestReleaseCheck!, _startupCancellation.Token);
        }

        if (!networkCache && !groupTags)
        {
            return;
        }

        // The menu and the start stay disabled meanwhile: the network cache session and the wizard content change underneath.
        SetRefreshingAfterNetworkReturn(true);
        try
        {
            if (networkCache && !await RefreshNetworkCacheAfterReturnAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (groupTags)
            {
                await RefreshAutopilotGroupTagsAfterReturnAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            RunOnUi(() => SetRefreshingAfterNetworkReturn(false));
        }
    }

    /// <summary>
    /// Resolves the network cache again and applies its content to the wizard. Returns <see langword="false"/> when the
    /// operator closed Foundry Deploy from the failure dialog.
    /// </summary>
    private async Task<bool> RefreshNetworkCacheAfterReturnAsync(CancellationToken cancellationToken)
    {
        NetworkCacheStartupResult? result = await ResolveNetworkCacheAgainAsync(cancellationToken).ConfigureAwait(false);
        if (result?.Failure is { } failure)
        {
            NetworkCacheFailureResolution resolution = await ResolveNetworkCacheFailureAsync(failure, async token =>
            {
                result = await ResolveNetworkCacheAgainAsync(token).ConfigureAwait(false);
                return result?.Failure;
            }, cancellationToken).ConfigureAwait(false);
            if (resolution == NetworkCacheFailureResolution.Closed)
            {
                return false;
            }

            if (resolution == NetworkCacheFailureResolution.ContinueWithout && result is not null)
            {
                result = result with { Document = NetworkCacheStartupCheck.WithoutPublishedContent(result.Document), Failure = null };
            }
        }

        if (result is { } resolved)
        {
            RunOnUi(() => _wizardContext.ApplyNetworkCacheContent(resolved.Document));
        }

        return true;
    }

    // Off the UI thread: the resolution reads the adapters and the configuration before its first wait.
    private Task<NetworkCacheStartupResult?> ResolveNetworkCacheAgainAsync(CancellationToken cancellationToken) =>
        Task.Run(() => _deploymentStartupCoordinator.ResolveNetworkCacheAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Runs the failed group tag discovery again and offers the discovered tags in the Autopilot step, keeping the selected
    /// tag when it is still offered. A debug Autopilot scenario keeps its own tags.
    /// </summary>
    private async Task RefreshAutopilotGroupTagsAfterReturnAsync(CancellationToken cancellationToken)
    {
        AutopilotGroupTagRefresh refresh = await Task.Run(
            () => _deploymentStartupCoordinator.RefreshAutopilotGroupTagsAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        RunOnUi(() =>
        {
            _autopilotGroupTagsFailed = refresh.Failed;
            if (!refresh.Failed && _debugAutopilotMode == DebugAutopilotMode.None)
            {
                Preparation.ApplyDiscoveredHardwareHashGroupTags(refresh.Document.Autopilot.HardwareHashUpload.KnownGroupTags);
            }
        });
    }

    /// <summary>
    /// Offers Retry, Network, Continue without and Close while the network cache prevents the deployment from starting, at
    /// startup and after a return from the Network page. Retry, and Network once the page closed, run
    /// <paramref name="retryAsync"/>, which returns the next failure or <see langword="null"/>. Close reports the blocked
    /// start and ends Foundry Deploy.
    /// </summary>
    private async Task<NetworkCacheFailureResolution> ResolveNetworkCacheFailureAsync(NetworkCacheStartupFailure failure,
        Func<CancellationToken, Task<NetworkCacheStartupFailure?>> retryAsync, CancellationToken cancellationToken)
    {
        while (true)
        {
            NetworkCacheStartupChoice choice = NetworkCacheStartupChoice.Close;
            NetworkCacheStartupFailure shown = failure;
            RunOnUi(() => choice = _networkCacheStartupDialog.ShowFailure(shown, CanOfferNetworkInDialogs));
            if (choice == NetworkCacheStartupChoice.ContinueWithout)
            {
                // The failed session is already unavailable, so downloads neither read from nor publish to a share.
                _logger.LogWarning(
                    "Deployment continues without the network cache: custom images, custom driver sets and post-installation actions are not available. FailureKind={FailureKind}",
                    failure.Kind);
                return NetworkCacheFailureResolution.ContinueWithout;
            }

            if (choice == NetworkCacheStartupChoice.Close)
            {
                await CloseForNetworkCacheFailureAsync(failure).ConfigureAwait(false);
                return NetworkCacheFailureResolution.Closed;
            }

            if (choice == NetworkCacheStartupChoice.Network)
            {
                await OpenNetworkPageFromDialogAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Network cache startup check retried after FailureKind={FailureKind}.", failure.Kind);
            if (await retryAsync(cancellationToken).ConfigureAwait(false) is not { } next)
            {
                return NetworkCacheFailureResolution.Resolved;
            }

            failure = next;
        }
    }

    private async Task CloseForNetworkCacheFailureAsync(NetworkCacheStartupFailure failure)
    {
        _logger.LogWarning("Deployment closed because the network cache cannot be used. FailureKind={FailureKind}", failure.Kind);
        // The blocked start ends the session: report it as one. The network is often what failed, so Close waits for the
        // send only briefly.
        try
        {
            await _telemetryService.TrackAsync(TelemetryEvents.DeploySessionFinished, NetworkCacheTelemetry.DescribeBlockedStart(failure),
                CancellationToken.None).WaitAsync(BlockedStartTelemetryTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("The blocked start telemetry was not sent before Foundry Deploy closed.");
        }

        RunOnUi(_applicationShellService.Shutdown);
    }

    /// <summary>
    /// Shows the Foundry Connect page from a failure dialog and waits until Deploy is shown again. The return refresh does
    /// nothing meanwhile: the dialog's retry follows.
    /// </summary>
    private async Task OpenNetworkPageFromDialogAsync(CancellationToken cancellationToken)
    {
        Task<bool> open = Task.FromResult(false);
        RunOnUi(() =>
        {
            _isNetworkPageOpenFromDialog = true;
            open = _networkPageNavigator!.OpenAsync(cancellationToken);
        });
        try
        {
            // The navigator reports its own outcome and never throws for a page that failed or was cancelled.
            await open.ConfigureAwait(false);
        }
        finally
        {
            RunOnUi(() => _isNetworkPageOpenFromDialog = false);
        }
    }

    private void SetRefreshingAfterNetworkReturn(bool value)
    {
        _isRefreshingAfterNetworkReturn = value;
        OpenNetworkCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }
}
