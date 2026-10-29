// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Network;
using Foundry.Localization;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// The single Connect page: renders the live or simulated <see cref="NetworkState"/> into the hero, the connection chip,
/// the notices and the five child view models, requests the focus on page status changes, switches debug scenarios,
/// and owns the startup sequence (provisioning, then the network monitor) and its shutdown.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IConnectionCoordinator.StateChanged"/> arrives on any thread and its argument may be stale, so every
/// notification is marshaled through <see cref="IUiDispatcher"/> and renders <see cref="IConnectionCoordinator.State"/>
/// read again on the UI thread. Every text is resolved from the typed state on demand, so a language change renders
/// the same state again and refreshes the children's texts without a network refresh or a focus move (UI-05).
/// </para>
/// <para>
/// While a debug scenario is active the live state is still recorded but not rendered, and every network action, the
/// final probe and the exit are skipped by <see cref="ConnectNetworkActions"/> and <see cref="AutoContinueViewModel"/>
/// (D16). The view model logs only the applied scenario; it never logs a network state. The children are DI singletons
/// disposed by the container, not by <see cref="Dispose"/>.
/// </para>
/// </remarks>
public sealed partial class ConnectPageViewModel : LocalizedViewModelBase
{
    private readonly IConnectionCoordinator _coordinator;
    private readonly INetworkMonitor _monitor;
    private readonly ConnectNetworkActions _actions;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConnectPageViewModel> _logger;
    private readonly CancellationTokenSource _disposeCts = new();
    private NetworkState _renderedState;
    private NetworkState? _liveState;
    private ConnectPageStatus? _lastStatus = ConnectPageStatus.Checking;
    private bool _hasInteraction;
    private bool _isInitialized;
    private bool _isDisposed;
    private Task? _monitorTask;

    /// <summary>
    /// Subscribes to the coordinator, the networking interactions and the language changes, then renders the current
    /// coordinator state without requesting the focus.
    /// </summary>
    /// <param name="localization">Source of the texts and language change events.</param>
    /// <param name="coordinator">Owner of the network state; also applies the provisioning at startup.</param>
    /// <param name="monitor">Periodic observer started by <see cref="InitializeAsync"/>; refreshed by <see cref="RefreshCommand"/>.</param>
    /// <param name="actions">The page's only path to the coordinator for technician actions.</param>
    /// <param name="autoContinue">The countdown and Continue.</param>
    /// <param name="ethernet">The Ethernet card.</param>
    /// <param name="provisionedWifi">The provisioned Wi-Fi card.</param>
    /// <param name="wifiNetworks">The Wi-Fi networks card.</param>
    /// <param name="diagnostics">The Tools menu commands.</param>
    /// <param name="dispatcher">Marshals coordinator and language notifications to the UI thread.</param>
    /// <param name="timeProvider">Clock of the simulated states.</param>
    /// <param name="logger">Logger for applied debug scenarios.</param>
    public ConnectPageViewModel(
        ILocalizationService localization,
        IConnectionCoordinator coordinator,
        INetworkMonitor monitor,
        ConnectNetworkActions actions,
        AutoContinueViewModel autoContinue,
        EthernetSectionViewModel ethernet,
        ProvisionedWifiViewModel provisionedWifi,
        WifiNetworksViewModel wifiNetworks,
        DiagnosticsViewModel diagnostics,
        IUiDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<ConnectPageViewModel> logger)
        : base(localization)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        AutoContinue = autoContinue ?? throw new ArgumentNullException(nameof(autoContinue));
        Ethernet = ethernet ?? throw new ArgumentNullException(nameof(ethernet));
        ProvisionedWifi = provisionedWifi ?? throw new ArgumentNullException(nameof(provisionedWifi));
        WifiNetworks = wifiNetworks ?? throw new ArgumentNullException(nameof(wifiNetworks));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _coordinator.StateChanged += OnCoordinatorStateChanged;
        _actions.InteractionOccurred += OnInteraction;
        AutoContinue.ContinueFocusRequested += OnContinueFocusRequested;
        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;

