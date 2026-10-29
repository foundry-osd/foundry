// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network;

/// <summary>
/// Runs the periodic network observation and hands each result to the connection coordinator.
/// </summary>
public interface INetworkMonitor
{
    /// <summary>
    /// Observes immediately, then every refresh interval or when a refresh is requested, until cancelled.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop; cancellation ends the task without throwing.</param>
    /// <returns>A task that completes when the loop stopped.</returns>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asks for an immediate observation. Requests made while an observation runs coalesce into one more pass.
    /// </summary>
    void RequestRefresh();
}

/// <summary>
/// <see cref="INetworkMonitor"/> driven by a <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// The interval is measured from the end of a pass, so passes never overlap. A pass publishes only complete
/// observations: while the connectivity probe of a new pass is running, the coordinator keeps the internet status of the
/// previous pass, so <see cref="InternetStatus.Checking"/> appears only before the first result and never resets the
/// auto-continue countdown. A pass that fails is logged once per failure type and the loop continues; the first pass that
/// succeeds afterwards is logged as recovered. The loop runs on the thread pool, never on the thread that called
/// <see cref="RunAsync"/>, because the observation source reads the network adapters synchronously.
/// </remarks>
public sealed class NetworkMonitor(
    INetworkObservationSource source,
    IConnectionCoordinator coordinator,
    NetworkRefreshSignal refreshSignal,
    ConnectNetworkOptions options,
    TimeProvider timeProvider,
    ILogger<NetworkMonitor> logger) : INetworkMonitor
{
    private readonly INetworkObservationSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IConnectionCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly NetworkRefreshSignal _refreshSignal = refreshSignal ?? throw new ArgumentNullException(nameof(refreshSignal));
    private readonly ConnectNetworkOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<NetworkMonitor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Touched only by the single loop in RunAsync.
    private string? _lastFailureType;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Network monitoring started. RefreshIntervalSeconds={RefreshIntervalSeconds}",
            _options.RefreshInterval.TotalSeconds);

        try
        {
            // The first pass reads the Ethernet and WLAN adapters synchronously: the loop never runs on the caller's thread.
            await Task.Run(() => RunLoopAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal stop.
        }
        finally
        {
            _logger.LogInformation("Network monitoring stopped.");
        }
    }

    /// <inheritdoc />
    public void RequestRefresh() => _refreshSignal.Request();

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
            await WaitForNextPassAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            bool readWiredAuthentication = _coordinator.State.Ethernet.Dot1x.IsProfileApplied;
            NetworkObservation observation = await _source.ObserveAsync(readWiredAuthentication, cancellationToken).ConfigureAwait(false);
            await _coordinator.ApplyObservationAsync(observation, cancellationToken).ConfigureAwait(false);
            if (_lastFailureType is not null)
            {
                _logger.LogInformation("Network monitor pass recovered.");
                _lastFailureType = null;
            }
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Exception text can carry identifying data: the type is logged at Error, the exception itself at Debug only.
            string failureType = exception.GetType().Name;
            if (!string.Equals(failureType, _lastFailureType, StringComparison.Ordinal))
            {
                _logger.LogError("Network observation pass failed. FailureType={FailureType}", failureType);
                _logger.LogDebug(exception, "Network observation pass failure detail. FailureType={FailureType}", failureType);
                _lastFailureType = failureType;
            }
        }
    }

    // The linked source is cancelled once one wait won, so the losing wait never consumes a later request.
    private async Task WaitForNextPassAsync(CancellationToken cancellationToken)
    {
        using var iteration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task interval = Task.Delay(_options.RefreshInterval, _timeProvider, iteration.Token);
        Task requested = _refreshSignal.WaitAsync(iteration.Token);

        try
        {
            await Task.WhenAny(interval, requested).ConfigureAwait(false);
        }
        finally
        {
            await iteration.CancelAsync().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
