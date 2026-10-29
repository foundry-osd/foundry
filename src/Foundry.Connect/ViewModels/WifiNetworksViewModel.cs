// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Network;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// The Wi-Fi networks card: a selection-only list and a details panel with the password outside the list.
/// </summary>
/// <remarks>
/// <para>
/// The list keeps its order while a network is selected: rows are reused by SSID bytes and the collection is
/// synchronized in place, so the selected instance, the password being typed and the keyboard position survive the
/// periodic observations. Without a selection the list follows the <c>WifiNetworkListPolicy</c> order. Programmatic
/// updates never count as networking interactions; selecting another network, typing a password and every
/// connect or disconnect do.
/// </para>
/// <para>
/// The password lives in an owned <see cref="char"/> buffer that is zeroed whenever it is replaced, cleared or disposed.
/// It is never trimmed. The coordinator takes a <see cref="string"/>, so each connect call makes one immutable copy
/// that cannot be zeroed and stays in memory until the garbage collector reclaims it. The view model never logs: SSIDs
/// and passwords must stay out of every log. Every member runs on the UI thread; a command that finishes after
/// <see cref="Dispose"/> changes nothing, and a connect that finishes after the selection changed leaves the new
/// selection's password and feedback alone.
/// </para>
/// </remarks>
public sealed partial class WifiNetworksViewModel : LocalizedViewModelBase
{
    private readonly ConnectNetworkActions _actions;
    private readonly INetworkMonitor _monitor;
    private readonly CancellationTokenSource _disposeCts = new();
    private WifiAvailability _availability = WifiAvailability.Disabled;
    private NetworkOperationKind? _activeOperation;
    private char[] _passphrase = [];
    private string? _feedbackKey;
    private bool _isSyncing;
    private bool _isCommandRunning;
    private bool _isDisposed;

    /// <summary>
    /// Gets or sets the network shown in the details panel. A change made by the technician clears the password and the
    /// feedback and reports a networking interaction; list synchronization never changes it.
    /// </summary>
    [ObservableProperty]
    private WifiNetworkItemViewModel? selectedNetwork;

    /// <summary>
    /// Initializes an empty, hidden list.
    /// </summary>
    /// <param name="localization">Source of the texts.</param>
    /// <param name="actions">The page's only path to the coordinator.</param>
    /// <param name="monitor">Monitor asked for an early observation by <see cref="RefreshCommand"/>.</param>
    public WifiNetworksViewModel(ILocalizationService localization, ConnectNetworkActions actions, INetworkMonitor monitor)
        : base(localization)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    /// <summary>
    /// Raised when the password is cleared; the view empties the editor.
    /// </summary>
    public event EventHandler? PassphraseCleared;

    /// <summary>
    /// Gets the list rows in display order.
    /// </summary>
    public ObservableCollection<WifiNetworkItemViewModel> Networks { get; } = [];

    /// <summary>
    /// Gets whether the card is shown: Wi-Fi works on this machine.
    /// </summary>
    public bool IsVisible => _availability == WifiAvailability.Available;

    /// <summary>
    /// Gets whether the list has rows.
    /// </summary>
    public bool HasNetworks => Networks.Count > 0;

    /// <summary>
    /// Gets the text shown when no network is visible.
    /// </summary>
    public string EmptyText => LocalizationService.GetString("Wifi.NoNetworksVisible");

    /// <summary>
    /// Gets whether a network is selected.
    /// </summary>
    public bool HasSelection => SelectedNetwork is not null;

    /// <summary>
    /// Gets the hint shown in the details panel while nothing is selected.
    /// </summary>
    public string SelectionHintText => LocalizationService.GetString("Wifi.SelectNetworkHint");

    /// <summary>
    /// Gets whether the password editor is shown: the selected network needs one, is in range and is not connected.
    /// </summary>
    public bool ShowPassphrase => SelectedNetwork is { RequiresPassphrase: true, IsConnected: false, IsInRange: true };

    /// <summary>
    /// Gets whether the selected network cannot be joined from the list.
    /// </summary>
    public bool HasUnsupportedText => UnsupportedKey is not null;

    /// <summary>
    /// Gets why the selected network cannot be joined from the list, or an empty string.
    /// </summary>
    public string UnsupportedText => UnsupportedKey is { } key ? LocalizationService.GetString(key) : string.Empty;

    /// <summary>
    /// Gets whether a password has been typed.
    /// </summary>
    public bool HasPassphrase => _passphrase.Length > 0;

    /// <summary>
    /// Gets whether a connection to a listed network is in flight.
    /// </summary>
    public bool IsConnecting => _activeOperation == NetworkOperationKind.ConnectWifiNetwork;

    /// <summary>
    /// Gets the caption shown in the details panel while <see cref="IsConnecting"/>.
    /// </summary>
    public string ConnectingText => LocalizationService.GetString("Status.ConnectingTitle");

