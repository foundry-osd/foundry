// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Core.Models.Configuration;
using Foundry.Wpf.Controls;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// The provisioned Wi-Fi card: the profile from the media configuration, a status pill, one message (the unavailable
/// reason, the locked text or the last feedback) and one action button that connects or disconnects. A locked
/// profile's Connect asks for the deployment password.
/// </summary>
/// <remarks>
/// Every text is derived from the typed <see cref="WifiState"/> and resolved on demand, so a language change only needs
/// <see cref="RefreshTexts"/>. The security label comes from the configured security type; the WLAN template is
/// never read. Commands go through <see cref="ConnectNetworkActions"/>, which reports the interaction and
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
    /// Raised on the UI thread once each time a command run by this view model ends (never after <see cref="Dispose"/>),
    /// so the view can return the keyboard focus to the card's action button when WPF lost it.
    /// </summary>
    public event EventHandler? ActionFocusRequested;

    /// <summary>
    /// Gets whether the card is shown: a profile is configured and Wi-Fi works on this machine.
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
    /// Gets the result of the last action or attempt, or an empty string.
    /// </summary>
    public string FeedbackText => _feedbackKey is { } key ? LocalizationService.GetString(key) : string.Empty;

    /// <summary>
    /// Gets whether there is feedback to show.
    /// </summary>
    public bool HasFeedback => _feedbackKey is not null;

    /// <summary>
    /// Gets the card's single message, in order: the unavailable reason, else the locked text (which tells whether the
    /// password was already declined), else the last feedback, else an empty string. Feedback is hidden while the
    /// profile is unavailable or locked.
    /// </summary>
    public string MessageText => IsUnavailable ? UnavailableText : IsLocked ? LockedText : FeedbackText;

    /// <summary>
    /// Gets the severity glyph of <see cref="MessageText"/>: warning for the unavailable reason, informational for the
    /// locked text, error for a feedback.
    /// </summary>
    public InfoBarSeverity MessageSeverity => IsUnavailable ? InfoBarSeverity.Warning : IsLocked ? InfoBarSeverity.Informational : InfoBarSeverity.Error;

    /// <summary>
    /// Gets whether the card shows a message.
    /// </summary>
    public bool HasMessage => MessageText.Length > 0;

    /// <summary>
    /// Gets whether the action button is shown: hidden while the profile is unavailable.
    /// </summary>
    public bool ShowPrimaryAction => !IsUnavailable;

    /// <summary>
    /// Gets the label of the action button: Disconnect when the provisioned profile is connected, else Connect.
    /// </summary>
    public string PrimaryActionText => LocalizationService.GetString(IsConnected ? "Action.Disconnect" : "Action.Connect");

    /// <summary>
    /// Gets the automation name of the action button.
    /// </summary>
    public string PrimaryActionAutomationName => LocalizationService.GetString(
        IsConnected ? "Wifi.DisconnectProvisionedAutomationName" : "Wifi.ConnectProvisionedAutomationName");

    private bool IsConnected => _wifi.Connection?.Source == WifiConnectionSource.Provisioned;

    private bool IsConnecting => _activeOperation == NetworkOperationKind.ConnectProvisionedWifi;

    private bool IsUnavailable => _wifi.Provisioned.Status == ProvisionedWifiStatus.Unavailable;

    // A locked profile needs the deployment password; its Connect button prompts for it.
    private bool IsLocked => _wifi.Provisioned.Status == ProvisionedWifiStatus.Locked;

    private string UnavailableText => LocalizationService.GetString(NetworkTextKeys.ProvisionedUnavailableKey(_wifi.Provisioned.Reason));

    private string LockedText => LocalizationService.GetString(_wifi.Provisioned.Reason == ProvisionedWifiUnavailableReason.UnlockCancelled
        ? "Wifi.ProvisionedUnlockCancelled"
        : "Wifi.ProvisionedLocked");

    private string StatusKey
    {
        get
        {
            if (IsUnavailable)
            {
                return "Common.Unavailable";
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

    private bool CanRunPrimaryAction => IsConnected ? CanDisconnect : CanConnect;

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
            RaiseFeedback();
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

    // One command for the card's single button, so the button keeps its command while it turns from Connect to Disconnect.
    [RelayCommand(CanExecute = nameof(CanRunPrimaryAction))]
    private Task PrimaryActionAsync() => IsConnected ? DisconnectAsync() : ConnectAsync();

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
            RaiseFeedback();
        }
        finally
        {
            if (!_isDisposed)
            {
                SetCommandRunning(false);
                ActionFocusRequested?.Invoke(this, EventArgs.Empty);
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
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private void RaiseFeedback()
    {
        OnPropertyChanged(nameof(FeedbackText));
        OnPropertyChanged(nameof(HasFeedback));
        OnPropertyChanged(nameof(MessageText));
        OnPropertyChanged(nameof(MessageSeverity));
        OnPropertyChanged(nameof(HasMessage));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SecurityText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowPrimaryAction));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(PrimaryActionAutomationName));
        RaiseFeedback();
        NotifyCommands();
    }
}
