// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network;

/// <summary>
/// Writes the network operation and state-transition log entries (D17, NET-07/08).
/// </summary>
/// <remarks>
/// The PostHog Logs stream mirrors every message and every structured property, so no entry carries an SSID, a WLAN
/// profile name, an IP address or profile XML; only typed, non-identifying facts are logged. Never pass a state object
/// to a template: generated record members would print names and addresses.
/// </remarks>
/// <param name="logger">Destination logger.</param>
/// <param name="timeProvider">Clock that measures <c>DurationMs</c>.</param>
public sealed class NetworkOperationLogger(ILogger logger, TimeProvider timeProvider)
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Opens the logging scope of an operation and writes its start entry.
    /// </summary>
    /// <param name="operation">Operation kind.</param>
    /// <param name="trigger">What started the operation.</param>
    /// <returns>The scope; dispose it after the outcome is logged.</returns>
    public NetworkOperationScope Begin(NetworkOperationKind operation, NetworkOperationTrigger trigger)
    {
        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string operationName = NetworkFailureTaxonomy.OperationName(operation);
        IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Workflow"] = "connect",
            ["OperationId"] = operationId,
            ["NetworkOperation"] = operationName
        });

        _logger.LogInformation(
            "Network operation started. NetworkOperation={NetworkOperation}, Trigger={Trigger}",
            operationName,
            TriggerName(trigger));

        return new NetworkOperationScope(_logger, _timeProvider, operationId, operationName, scope);
    }

    /// <summary>
    /// Logs the state when its summary changed. Network lists, operation data and observation times never count as a
    /// change, so polling does not log.
    /// </summary>
    /// <param name="previous">State before the change, or <see langword="null"/> for the first state.</param>
    /// <param name="current">New state.</param>
    public void LogTransition(NetworkState? previous, NetworkState current)
    {
        ArgumentNullException.ThrowIfNull(current);

        StateSummary summary = StateSummary.From(current);
        if (previous is not null && StateSummary.From(previous) == summary)
        {
            return;
        }

        _logger.LogInformation(
            "Network state changed. Internet={Internet}, EthernetLink={EthernetLink}, EthernetAddress={EthernetAddress}, Dot1x={Dot1x}, WifiAvailability={WifiAvailability}, WifiConnected={WifiConnected}, WifiSource={WifiSource}, ConnectionMode={ConnectionMode}, ProvisionedWifi={ProvisionedWifi}",
            summary.Internet,
            summary.EthernetLink,
            summary.EthernetAddress,
            summary.Dot1x,
            summary.WifiAvailability,
            summary.WifiSsid is not null,
            summary.WifiSource,
            summary.Mode,
            summary.ProvisionedWifi);
    }

    private static string TriggerName(NetworkOperationTrigger trigger)
    {
        return trigger switch
        {
            NetworkOperationTrigger.Manual => "manual",
            NetworkOperationTrigger.Automatic => "automatic",
            NetworkOperationTrigger.Startup => "startup",
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "The trigger has no log name.")
        };
    }

    // The SSID takes part in the comparison so a roam to another network is a transition, but it is never logged.
    private readonly record struct StateSummary(
        InternetStatus Internet,
        bool EthernetLink,
        bool EthernetAddress,
        Dot1xAuthenticationStatus Dot1x,
        WifiAvailability WifiAvailability,
        WifiSsid? WifiSsid,
        WifiConnectionSource? WifiSource,
        ConnectionMode Mode,
        ProvisionedWifiStatus ProvisionedWifi)
    {
        public static StateSummary From(NetworkState state) => new(
            state.Internet,
            state.Ethernet.HasLink,
            state.Ethernet.HasUsableAddress,
            state.Ethernet.Dot1x.Status,
            state.Wifi.Availability,
            state.Wifi.Connection?.Ssid,
            state.Wifi.Connection?.Source,
            state.Mode,
            state.Wifi.Provisioned.Status);
    }
}

/// <summary>
/// Logging scope of one network operation, carrying <c>Workflow</c>, <c>OperationId</c> and <c>NetworkOperation</c>.
/// </summary>
public sealed class NetworkOperationScope : IDisposable
{
    private const string FailureTemplate =
        "Network operation failed. Workflow={Workflow}, OperationId={OperationId}, NetworkOperation={NetworkOperation}, FailureKind={FailureKind}, FailureReason={FailureReason}, FailureCode={FailureCode}, WlanReasonCode={WlanReasonCode}";

    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string _operationName;
    private readonly IDisposable? _scope;
    private readonly long _startTimestamp;
    private bool _failureLogged;

    internal NetworkOperationScope(ILogger logger, TimeProvider timeProvider, string operationId, string operationName, IDisposable? scope)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        OperationId = operationId;
        _operationName = operationName;
        _scope = scope;
        _startTimestamp = timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Gets the operation identifier (a GUID in <c>N</c> format).
    /// </summary>
    public string OperationId { get; }

    /// <summary>
    /// Writes the terminal entry at Information. A failed outcome also writes its Warning unless a failure or an
    /// exception was already logged in this scope, so each failure is reported once.
    /// </summary>
    /// <param name="outcome">Terminal outcome.</param>
    public void LogOutcome(OperationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Status == OperationStatus.Failed && !_failureLogged)
        {
            LogFailure(outcome.Failure, outcome.WlanReasonCode);
        }

        _logger.LogInformation(
            "Network operation finished. NetworkOperation={NetworkOperation}, Status={Status}, Failure={Failure}, DurationMs={DurationMs}",
            _operationName,
            outcome.Status,
            outcome.Failure,
            (long)Math.Round(_timeProvider.GetElapsedTime(_startTimestamp).TotalMilliseconds));
    }

    /// <summary>
    /// Writes one Warning with the existing remote failure taxonomy.
    /// </summary>
    /// <param name="failure">Failure code; not <see cref="NetworkFailureCode.None"/>.</param>
    /// <param name="wlanReasonCode">Native WLAN reason code, when known.</param>
    public void LogFailure(NetworkFailureCode failure, uint? wlanReasonCode)
    {
        NetworkFailureDescriptor descriptor = NetworkFailureTaxonomy.Describe(failure);
        _failureLogged = true;
        _logger.Log(LogLevel.Warning, default, null, FailureTemplate,
            "connect", OperationId, _operationName, descriptor.Kind, descriptor.Reason, descriptor.Code, wlanReasonCode);
    }

    /// <summary>
    /// Records that a component inside the operation already logged its failure Warning, so
    /// <see cref="LogOutcome"/> adds none.
    /// </summary>
    public void MarkFailureLogged()
    {
        _failureLogged = true;
    }

    /// <summary>
    /// Writes one Error for an unexpected exception, classified with <see cref="NetworkFailureTaxonomy.ClassifyException"/>.
    /// </summary>
    /// <param name="exception">The exception.</param>
    public void LogException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        NetworkFailureDescriptor descriptor = NetworkFailureTaxonomy.ClassifyException(exception);
        _failureLogged = true;
        _logger.Log(LogLevel.Error, default, exception, FailureTemplate,
            "connect", OperationId, _operationName, descriptor.Kind, descriptor.Reason, descriptor.Code, null);
    }

    /// <summary>
    /// Ends the logging scope.
    /// </summary>
    public void Dispose()
    {
        _scope?.Dispose();
    }
}
