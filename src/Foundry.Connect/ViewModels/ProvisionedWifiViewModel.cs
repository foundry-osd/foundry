// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Core.Models.Configuration;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// The provisioned Wi-Fi card: the profile from the media configuration, its status, connect and disconnect, and the
/// D9 unavailable state with its reason, and the locked state whose Connect button asks for the deployment password (D11).
/// </summary>
/// <remarks>
/// Every text is derived from the typed <see cref="WifiState"/> and resolved on demand, so a language change only needs
/// <see cref="RefreshTexts"/> (UI-05). The security label comes from the configured security type; the WLAN template is
/// never read (UI-04, L3-15). Commands go through <see cref="ConnectNetworkActions"/>, which reports the interaction and
/// skips the coordinator under a debug scenario. The view model never logs: SSIDs and profile names must stay out of
/// every log. Every member runs on the UI thread; a command that finishes after <see cref="Dispose"/> changes nothing.
/// </remarks>
public sealed partial class ProvisionedWifiViewModel : LocalizedViewModelBase
{
    private readonly ConnectNetworkActions _actions;
    private readonly FoundryConnectConfigurationDocument _configuration;
    private readonly CancellationTokenSource _disposeCts = new();
    private WifiState _wifi = WifiState.Initial;
    private NetworkOperationKind? _activeOperation;
    private OperationOutcome? _lastOutcome;
    private string? _feedbackKey;
    private bool _isCommandRunning;
    private bool _isDisposed;

    /// <summary>
    /// Initializes the view model with no provisioned profile.
    /// </summary>
    /// <param name="localization">Source of the texts.</param>
    /// <param name="actions">The page's only path to the coordinator.</param>
    /// <param name="configuration">Media configuration; only the configured security type is read.</param>
    public ProvisionedWifiViewModel(ILocalizationService localization, ConnectNetworkActions actions, FoundryConnectConfigurationDocument configuration)
        : base(localization)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Gets whether the card is shown: a profile is configured and Wi-Fi works on this machine (L3-7).
    /// </summary>
    public bool IsVisible => _wifi.Provisioned.Status != ProvisionedWifiStatus.NotConfigured && _wifi.Availability == WifiAvailability.Available;

    /// <summary>
    /// Gets the profile name to show: the SSID, else the profile name, else a generic label.
    /// </summary>
    public string DisplayName => _wifi.Provisioned.Ssid?.DisplayName ?? _wifi.Provisioned.ProfileName ?? LocalizationService.GetString("Wifi.UnnamedProvisionedProfile");

    /// <summary>
    /// Gets the security label. An enterprise profile shows the generic enterprise label.
    /// </summary>
    public string SecurityText => LocalizationService.GetString(
        _wifi.Provisioned.IsEnterprise
            ? "Wifi.SecurityEnterprise"
            : NetworkTextKeys.ProfileSecurityKey(WifiSecurityClassifier.FromConfiguredSecurityType(_configuration.Wifi.SecurityType)));

    /// <summary>
    /// Gets the status line of the card.
    /// </summary>
    public string StatusText => LocalizationService.GetString(StatusKey);

    /// <summary>
    /// Gets whether the current Wi-Fi connection uses the provisioned profile.
    /// </summary>
    public bool IsConnected => _wifi.Connection?.Source == WifiConnectionSource.Provisioned;

    /// <summary>
    /// Gets whether a provisioned connection attempt is in flight.
    /// </summary>
    public bool IsConnecting => _activeOperation == NetworkOperationKind.ConnectProvisionedWifi;

    /// <summary>
    /// Gets whether the profile is configured but cannot be used (D9).
    /// </summary>
    public bool IsUnavailable => _wifi.Provisioned.Status == ProvisionedWifiStatus.Unavailable;

    /// <summary>
    /// Gets the explanation of an unavailable profile, or an empty string.
    /// </summary>
    public string UnavailableText => IsUnavailable ? LocalizationService.GetString(NetworkTextKeys.ProvisionedUnavailableKey(_wifi.Provisioned.Reason)) : string.Empty;

    /// <summary>
    /// Gets whether the profile needs the deployment password before it can be used; its Connect button prompts for it.
    /// </summary>
    public bool IsLocked => _wifi.Provisioned.Status == ProvisionedWifiStatus.Locked;

    /// <summary>
    /// Gets the explanation of a locked profile, which tells whether the password was already declined (L4-10), or an
    /// empty string.
    /// </summary>
    public string LockedText => IsLocked
        ? LocalizationService.GetString(_wifi.Provisioned.Reason == ProvisionedWifiUnavailableReason.UnlockCancelled
            ? "Wifi.ProvisionedUnlockCancelled"
            : "Wifi.ProvisionedLocked")
        : string.Empty;

    /// <summary>
    /// Gets the result of the last action or attempt, or an empty string.
    /// </summary>
    public string FeedbackText => _feedbackKey is { } key ? LocalizationService.GetString(key) : string.Empty;

    /// <summary>
    /// Gets whether there is feedback to show.
    /// </summary>
    public bool HasFeedback => _feedbackKey is not null;

