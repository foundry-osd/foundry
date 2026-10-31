// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Configuration;
using Foundry.Connect.Network;
using Foundry.Connect.Telemetry;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Runtime;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Lifetime;

/// <summary>
/// Outcome of <see cref="ISuccessfulExitService.TryCompleteAsync"/>.
/// </summary>
public enum SuccessfulExitResult
{
    /// <summary>The successful exit was requested from the lifetime service.</summary>
    Exited,

    /// <summary>The final probe did not confirm internet access; Connect stays open.</summary>
    NotOnline,

    /// <summary>Another exit was already requested, or won the race.</summary>
    AlreadyExiting
}

/// <summary>
/// The single exit path of Continue and auto-continue.
/// </summary>
public interface ISuccessfulExitService
{
    /// <summary>
    /// Confirms internet access with a final probe, then captures roaming profiles, sends the session-ready event, clears
    /// the secrets and requests the successful exit.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation; cancelling it abandons the exit and throws.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled before the exit was requested.</exception>
    Task<SuccessfulExitResult> TryCompleteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Leaves Foundry Connect once, after a final probe has confirmed the internet.
/// </summary>
/// <remarks>
/// Order matters: roaming is captured while the profiles still exist, the session-ready event waits at most
/// <see cref="TelemetryBudget"/> so a slow network cannot hold the exit, the secrets and the in-memory
/// provisioned profile are dropped, the theme and language are handed to Foundry Deploy and only then the exit
/// is requested. Roaming, telemetry and hand-off failures never block the exit. Log entries carry no SSID, profile name or
/// address.
/// </remarks>
public sealed class SuccessfulExitService : ISuccessfulExitService
{
    /// <summary>
    /// Longest time the exit waits for the session-ready event.
    /// </summary>
    public static readonly TimeSpan TelemetryBudget = TimeSpan.FromSeconds(2);

    private readonly IConnectivityProbe _probe;
    private readonly IConnectionCoordinator _coordinator;
    private readonly IRoamingCapture _roaming;
    private readonly ITelemetryService _telemetry;
    private readonly FoundryConnectConfigurationDocument _configuration;
    private readonly ConnectRuntimeSecrets _secrets;
    private readonly IApplicationLifetimeService _lifetime;
    private readonly NetworkRefreshSignal _refreshSignal;
    private readonly IRuntimePreferencesPublisher _preferences;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SuccessfulExitService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes the service.
    /// </summary>
    /// <param name="probe">Connectivity probe for the final check.</param>
    /// <param name="coordinator">Source of the last network state; releases the provisioned profile.</param>
    /// <param name="roaming">Roaming profile capture.</param>
    /// <param name="telemetry">Telemetry sink.</param>
    /// <param name="configuration">Runtime configuration.</param>
    /// <param name="secrets">Runtime secrets, cleared before the exit.</param>
    /// <param name="lifetime">Application lifetime service that performs the exit.</param>
    /// <param name="refreshSignal">Asks the network monitor for an immediate refresh when the final probe fails.</param>
    /// <param name="preferences">Hands the theme and language to Foundry Deploy just before the exit.</param>
    /// <param name="timeProvider">Clock that drives the telemetry budget.</param>
    /// <param name="logger">Logger.</param>
    public SuccessfulExitService(
        IConnectivityProbe probe,
        IConnectionCoordinator coordinator,
        IRoamingCapture roaming,
        ITelemetryService telemetry,
        FoundryConnectConfigurationDocument configuration,
        ConnectRuntimeSecrets secrets,
        IApplicationLifetimeService lifetime,
        NetworkRefreshSignal refreshSignal,
        IRuntimePreferencesPublisher preferences,
        TimeProvider timeProvider,
        ILogger<SuccessfulExitService> logger)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _roaming = roaming ?? throw new ArgumentNullException(nameof(roaming));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SuccessfulExitResult> TryCompleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lifetime.RequestedExitCode is not null)
            {
                return SuccessfulExitResult.AlreadyExiting;
            }

            InternetStatus internet = await CheckInternetAsync(cancellationToken).ConfigureAwait(false);
            if (internet != InternetStatus.Online)
            {
                _refreshSignal.Request();
                _logger.LogInformation("Final internet check did not confirm access; staying. Internet={Internet}", internet);
                return SuccessfulExitResult.NotOnline;
            }

            NetworkState state = _coordinator.State with { Internet = InternetStatus.Online };
            await CaptureRoamingAsync(state, cancellationToken).ConfigureAwait(false);
            await TrackSessionReadyAsync(state, cancellationToken).ConfigureAwait(false);

            _coordinator.ReleaseProvisionedProfile();
            _secrets.ClearAll();
            PublishPreferences();

            _logger.LogInformation(
                "Network ready; leaving Foundry Connect. ConnectionType={ConnectionType}",
                ConnectSessionReadyTelemetry.ConnectionType(state.ActiveInternetPath));
            return _lifetime.TryExit(ChildRuntimeExitCode.Success) ? SuccessfulExitResult.Exited : SuccessfulExitResult.AlreadyExiting;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<InternetStatus> CheckInternetAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _probe.CheckAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug("Final internet check failed. FailureType={FailureType}", exception.GetType().Name);
            return InternetStatus.NoInternet;
        }
    }

    private void PublishPreferences()
    {
        try
        {
            _preferences.Publish();
        }
        catch (Exception exception)
        {
            // The publisher already swallows IO errors; this guard keeps the exit unconditional.
            _logger.LogWarning("Theme and language hand-off failed. FailureType={FailureType}", exception.GetType().Name);
        }
    }

    private async Task CaptureRoamingAsync(NetworkState state, CancellationToken cancellationToken)
    {
        try
        {
            await _roaming.CaptureAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The exception is not attached: its message could name a profile file.
            _logger.LogWarning("Network profile roaming capture failed before exit. FailureType={FailureType}", exception.GetType().Name);
        }
    }

    private async Task TrackSessionReadyAsync(NetworkState state, CancellationToken cancellationToken)
    {
        Task? tracking = null;
        try
        {
            using var budget = new CancellationTokenSource(TelemetryBudget, _timeProvider);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);
            tracking = _telemetry.TrackAsync(TelemetryEvents.ConnectSessionReady, ConnectSessionReadyTelemetry.Build(state, _configuration), linked.Token);
            await tracking.WaitAsync(TelemetryBudget, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The send is abandoned, not awaited: observe a later fault so it never surfaces as an unobserved exception.
            _ = tracking?.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _logger.LogInformation(
                "Session-ready telemetry did not complete within the exit budget. BudgetSeconds={BudgetSeconds}",
                TelemetryBudget.TotalSeconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug("Session-ready telemetry failed. FailureType={FailureType}", exception.GetType().Name);
        }
    }
}