        _renderedState = _coordinator.State;
        Render(_renderedState);
    }

    /// <summary>
    /// Raised on the UI thread when the page status changes and a control should take the focus, and after "Stay here".
    /// </summary>
    public event EventHandler<ConnectFocusTarget>? FocusRequested;

    /// <summary>Gets the countdown and Continue.</summary>
    public AutoContinueViewModel AutoContinue { get; }

    /// <summary>Gets the Ethernet card.</summary>
    public EthernetSectionViewModel Ethernet { get; }

    /// <summary>Gets the provisioned Wi-Fi card.</summary>
    public ProvisionedWifiViewModel ProvisionedWifi { get; }

    /// <summary>Gets the Wi-Fi networks card.</summary>
    public WifiNetworksViewModel WifiNetworks { get; }

    /// <summary>Gets the Tools menu commands.</summary>
    public DiagnosticsViewModel Diagnostics { get; }

    /// <summary>Gets the page status of the rendered state.</summary>
    public ConnectPageStatus Status { get; private set; } = ConnectPageStatus.Checking;

    /// <summary>Gets why the page is blocked, or <see cref="BlockedReason.None"/>.</summary>
    public BlockedReason BlockedReason { get; private set; }

    /// <summary>Gets whether the internet is online.</summary>
    public bool IsReady => Status == ConnectPageStatus.Ready;

    /// <summary>Gets whether the internet is not reachable and nothing is running.</summary>
    public bool IsBlocked => Status == ConnectPageStatus.Blocked;

    /// <summary>Gets the hero title.</summary>
    public string HeroTitle => LocalizationService.GetString(HeroKeys.TitleKey);

    /// <summary>Gets the hero description.</summary>
    public string HeroDescription => LocalizationService.GetString(HeroKeys.DescriptionKey);

    /// <summary>Gets whether the hero shows the progress ring: checking or waiting.</summary>
    public bool IsHeroBusy => Status is ConnectPageStatus.Checking or ConnectPageStatus.Waiting;

    /// <summary>Gets whether the hero shows the terminal status glyph: ready or blocked.</summary>
    public bool IsHeroTerminal => !IsHeroBusy;

    /// <summary>Gets whether the terminal glyph shows success.</summary>
    public bool IsHeroSuccess => IsReady;

    /// <summary>
    /// Gets the connection chip: Ethernet when it has a usable address, else the Wi-Fi network in use, else empty.
    /// </summary>
    public string ConnectionChipText
    {
        get
        {
            if (_renderedState.Ethernet.HasUsableAddress)
            {
                return LocalizationService.GetString("Wifi.ConnectionChipEthernet");
            }

            return _renderedState.Wifi.Connection is { } connection
                ? LocalizationService.Format(
                    connection.Source == WifiConnectionSource.Provisioned ? "Wifi.ConnectionChipProvisionedFormat" : "Wifi.ConnectionChipFormat",
                    connection.Ssid.DisplayName)
                : string.Empty;
        }
    }

    /// <summary>Gets whether the connection chip is shown.</summary>
    public bool HasConnectionChip => ConnectionChipText.Length > 0;

    /// <summary>Gets whether the captive portal InfoBar is shown (L3-17).</summary>
    public bool ShowCaptivePortalNotice => BlockedReason == BlockedReason.CaptivePortal;

    /// <summary>
    /// Gets the one-line notice that replaces the Wi-Fi cards when the WLAN runtime or the adapter is missing, or an
    /// empty string (L3-7).
    /// </summary>
    public string WifiNoticeText => NetworkTextKeys.WifiNoticeKey(_renderedState.Wifi.Availability) is { } key ? LocalizationService.GetString(key) : string.Empty;

    /// <summary>Gets whether the Wi-Fi notice is shown.</summary>
    public bool HasWifiNotice => WifiNoticeText.Length > 0;

    /// <summary>Gets whether a debug scenario replaces the live state.</summary>
    public bool IsSimulation { get; private set; }

    /// <summary>Gets the active debug scenario; <see cref="ConnectDebugScenario.Live"/> for the real network.</summary>
    public ConnectDebugScenario ActiveDebugScenario { get; private set; }

    /// <summary>Gets the state rendered last, live or simulated.</summary>
    internal NetworkState CurrentState => _renderedState;

    private (string TitleKey, string DescriptionKey) HeroKeys =>
        NetworkTextKeys.HeroKeys(Status, BlockedReason, _renderedState.ActiveOperation);

    /// <summary>
    /// Applies the provisioning once, shows its Wi-Fi feedback, then starts the network monitor. Later calls do nothing.
    /// </summary>
    /// <remarks>
    /// The monitor is started with the page lifetime token, not <paramref name="cancellationToken"/>, and is not
    /// awaited: it runs its loop off the caller thread until <see cref="Dispose"/>. It never starts once the page is
    /// disposed.
    /// </remarks>
    /// <param name="cancellationToken">Caller cancellation for the provisioning step.</param>
    /// <returns>A task that completes once the monitor is started, or when the page is disposed or cancelled first.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed || _isInitialized)
        {
            return;
        }

        _isInitialized = true;

        // Read before the first await: Dispose disposes the source.
        CancellationToken lifetime = _disposeCts.Token;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
        ProvisioningOutcome outcome;
        try
        {
            // No ConfigureAwait(false): the continuation updates bound properties on the UI thread.
            outcome = await _coordinator.ApplyProvisioningAsync(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        if (_isDisposed)
        {
            return;
        }

        _dispatcher.Run(() => ProvisionedWifi.ApplyProvisioningOutcome(outcome));
        _monitorTask = _monitor.RunAsync(lifetime);
    }

    /// <summary>
    /// Replaces the rendered state with a simulated scenario, or returns to the live state with
    /// <see cref="ConnectDebugScenario.Live"/>. The focus logic and the interaction pause start over.
    /// </summary>
    /// <param name="scenario">The scenario to show.</param>
    public void ApplyDebugScenario(ConnectDebugScenario scenario)
    {
        if (_isDisposed)
        {
            return;
        }

        _logger.LogInformation("Debug scenario applied. Scenario={Scenario}", scenario);
        ActiveDebugScenario = scenario;
        IsSimulation = scenario != ConnectDebugScenario.Live;
        _actions.IsSimulation = IsSimulation;
        AutoContinue.IsSimulation = IsSimulation;
        _lastStatus = null;
        _hasInteraction = false;
        OnPropertyChanged(nameof(ActiveDebugScenario));
        OnPropertyChanged(nameof(IsSimulation));

        Render(IsSimulation ? DebugScenarioCatalog.Create(scenario, _timeProvider.GetUtcNow()) : _liveState ?? _coordinator.State);
    }

    /// <summary>
    /// Unsubscribes, cancels the network monitor and observes its task without waiting for it. Idempotent.
    /// </summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _coordinator.StateChanged -= OnCoordinatorStateChanged;
        _actions.InteractionOccurred -= OnInteraction;
        AutoContinue.ContinueFocusRequested -= OnContinueFocusRequested;
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        _disposeCts.Cancel();

        // Never block the UI thread on the monitor; reading Exception keeps a faulted loop from going unobserved.
        _ = _monitorTask?.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
        _disposeCts.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Asks the monitor for an early observation: the Retry button and Tools › Refresh status. Not a networking
    /// interaction (L3-4).
    /// </summary>
    [RelayCommand]
    private void Refresh() => _monitor.RequestRefresh();

    // The argument may be stale and arrives on any thread: render the coordinator's current state on the UI thread.
    private void OnCoordinatorStateChanged(object? sender, NetworkState ignored) =>
        _dispatcher.Run(() =>
        {
            if (!_isDisposed)
            {
                OnLiveState(_coordinator.State);
            }
        });

    private void OnLiveState(NetworkState state)
    {
        _liveState = state;
        if (!IsSimulation)
        {
            Render(state);
        }
    }

    private void OnInteraction(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _hasInteraction = true;
        AutoContinue.OnNetworkingInteraction();
    }

    // "Stay here" hides its own button; the focus goes to Continue, which stays visible while the internet is online.
    private void OnContinueFocusRequested(object? sender, EventArgs e)
    {
        if (!_isDisposed)
        {
            FocusRequested?.Invoke(this, ConnectFocusTarget.Continue);
        }
    }

    private void OnLocalizationLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e) => _dispatcher.Run(OnLanguageChanged);

    private void OnLanguageChanged()
    {
        if (_isDisposed)
        {
            return;
        }

        // Same state, same status: no focus request and no monitor call.
        Render(_renderedState);
        Ethernet.RefreshTexts();
        ProvisionedWifi.RefreshTexts();
        WifiNetworks.RefreshTexts();
        AutoContinue.RefreshTexts();
    }

    private void Render(NetworkState state)
    {
        _renderedState = state;
        ConnectPageStatus status = ConnectPagePolicy.ResolveStatus(state);
        Status = status;
        BlockedReason = ConnectPagePolicy.ResolveBlockedReason(state);

        Ethernet.Apply(state.Ethernet, status == ConnectPageStatus.Checking);
        ProvisionedWifi.Apply(state.Wifi, state.ActiveOperation, state.LastOutcome);
        WifiNetworks.Apply(state.Wifi, state.ActiveOperation);
        AutoContinue.OnInternetStatusChanged(state.Internet);
        RaisePageProperties();

        if (status == _lastStatus)
        {
            return;
        }

        _lastStatus = status;
        ConnectFocusTarget target = ConnectPagePolicy.ResolveFocus(
            status,
            _hasInteraction,
            ProvisionedWifi.IsVisible && ProvisionedWifi.ConnectCommand.CanExecute(null),
            WifiNetworks.IsVisible && WifiNetworks.HasNetworks);
        if (target != ConnectFocusTarget.None)
        {
            FocusRequested?.Invoke(this, target);
        }
    }

    private void RaisePageProperties()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(BlockedReason));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(HeroDescription));
        OnPropertyChanged(nameof(IsHeroBusy));
        OnPropertyChanged(nameof(IsHeroTerminal));
        OnPropertyChanged(nameof(IsHeroSuccess));
        OnPropertyChanged(nameof(ConnectionChipText));
        OnPropertyChanged(nameof(HasConnectionChip));
        OnPropertyChanged(nameof(ShowCaptivePortalNotice));
        OnPropertyChanged(nameof(WifiNoticeText));
        OnPropertyChanged(nameof(HasWifiNotice));
    }
}