    private string StatusKey
    {
        get
        {
            if (IsUnavailable)
            {
                return "Wifi.ProvisionedUnavailableTitle";
            }

            if (IsLocked)
            {
                return "Wifi.StatusLocked";
            }

            if (IsConnected)
            {
                return "Common.Connected";
            }

            if (IsConnecting)
            {
                return "Status.ConnectingTitle";
            }

            if (_wifi.Connection is not null)
            {
                return "Wifi.StatusAnotherNetworkActive";
            }

            // A hidden profile never appears in the scan list, and a profile without SSID counts as reachable.
            return !_wifi.Provisioned.IsHidden && !ConnectionPolicy.IsProvisionedNetworkVisible(_wifi)
                ? "Wifi.StatusNotInRange"
                : "Wifi.StatusReadyToConnect";
        }
    }

    private bool CanConnect => !_isCommandRunning
        && _activeOperation is null
        && _wifi.Provisioned.Status is ProvisionedWifiStatus.Ready or ProvisionedWifiStatus.Locked
        && _wifi.Availability == WifiAvailability.Available
        && !IsConnected;

    private bool CanDisconnect => !_isCommandRunning && _activeOperation is null && IsConnected;

    /// <summary>
    /// Applies the latest wireless state. An outcome that is a new instance and belongs to a provisioned connect
    /// replaces the feedback, which covers the attempts started by the coordinator itself. Becoming connected through
    /// the provisioned profile clears the feedback first.
    /// </summary>
    /// <param name="wifi">The wireless state read from the coordinator.</param>
    /// <param name="activeOperation">The operation in flight, or <see langword="null"/>.</param>
    /// <param name="lastOutcome">The outcome of the last finished operation, or <see langword="null"/>.</param>
    public void Apply(WifiState wifi, NetworkOperationKind? activeOperation, OperationOutcome? lastOutcome)
    {
        ArgumentNullException.ThrowIfNull(wifi);

        bool wasConnected = IsConnected;
        _wifi = wifi;
        _activeOperation = activeOperation;

        // Once the provisioned profile is connected, an earlier failure no longer describes the card.
        if (!wasConnected && IsConnected)
        {
            _feedbackKey = null;
        }

        // Outcomes are records that compare by value: two identical failures from two attempts must both count as new.
        if (lastOutcome is not null && !ReferenceEquals(lastOutcome, _lastOutcome))
        {
            _lastOutcome = lastOutcome;
            if (lastOutcome.Operation == NetworkOperationKind.ConnectProvisionedWifi)
            {
                _feedbackKey = NetworkTextKeys.OutcomeKey(lastOutcome, provisionedPanel: true);
            }
        }

        RaiseAll();
    }

    /// <summary>
    /// Shows a Wi-Fi problem met while applying the provisioning at startup.
    /// </summary>
    /// <remarks>
    /// <see cref="NetworkFailureCode.Unexpected"/> is not listed: a Wi-Fi part that throws ends the profile
    /// <see cref="ProvisionedWifiStatus.Unavailable"/>, so with a ready profile it comes from the wired part.
    /// </remarks>
    /// <param name="outcome">The provisioning outcome.</param>
    public void ApplyProvisioningOutcome(ProvisioningOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Wifi.Status == ProvisionedWifiStatus.Ready &&
            outcome.Failures.Any(static failure => failure is NetworkFailureCode.WifiCertificateImportFailed or NetworkFailureCode.WifiProfileRejected))
        {
            _feedbackKey = "Wifi.ActionApplyProvisionedFailed";
            OnPropertyChanged(nameof(FeedbackText));
            OnPropertyChanged(nameof(HasFeedback));
        }
    }

    /// <summary>
    /// Raises every text again after a language change; the stored state is unchanged.
    /// </summary>
    public void RefreshTexts() => RaiseAll();

    /// <summary>
    /// Cancels commands still running. Idempotent.
    /// </summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        base.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => RunAsync(_actions.ConnectProvisionedWifiAsync);

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync() => RunAsync(_actions.DisconnectWifiAsync);

    private async Task RunAsync(Func<CancellationToken, Task<OperationOutcome>> action)
    {
        if (_isDisposed)
        {
            return;
        }

        CancellationToken token = _disposeCts.Token;
        SetCommandRunning(true);
        try
        {
            // No ConfigureAwait(false): the continuation changes bound properties on the UI thread.
            OperationOutcome outcome = await action(token);
            if (_isDisposed)
            {
                return;
            }

            _feedbackKey = NetworkTextKeys.OutcomeKey(outcome, provisionedPanel: true);
            OnPropertyChanged(nameof(FeedbackText));
            OnPropertyChanged(nameof(HasFeedback));
        }
        finally
        {
            if (!_isDisposed)
            {
                SetCommandRunning(false);
            }
        }
    }

    private void SetCommandRunning(bool value)
    {
        _isCommandRunning = value;
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SecurityText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(UnavailableText));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(LockedText));
        OnPropertyChanged(nameof(FeedbackText));
        OnPropertyChanged(nameof(HasFeedback));
        NotifyCommands();
    }
}
