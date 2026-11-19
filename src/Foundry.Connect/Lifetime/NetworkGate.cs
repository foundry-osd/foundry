// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Security;
using Foundry.Connect.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Lifetime;

/// <summary>
/// The startup of Foundry Connect behind its status page: the wired 802.1X unlock prompt when needed, the provisioning, the
/// provisioned Wi-Fi feedback and the network monitor, each once per session, then the network gate of
/// <see cref="NetworkGatePolicy"/>: an online internet completes Connect through <see cref="ISuccessfulExitService"/>
/// (<c>connect_skipped</c>) without showing its page; otherwise the caller shows the page.
/// </summary>
/// <remarks>
/// <para>
/// The monitor starts right after the provisioning, before any probe: the provisioned Wi-Fi profile connects only through
/// the coordinator's automatic attempt, which the monitor drives, and its passes may raise the automatic Wi-Fi unlock
/// prompt that the already created page opens over the status page. The monitor stops at the first exit request of the
/// phase (skip, Continue, close, cancellation or failure) or when the gate is disposed with the phase's container, so it
/// runs once per session whether the page is shown or not.
/// </para>
/// <para>
/// The gate never skips when automatic continue is disabled (a debugger is attached) or while a deployment password prompt
/// is open (<see cref="IConnectUnlockService.IsPromptOpen"/>); its deadline does not move, so a prompt still open at the
/// deadline leaves the page shown behind it. A final probe of the successful exit that is not online keeps the gate
/// probing until its deadline. Call <see cref="RunAsync"/> on the UI thread: the wired prompt runs a nested modal loop
/// inside it, and the provisioned Wi-Fi feedback is a bound property.
/// </para>
/// <para>
/// A Connect page reopened from the deployment calls <see cref="StartForRevisitAsync"/> instead: no wired prompt, no probe
/// and no skip, only the provisioning (replayed when the first page ran) and the monitor. Either start runs once and logs
/// one entry: the gate outcome or the revisit start.
/// </para>
/// </remarks>
public sealed class NetworkGate : IDisposable
{
    private readonly ConnectNetworkActions _actions;
    private readonly IConnectionCoordinator _coordinator;
    private readonly INetworkMonitor _monitor;
    private readonly IConnectivityProbe _probe;
    private readonly IConnectUnlockService _unlock;
    private readonly ISuccessfulExitService _successfulExit;
    private readonly IApplicationLifetimeService _lifetime;
    private readonly ProvisionedWifiViewModel _provisionedWifi;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NetworkGate> _logger;
    private readonly bool _isAutoContinueEnabled;
    private readonly CancellationTokenSource _disposeCts = new();
    private CancellationTokenSource? _monitorStop;
    private Task? _monitorTask;
    private bool _hasRun;
    private bool _isDisposed;