    /// <summary>
    /// Gets the result of the last connect or disconnect, or an empty string.
    /// </summary>
    public string FeedbackText => _feedbackKey is { } key ? LocalizationService.GetString(key) : string.Empty;

    /// <summary>
    /// Gets whether there is feedback to show.
    /// </summary>
    public bool HasFeedback => _feedbackKey is not null;

    /// <summary>
    /// Gets the name announced for the connect button, or an empty string.
    /// </summary>
    public string ConnectAutomationName => SelectedNetwork is { } network
        ? LocalizationService.Format("Wifi.ConnectSelectedAutomationFormat", network.DisplayName)
        : string.Empty;

    /// <summary>
    /// Gets the name announced for the disconnect button, or an empty string.
    /// </summary>
    public string DisconnectAutomationName => SelectedNetwork is { } network
        ? LocalizationService.Format("Wifi.DisconnectSelectedAutomationFormat", network.DisplayName)
        : string.Empty;

    private string? UnsupportedKey => SelectedNetwork switch
    {
        { IsSupported: false, IsEnterprise: true } => "Wifi.ProvisionedProfileRequired",
        { IsSupported: false } => "Wifi.ActionSelectedNotSupported",
        _ => null
    };

    private bool CanConnect => !_isCommandRunning
        && _activeOperation is null
        && SelectedNetwork is { IsSupported: true, IsInRange: true, IsConnected: false } network
        && (!network.RequiresPassphrase || HasPassphrase);

    private bool CanDisconnect => !_isCommandRunning && _activeOperation is null && SelectedNetwork is { IsConnected: true };

    /// <summary>
    /// Replaces the password with a copy of <paramref name="value"/>, as typed (never trimmed). The previous buffer is
    /// zeroed. A non-empty value reports a networking interaction.
    /// </summary>
    /// <param name="value">The password from the editor; the caller keeps ownership of its own buffer.</param>
    public void SetPassphrase(ReadOnlySpan<char> value)
    {
        if (_isDisposed)
        {
            return;
        }

        char[] previous = _passphrase;
        _passphrase = value.ToArray();
        Zero(previous);
        OnPropertyChanged(nameof(HasPassphrase));
        ConnectCommand.NotifyCanExecuteChanged();
        if (!value.IsEmpty)
        {
            _actions.NotifyInteraction();
        }
    }

    /// <summary>
    /// Selects the row the technician chose with the keyboard (Enter on the list) and reports a networking interaction,
    /// also when that row is already selected.
    /// </summary>
    /// <param name="network">The focused row.</param>
    public void ChooseNetwork(WifiNetworkItemViewModel network)
    {
        ArgumentNullException.ThrowIfNull(network);
        if (_isDisposed)
        {
            return;
        }

        if (ReferenceEquals(SelectedNetwork, network))
        {
            _actions.NotifyInteraction();
            return;
        }

        SelectedNetwork = network;
    }

    /// <summary>
    /// Applies the latest wireless state. While a network is selected the rows keep their positions, new networks are
    /// appended and the selected one stays even when it is no longer visible; otherwise the incoming order is used.
    /// When the selected network becomes connected, by this panel or by other means, the password is zeroed; it is kept
    /// when the network merely goes out of range.
    /// </summary>
    /// <param name="wifi">The wireless state read from the coordinator.</param>
    /// <param name="activeOperation">The operation in flight, or <see langword="null"/>.</param>
    public void Apply(WifiState wifi, NetworkOperationKind? activeOperation)
    {
        ArgumentNullException.ThrowIfNull(wifi);
        if (_isDisposed)
        {
            return;
        }

        _availability = wifi.Availability;
        _activeOperation = activeOperation;
        bool wasSelectionConnected = SelectedNetwork?.IsConnected == true;

        IReadOnlyList<WifiSsid> order = WifiListOrder.Merge(Networks.Select(static item => item.Ssid).ToList(), wifi.Networks, SelectedNetwork?.Ssid);
        _isSyncing = true;
        try
        {
            Synchronize(order, wifi);
        }
        finally
        {
            _isSyncing = false;
        }

        // A connected network needs no password any more; the editor is hidden for it.
        if (!wasSelectionConnected && SelectedNetwork is { IsConnected: true } && HasPassphrase)
        {
            ClearPassphrase();
        }

        RaiseAll();
    }

    /// <summary>
    /// Raises every text again after a language change; the stored state is unchanged.
    /// </summary>
    public void RefreshTexts()
    {
        foreach (WifiNetworkItemViewModel item in Networks)
        {
            item.RefreshTexts();
        }

        RaiseAll();
    }