    /// <summary>
    /// Initializes the gate.
    /// </summary>
    /// <param name="actions">Asks for the deployment password when wired 802.1X needs it at startup.</param>
    /// <param name="coordinator">Applies the provisioning.</param>
    /// <param name="monitor">The network monitor started after the provisioning.</param>
    /// <param name="probe">The internet probe of the gate.</param>
    /// <param name="unlock">Tells whether a deployment password prompt is open.</param>
    /// <param name="successfulExit">The single exit path, used to skip the page.</param>
    /// <param name="lifetime">Its exit request stops the gate and the monitor.</param>
    /// <param name="provisionedWifi">Receives the provisioning outcome for its feedback text.</param>
    /// <param name="timeProvider">Clock of the probe cadence and of the deadline.</param>
    /// <param name="logger">Logger for the gate outcome.</param>
    /// <param name="isAutoContinueEnabled">Whether the gate may skip the page; false while a debugger is attached.</param>
    public NetworkGate(
        ConnectNetworkActions actions,
        IConnectionCoordinator coordinator,
        INetworkMonitor monitor,
        IConnectivityProbe probe,
        IConnectUnlockService unlock,
        ISuccessfulExitService successfulExit,
        IApplicationLifetimeService lifetime,
        ProvisionedWifiViewModel provisionedWifi,
        TimeProvider timeProvider,
        ILogger<NetworkGate> logger,
        bool isAutoContinueEnabled)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _unlock = unlock ?? throw new ArgumentNullException(nameof(unlock));
        _successfulExit = successfulExit ?? throw new ArgumentNullException(nameof(successfulExit));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _provisionedWifi = provisionedWifi ?? throw new ArgumentNullException(nameof(provisionedWifi));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isAutoContinueEnabled = isAutoContinueEnabled;
    }

    /// <summary>
    /// Runs the startup provisioning, starts the monitor, then probes the internet until it skips Connect or decides to
    /// show its page. A cancelled wired prompt still provisions, with wired 802.1X unavailable for the session.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>
    /// <see cref="NetworkGateOutcome.SkipConnect"/> once the successful exit was requested, else
    /// <see cref="NetworkGateOutcome.ShowConnect"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">The gate already ran.</exception>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled, another exit was requested, or the gate was disposed before it ended.
    /// </exception>
    public async Task<NetworkGateOutcome> RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_hasRun)
        {
            throw new InvalidOperationException("The network gate runs once per session.");
        }

        _hasRun = true;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.ExitRequested, _disposeCts.Token);
        CancellationToken token = linked.Token;

        // No ConfigureAwait(false): the wired prompt and the provisioned Wi-Fi feedback belong to the UI thread.
        await _actions.UnlockWiredAtStartupAsync(token);
        token.ThrowIfCancellationRequested();
        ProvisioningOutcome provisioning = await _coordinator.ApplyProvisioningAsync(token);

        // The provisioning can complete after the phase ended; the monitor must then never start.
        token.ThrowIfCancellationRequested();
        _provisionedWifi.ApplyProvisioningOutcome(provisioning);
        StartMonitor();

        if (!_isAutoContinueEnabled)
        {
            LogOutcome(NetworkGateOutcome.ShowConnect, InternetStatus.Checking, TimeSpan.Zero);
            return NetworkGateOutcome.ShowConnect;
        }

        return await ProbeUntilDecidedAsync(token);
    }

    /// <summary>
    /// Starts a Connect page reopened from the deployment: replays <paramref name="previous"/> without provisioning (the
    /// provisioning reconnects every linked Ethernet interface, which would drop the operator's link), refreshes a locked
    /// provisioned Wi-Fi once the deployment key already unlocked its secrets, shows the provisioned Wi-Fi feedback of the
    /// resulting outcome and starts the monitor. Without a previous outcome it provisions once, as <see cref="RunAsync"/> does. It never asks for the wired
    /// password, never probes and never leaves Connect: the page always shows.
    /// </summary>
    /// <param name="previous">The provisioning outcome of the first Connect page, or <see langword="null"/> when it did not run.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task that completes once the monitor started.</returns>
    /// <exception cref="InvalidOperationException">The gate already ran or started.</exception>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled, an exit was requested, or the gate was disposed before the monitor started.
    /// </exception>
    public async Task StartForRevisitAsync(ProvisioningOutcome? previous, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_hasRun)
        {
            throw new InvalidOperationException("The network gate runs once per session.");
        }

        _hasRun = true;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.ExitRequested, _disposeCts.Token);
        CancellationToken token = linked.Token;
        token.ThrowIfCancellationRequested();

        // No ConfigureAwait(false): the provisioned Wi-Fi feedback belongs to the UI thread.
        ProvisioningOutcome provisioning;
        if (previous is null)
        {
            provisioning = await _coordinator.ApplyProvisioningAsync(token);
        }
        else
        {
            _coordinator.RestoreProvisioning(previous);
            provisioning = previous;

            // The refresh a successful prompt runs: only the Wi-Fi part, which never touches the Ethernet interfaces. Its merged
            // outcome, not the locked one, feeds the provisioned Wi-Fi feedback.
            if (previous.Wifi.Status == ProvisionedWifiStatus.Locked && !_unlock.IsWifiUnlockRequired)
            {
                await _coordinator.RefreshProvisionedWifiAsync(token);
                provisioning = _coordinator.Provisioning ?? previous;
            }
        }

        token.ThrowIfCancellationRequested();
        _provisionedWifi.ApplyProvisioningOutcome(provisioning);
        StartMonitor();
        _logger.LogInformation(
            "Network gate skipped for the Connect page reopened from the deployment. Provisioning={Provisioning}",
            previous is null ? "Applied" : "Replayed");
    }

    /// <summary>
    /// Stops the monitor without waiting for it and observes its task; cancels a running gate. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposeCts.Cancel();
        _monitorStop?.Cancel();

        // Never block the UI thread on the monitor; reading Exception keeps a faulted loop from going unobserved.
        _ = _monitorTask?.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
        _monitorStop?.Dispose();
        _disposeCts.Dispose();
    }

    private void StartMonitor()
    {
        _monitorStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ExitRequested, _disposeCts.Token);

        // Not awaited: the loop runs off the UI thread until the phase ends.
        _monitorTask = _monitor.RunAsync(_monitorStop.Token);
    }

    private async Task<NetworkGateOutcome> ProbeUntilDecidedAsync(CancellationToken cancellationToken)
    {
        long start = _timeProvider.GetTimestamp();
        using var deadline = new CancellationTokenSource(NetworkGatePolicy.Duration, _timeProvider);
        using CancellationTokenSource probing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        InternetStatus internet = InternetStatus.Checking;
        try
        {
            while (true)
            {
                TimeSpan probeStartedAt = _timeProvider.GetElapsedTime(start);
                internet = await CheckInternetAsync(probing.Token);
                NetworkGateOutcome? decision = NetworkGatePolicy.Evaluate(internet, _timeProvider.GetElapsedTime(start), _unlock.IsPromptOpen);
                if (decision == NetworkGateOutcome.SkipConnect && await TrySkipAsync(cancellationToken))
                {
                    return Decide(NetworkGateOutcome.SkipConnect, internet, start);
                }

                if (decision == NetworkGateOutcome.ShowConnect)
                {
                    return Decide(NetworkGateOutcome.ShowConnect, internet, start);
                }

                TimeSpan delay = NetworkGatePolicy.NextProbeDelay(probeStartedAt, _timeProvider.GetElapsedTime(start));
                await Task.Delay(delay, _timeProvider, probing.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Decide(NetworkGateOutcome.ShowConnect, internet, start);
        }
    }

    private async Task<InternetStatus> CheckInternetAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _probe.CheckAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug("Network gate internet check failed. FailureType={FailureType}", exception.GetType().Name);
            return InternetStatus.NoInternet;
        }
    }

    // True once the successful exit was requested; false when its final probe was not online or it failed, so the gate
    // keeps probing. Not bound to the deadline: a skip that started completes.
    private async Task<bool> TrySkipAsync(CancellationToken cancellationToken)
    {
        SuccessfulExitResult result;
        try
        {
            result = await _successfulExit.TryCompleteAsync(connectSkipped: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception is not attached: its message may name a network or a profile file.
            _logger.LogWarning("Skipping Foundry Connect failed; the network gate goes on. FailureType={FailureType}", exception.GetType().Name);
            return false;
        }

        return result switch
        {
            SuccessfulExitResult.Exited => true,
            SuccessfulExitResult.NotOnline => false,

            // Another exit won: the phase ends with it.
            _ => throw new OperationCanceledException(_lifetime.ExitRequested)
        };
    }

    private NetworkGateOutcome Decide(NetworkGateOutcome outcome, InternetStatus internet, long start)
    {
        LogOutcome(outcome, internet, _timeProvider.GetElapsedTime(start));
        return outcome;
    }

    private void LogOutcome(NetworkGateOutcome outcome, InternetStatus internet, TimeSpan elapsed) =>
        _logger.LogInformation(
            "Network gate ended. Outcome={Outcome}, Internet={Internet}, ElapsedSeconds={ElapsedSeconds}, AutoContinueEnabled={AutoContinueEnabled}",
            outcome,
            internet,
            Math.Round(elapsed.TotalSeconds, 1),
            _isAutoContinueEnabled);
}