    /// <summary>
    /// Zeroes the password and cancels commands still running. Idempotent.
    /// </summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Zero(_passphrase);
        _passphrase = [];
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        base.Dispose();
    }

    partial void OnSelectedNetworkChanged(WifiNetworkItemViewModel? oldValue, WifiNetworkItemViewModel? newValue)
    {
        if (!_isSyncing && !ReferenceEquals(oldValue, newValue))
        {
            ClearPassphrase();
            _actions.NotifyInteraction();
            _feedbackKey = null;
        }

        RaiseDetails();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (_isDisposed || SelectedNetwork is not { } network)
        {
            return;
        }

        CancellationToken token = _disposeCts.Token;
        SetCommandRunning(true);
        try
        {
            // The coordinator takes a string: an immutable copy that cannot be zeroed and lives until GC. Never trimmed.
            string? passphrase = network.RequiresPassphrase ? new string(_passphrase) : null;

            // No ConfigureAwait(false): the continuation changes bound properties on the UI thread.
            OperationOutcome outcome = await _actions.ConnectWifiNetworkAsync(network.Ssid, passphrase, token);

            // The technician may have picked another network meanwhile: its password and feedback are not this call's.
            if (_isDisposed || !ReferenceEquals(SelectedNetwork, network))
            {
                return;
            }

            if (outcome.Status == OperationStatus.Succeeded)
            {
                ClearPassphrase();
            }

            SetFeedback(NetworkTextKeys.OutcomeKey(outcome));
        }
        finally
        {
            if (!_isDisposed)
            {
                SetCommandRunning(false);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        CancellationToken token = _disposeCts.Token;
        SetCommandRunning(true);
        try
        {
            OperationOutcome outcome = await _actions.DisconnectWifiAsync(token);
            if (_isDisposed)
            {
                return;
            }

            SetFeedback(NetworkTextKeys.OutcomeKey(outcome, provisionedPanel: false));
        }
        finally
        {
            if (!_isDisposed)
            {
                SetCommandRunning(false);
            }
        }
    }

    /// <summary>
    /// Asks the monitor for an early observation. Not a networking interaction.
    /// </summary>
    [RelayCommand]
    private void Refresh() => _monitor.RequestRefresh();

    private static void Zero(char[] buffer) => CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(buffer.AsSpan()));

    /// <summary>
    /// Brings <see cref="Networks"/> to <paramref name="order"/> in place, never clearing it, so the selected instance
    /// stays in the collection. Vanished rows are removed first: with a selection the remaining rows are then already in
    /// order and new rows are only appended, so the selected row is never moved.
    /// </summary>
    private void Synchronize(IReadOnlyList<WifiSsid> order, WifiState wifi)
    {
        Dictionary<WifiSsid, VisibleWifiNetwork> visible = [];
        foreach (VisibleWifiNetwork network in wifi.Networks)
        {
            visible.TryAdd(network.Ssid, network);
        }

        var wanted = new HashSet<WifiSsid>(order);
        for (int index = Networks.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(Networks[index].Ssid))
            {
                Networks.RemoveAt(index);
            }
        }

        Dictionary<WifiSsid, WifiNetworkItemViewModel> existing = Networks.ToDictionary(static item => item.Ssid);
        for (int position = 0; position < order.Count; position++)
        {
            WifiSsid ssid = order[position];
            if (existing.TryGetValue(ssid, out WifiNetworkItemViewModel? item))
            {
                int index = Networks.IndexOf(item);
                if (index != position)
                {
                    Networks.Move(index, position);
                }
            }
            else
            {
                item = new WifiNetworkItemViewModel(LocalizationService, ssid);
                Networks.Insert(position, item);
            }

            item.Update(visible.GetValueOrDefault(ssid), wifi.Connection?.Ssid.Equals(ssid) == true);
        }
    }

    private void ClearPassphrase()
    {
        Zero(_passphrase);
        _passphrase = [];
        OnPropertyChanged(nameof(HasPassphrase));
        PassphraseCleared?.Invoke(this, EventArgs.Empty);
    }

    private void SetFeedback(string? key)
    {
        _feedbackKey = key;
        OnPropertyChanged(nameof(FeedbackText));
        OnPropertyChanged(nameof(HasFeedback));
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

    private void RaiseDetails()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionHintText));
        OnPropertyChanged(nameof(ShowPassphrase));
        OnPropertyChanged(nameof(HasUnsupportedText));
        OnPropertyChanged(nameof(UnsupportedText));
        OnPropertyChanged(nameof(HasPassphrase));
        OnPropertyChanged(nameof(FeedbackText));
        OnPropertyChanged(nameof(HasFeedback));
        OnPropertyChanged(nameof(ConnectAutomationName));
        OnPropertyChanged(nameof(DisconnectAutomationName));
        NotifyCommands();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(HasNetworks));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(ConnectingText));
        RaiseDetails();
    }
}
