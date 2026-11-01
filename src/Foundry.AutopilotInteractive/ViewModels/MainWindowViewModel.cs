// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.Input;
using Foundry.AutopilotInteractive.Services;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.Device;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Services.Simulation;
using Foundry.Core.Models.Autopilot;
using Foundry.Localization;
using Foundry.Utilities.Globalization;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Runtime;
using Foundry.Wpf.Shell;
using Foundry.Wpf.Theme;
using Foundry.Wpf.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.ViewModels;

/// <summary>The page shown in the window.</summary>
public enum AutopilotPage
{
    /// <summary>Page 1: device code sign-in.</summary>
    SignIn,

    /// <summary>Page 2: group tag and registration.</summary>
    GroupTag
}

/// <summary>
/// The window shell and the run state machine: menu (theme, language, keyboard, debug, about), version, device footer, current
/// page, result file, deadline and exit code.
/// </summary>
/// <remarks>
/// <para>
/// The run goes sign-in, page loading (still on page 1), page 2, registration, profile wait, success countdown. A Graph 401
/// or Use another account returns to page 1 with a new code. The shell writes <c>result.json</c>: <c>completed</c> on success,
/// <c>failed</c> on a registration failure (the window stays open), <c>skipped</c> on a confirmed close, and
/// <c>failed</c>/<c>deadline_reached</c> when the deadline arrives before the registration.
/// </para>
/// <para>
/// <see cref="ExitCode"/> is 1 until the registration completes, then 0. When the run decides to end (countdown, Continue now,
/// deadline) it raises <see cref="ExitRequested"/>; the window then closes without asking. The Debug menu exists only in
/// debug safety mode, where the service factory is the simulation. Every member runs on the UI thread.
/// </para>
/// </remarks>
public sealed partial class MainWindowViewModel : LocalizedViewModelBase
{
    /// <summary>How long the deadline message stays on screen before the application exits.</summary>
    public static readonly TimeSpan DeadlineMessageDuration = TimeSpan.FromSeconds(5);

    private const int CompletedExitCode = 0;
    private const int IncompleteExitCode = 1;

    private readonly IThemeService _themeService;
    private readonly IApplicationShellService _applicationShellService;
    private readonly IDeviceInformationReader _deviceInformationReader;
    private readonly IAutopilotServiceSetFactory _serviceSetFactory;
    private readonly RegistrationResultStore _resultStore;
    private readonly ISkipConfirmation _skipConfirmation;
    private readonly TimeProvider _timeProvider;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IKeyboardLayoutSwitcher _keyboardLayoutSwitcher;
    private readonly string? _assignedComputerName;
    private readonly string? _inheritedKeyboardLayoutId;
    private AutopilotServiceSet? _services;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _confirmationCts;
    private ITimer? _deadlineTimer;
    private ITimer? _exitTimer;
    private RegistrationOutcome? _registration;
    private DeviceInformation _deviceInformation = new(null, null, null);
    private bool _isSessionStarted;
    private bool _isCompleted;
    private bool _isEnding;
    private bool _isExitRequested;
    private bool _isInitialized;
    private bool _hasDeviceInformation;
    private bool _isDisposed;
    private string? _deadlineMessageKey;

    /// <summary>
    /// Initializes the shell; the Debug menu follows <see cref="DebugSafetyMode.IsEnabled"/>.
    /// </summary>
    /// <param name="localizationService">Source of the texts and of the language menu.</param>
    /// <param name="themeService">Applies the theme menu choices.</param>
    /// <param name="applicationShellService">Shows the About dialog.</param>
    /// <param name="deviceInformationReader">Reads the hardware identity for the footer.</param>
    /// <param name="configuration">
    /// Runtime configuration; its computer name is appended to the footer and its keyboard layout is applied by
    /// <see cref="ApplyInheritedKeyboardLayout"/>.
    /// </param>
    /// <param name="keyboardLayoutSwitcher">Lists the layouts available in this Windows and switches this process only.</param>
    /// <param name="signIn">Page 1.</param>
    /// <param name="groupTag">Page 2.</param>
    /// <param name="serviceSetFactory">Creates the services of a run (real, or simulated in debug safety mode).</param>
    /// <param name="resultStore">Writes <c>result.json</c>.</param>
    /// <param name="skipConfirmation">Asks before a close skips the registration.</param>
    /// <param name="timeProvider">Clock and timers of the deadline.</param>
    /// <param name="dispatcher">Delivers timer callbacks on the UI thread.</param>
    /// <param name="logger">Logs the run outcomes.</param>
    public MainWindowViewModel(
        ILocalizationService localizationService,
        IThemeService themeService,
        IApplicationShellService applicationShellService,
        IDeviceInformationReader deviceInformationReader,
        AutopilotInteractiveRuntimeConfiguration configuration,
        IKeyboardLayoutSwitcher keyboardLayoutSwitcher,
        SignInViewModel signIn,
        GroupTagViewModel groupTag,
        IAutopilotServiceSetFactory serviceSetFactory,
        RegistrationResultStore resultStore,
        ISkipConfirmation skipConfirmation,
        TimeProvider timeProvider,
        IUiDispatcher dispatcher,
        ILogger<MainWindowViewModel> logger)
        : this(
            localizationService,
            themeService,
            applicationShellService,
            deviceInformationReader,
            configuration,
            keyboardLayoutSwitcher,
            signIn,
            groupTag,
            serviceSetFactory,
            resultStore,
            skipConfirmation,
            timeProvider,
            dispatcher,
            logger,
            DebugSafetyMode.IsEnabled)
    {
    }

    /// <summary>Initializes the shell with an explicit debug safety flag, for tests.</summary>
    internal MainWindowViewModel(
        ILocalizationService localizationService,
        IThemeService themeService,
        IApplicationShellService applicationShellService,
        IDeviceInformationReader deviceInformationReader,
        AutopilotInteractiveRuntimeConfiguration configuration,
        IKeyboardLayoutSwitcher keyboardLayoutSwitcher,
        SignInViewModel signIn,
        GroupTagViewModel groupTag,
        IAutopilotServiceSetFactory serviceSetFactory,
        RegistrationResultStore resultStore,
        ISkipConfirmation skipConfirmation,
        TimeProvider timeProvider,
        IUiDispatcher dispatcher,
        ILogger<MainWindowViewModel> logger,
        bool isDebugSafeMode)
        : base(localizationService)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _applicationShellService = applicationShellService ?? throw new ArgumentNullException(nameof(applicationShellService));
        _deviceInformationReader = deviceInformationReader ?? throw new ArgumentNullException(nameof(deviceInformationReader));
        SignIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        GroupTag = groupTag ?? throw new ArgumentNullException(nameof(groupTag));
        _serviceSetFactory = serviceSetFactory ?? throw new ArgumentNullException(nameof(serviceSetFactory));
        _resultStore = resultStore ?? throw new ArgumentNullException(nameof(resultStore));
        _skipConfirmation = skipConfirmation ?? throw new ArgumentNullException(nameof(skipConfirmation));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keyboardLayoutSwitcher = keyboardLayoutSwitcher ?? throw new ArgumentNullException(nameof(keyboardLayoutSwitcher));
        _assignedComputerName = configuration.AssignedComputerName;
        _inheritedKeyboardLayoutId = configuration.KeyboardLayoutId;
        IsDebugMenuVisible = isDebugSafeMode;

        SignIn.SignedIn += OnSignedIn;
        GroupTag.SessionExpired += OnSessionExpired;
        GroupTag.SignOutRequested += OnSignOutRequested;
        GroupTag.RegistrationFailed += OnRegistrationFailed;
        GroupTag.RegistrationSucceeded += OnRegistrationSucceeded;
        GroupTag.Completed += OnCompleted;
        GroupTag.ContinueRequested += OnContinueRequested;

        LocalizationService.LanguageChanged += OnLanguageChanged;
        RefreshSupportedCultures();
        RefreshKeyboardMenu();
        RefreshDebugScenarios();
    }

    /// <summary>Raised when the run ends by itself (countdown, Continue now, deadline): the window closes with <see cref="ExitCode"/>.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Gets page 1.</summary>
    public SignInViewModel SignIn { get; }

    /// <summary>Gets page 2.</summary>
    public GroupTagViewModel GroupTag { get; }

    /// <summary>Gets the page shown in the window.</summary>
    public AutopilotPage CurrentPage { get; private set; } = AutopilotPage.SignIn;

    /// <summary>Gets whether page 1 is shown.</summary>
    public bool IsSignInPage => CurrentPage == AutopilotPage.SignIn;

    /// <summary>Gets whether page 2 is shown.</summary>
    public bool IsGroupTagPage => CurrentPage == AutopilotPage.GroupTag;

    /// <summary>Gets the process exit code: 0 once the registration completed, 1 otherwise.</summary>
    public int ExitCode { get; private set; } = IncompleteExitCode;

    /// <summary>Gets the message shown when the deadline ended the run, empty otherwise.</summary>
    public string DeadlineMessage => _deadlineMessageKey is null ? string.Empty : LocalizationService.GetString(_deadlineMessageKey);

    /// <summary>Gets whether the deadline message is shown.</summary>
    public bool HasDeadlineMessage => _deadlineMessageKey is not null;

    /// <summary>Gets the supported UI cultures, the active one marked as selected.</summary>
    public ObservableCollection<SupportedCultureOption> SupportedCultures { get; } = [];

    /// <summary>Gets the keyboard menu entries: the active language's layouts, then the other languages.</summary>
    public ObservableCollection<KeyboardMenuEntry> KeyboardMenuEntries { get; } = [];

    /// <summary>Gets the keyboard menu header, including the active layout's short name when it is known.</summary>
    public string KeyboardMenuHeader { get; private set; } = string.Empty;

    /// <summary>Gets the Debug menu entries, the active scenario checked; empty outside debug safety mode.</summary>
    public ObservableCollection<DebugScenarioOption> DebugScenarios { get; } = [];

    /// <summary>Gets the simulated scenario of the run.</summary>
    public DebugScenario ActiveDebugScenario { get; private set; } = DebugScenarioCatalog.Default;

    /// <summary>Gets the current application theme mode.</summary>
    public ThemeMode CurrentTheme => _themeService.CurrentTheme;

    /// <summary>Gets the localized window title.</summary>
    public string WindowTitle => LocalizationService.GetString("App.Name");

    /// <summary>Gets the version shown at the right of the menu bar.</summary>
    public string VersionDisplay => LocalizationService.Format("Common.VersionFormat", FoundryAutopilotInteractiveApplicationInfo.Version);

    /// <summary>Gets whether the Debug menu is shown: a Debug build with a debugger attached.</summary>
    public bool IsDebugMenuVisible { get; }

    /// <summary>Gets the footer line describing the device, empty until <see cref="InitializeAsync"/> read it.</summary>
    public string DeviceFooter { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the "Skip registration" button is shown and usable: false during the success countdown and once the run
    /// is ending (deadline message, exit requested).
    /// </summary>
    public bool CanSkipRegistration => !_isCompleted && !_isEnding && !_isExitRequested && !_isDisposed;

    /// <summary>
    /// Starts the run (unless a debug scenario already did), then reads the device identity off the UI thread for the
    /// footer. Later calls do nothing.
    /// </summary>
    /// <returns>A task that completes once the footer is set.</returns>
    public async Task InitializeAsync()
    {
        if (_isDisposed || _isInitialized)
        {
            return;
        }

        _isInitialized = true;
        if (!_isSessionStarted)
        {
            StartSession();
        }

        DeviceInformation information = await Task.Run(_deviceInformationReader.Read);
        if (_isDisposed)
        {
            return;
        }

        _deviceInformation = information;
        _hasDeviceInformation = true;
        RefreshDeviceFooter();
    }

    /// <summary>
    /// Applies the keyboard layout Foundry Deploy was using to this process, once the window is first activated (like the
    /// Foundry Deploy hand-off). Without a layout in the configuration, or when the layout does not exist in this Windows
    /// installation, the Windows layout is kept.
    /// </summary>
    public void ApplyInheritedKeyboardLayout()
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(_inheritedKeyboardLayoutId))
        {
            return;
        }

        KeyboardLayout? layout = KeyboardLayoutSelection.Find(_keyboardLayoutSwitcher.GetLayouts(), _inheritedKeyboardLayoutId);
        if (layout is null)
        {
            _logger.LogInformation(
                "The keyboard layout inherited from Foundry Deploy is not available in this Windows installation; the Windows layout is kept. LayoutId={LayoutId}",
                _inheritedKeyboardLayoutId);
            return;
        }

        if (_keyboardLayoutSwitcher.GetActiveLayoutId() == layout.Id)
        {
            return;
        }

        KeyboardLayoutSwitchResult result = _keyboardLayoutSwitcher.Apply(layout);
        if (result.Succeeded)
        {
            _logger.LogInformation("Keyboard layout inherited from Foundry Deploy applied. LayoutId={LayoutId}", layout.Id);
        }
        else
        {
            _logger.LogWarning("Keyboard layout inherited from Foundry Deploy was not applied. LayoutId={LayoutId}, Error={Error}", layout.Id, result.Error);
        }

        RefreshKeyboardMenu();
    }

    /// <summary>
    /// Decides whether the window may close, and records the outcome when it does.
    /// </summary>
    /// <remarks>
    /// No question is asked once the run decided to end, during the success countdown (exit 0) or during the profile wait
    /// (the device is registered: <c>completed</c> with the wait skipped, exit 0). Otherwise a confirmed close writes
    /// <c>skipped</c> and keeps exit code 1; while a registration runs, the question warns that the upload may already have
    /// been sent, so the device can still appear in Windows Autopilot. An open question is closed with a "no" answer when the device becomes registered,
    /// the deadline arrives or the run ends, so it never blocks a graceful exit.
    /// </remarks>
    /// <returns><see langword="true"/> when the window may close.</returns>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (_isExitRequested || _isEnding || _isDisposed)
        {
            return true;
        }

        if (_isCompleted)
        {
            _logger.LogInformation("Window closed during the success countdown. Exiting with code {ExitCode}.", CompletedExitCode);
            EndRun(CompletedExitCode);
            return true;
        }

        if (_registration is { } registration)
        {
            _logger.LogInformation("Window closed during the profile assignment wait; Windows setup continues.");
            CancelWork();
            WriteCompleted(registration, ProfileAssignmentOutcome.Skipped);
            EndRun(CompletedExitCode);
            return true;
        }

        // The upload request may already be sent once a registration runs; the profile wait was handled above.
        bool registrationInProgress = GroupTag.IsBusy;
        bool confirmed;
        using (CancellationTokenSource confirmation = new())
        {
            _confirmationCts = confirmation;
            try
            {
                confirmed = await _skipConfirmation.ConfirmAsync(registrationInProgress, confirmation.Token);
            }
            finally
            {
                _confirmationCts = null;
            }
        }

        // The run kept going while the question was open and may have closed it: an exit already requested closes the
        // window; the deadline message keeps it open for its few seconds; a device registered meanwhile makes "skipped"
        // wrong, so the window stays open for the next close to decide.
        if (_isExitRequested || _isDisposed)
        {
            return true;
        }

        if (_isEnding)
        {
            return false;
        }

        if (!confirmed || _isCompleted || _registration is not null)
        {
            return false;
        }

        _logger.LogInformation(
            "Autopilot registration skipped by the technician. RegistrationInProgress={RegistrationInProgress}. Exiting with code {ExitCode}.",
            registrationInProgress,
            IncompleteExitCode);
        CancelWork();
        WriteResult(new RegistrationResult(RegistrationResult.StatusSkipped, null, null, null, null, _timeProvider.GetUtcNow()));
        EndRun(IncompleteExitCode);
        return true;
    }

    /// <summary>Stops every operation and timer and unsubscribes. Idempotent.</summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        CancelConfirmation();
        CancelWork();
        _loadCts?.Dispose();
        _deadlineTimer?.Dispose();
        _exitTimer?.Dispose();
        SignIn.SignedIn -= OnSignedIn;
        GroupTag.SessionExpired -= OnSessionExpired;
        GroupTag.SignOutRequested -= OnSignOutRequested;
        GroupTag.RegistrationFailed -= OnRegistrationFailed;
        GroupTag.RegistrationSucceeded -= OnRegistrationSucceeded;
        GroupTag.Completed -= OnCompleted;
        GroupTag.ContinueRequested -= OnContinueRequested;
        SignIn.Dispose();
        GroupTag.Dispose();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        base.Dispose();
    }

    /// <summary>
    /// The "Skip registration" button: the same decision as closing the window (Alt+F4), through
    /// <see cref="ConfirmCloseAsync"/>. A confirmed skip, or the profile wait / success countdown shortcuts, end the run, which
    /// closes the window through <see cref="ExitRequested"/>. The command cannot run twice at once, so one question is open at a time.
    /// </summary>
    /// <returns>A task that completes when the question was answered.</returns>
    [RelayCommand(CanExecute = nameof(CanSkipRegistration))]
    private async Task SkipRegistrationAsync()
    {
        try
        {
            await ConfirmCloseAsync();
        }
        catch (Exception exception)
        {
            // Same policy as the window's close path: a failed question leaves the window open and usable.
            _logger.LogError("The skip confirmation failed. FailureType={FailureType}", exception.GetType().Name);
        }
    }

    [RelayCommand]
    private void SetSystemTheme() => SetTheme(ThemeMode.System);

    [RelayCommand]
    private void SetLightTheme() => SetTheme(ThemeMode.Light);

    [RelayCommand]
    private void SetDarkTheme() => SetTheme(ThemeMode.Dark);

    [RelayCommand]
    private void SetCulture(string cultureName)
    {
        LocalizationService.SetCulture(new CultureInfo(cultureName));
    }

    [RelayCommand]
    private void SetKeyboardLayout(string? layoutId)
    {
        KeyboardLayout? layout = KeyboardLayoutSelection.Find(_keyboardLayoutSwitcher.GetLayouts(), layoutId);
        if (layout is null)
        {
            return;
        }

        KeyboardLayoutSwitchResult result = _keyboardLayoutSwitcher.Apply(layout);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Keyboard layout change failed. LayoutId={LayoutId}, Error={Error}", layout.Id, result.Error);
        }
        else
        {
            _logger.LogInformation("Keyboard layout changed. LayoutId={LayoutId}", layout.Id);
        }

        RefreshKeyboardMenu();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        _applicationShellService.ShowAbout();
    }

    /// <summary>Restarts the run on a simulated scenario. Refused outside debug safety mode.</summary>
    [RelayCommand(CanExecute = nameof(IsDebugMenuVisible))]
    private void SetDebugScenario(DebugScenario scenario)
    {
        if (!IsDebugMenuVisible || _isDisposed || _isExitRequested)
        {
            return;
        }

        ActiveDebugScenario = scenario;
        RefreshDebugScenarios();
        _logger.LogInformation("Debug scenario selected. Scenario={Scenario}", scenario);

        if (scenario == DebugScenario.CompletedGuard)
        {
            // What the startup guard does when an earlier run completed: exit 0 without showing anything.
            CancelWork();
            _logger.LogInformation("Simulated completed result; exiting with code {ExitCode}.", CompletedExitCode);
            EndRun(CompletedExitCode);
            return;
        }

        StartSession();
    }

    private void StartSession()
    {
        CancelWork();
        _deadlineTimer?.Dispose();
        _deadlineTimer = null;
        _exitTimer?.Dispose();
        _exitTimer = null;
        _registration = null;
        _isCompleted = false;
        _isEnding = false;
        _deadlineMessageKey = null;
        ExitCode = IncompleteExitCode;
        OnPropertyChanged(nameof(ExitCode));
        OnPropertyChanged(nameof(DeadlineMessage));
        OnPropertyChanged(nameof(HasDeadlineMessage));
        RefreshSkipAvailability();

        _isSessionStarted = true;
        _services = _serviceSetFactory.Create(IsDebugMenuVisible ? ActiveDebugScenario : null);
        ArmDeadline(_services.DeadlineUtc);
        ShowSignIn(infoBannerKey: null);
    }

    private void ArmDeadline(DateTimeOffset? deadlineUtc)
    {
        if (DeadlinePolicy.StopAt(deadlineUtc) is not { } stopAt)
        {
            return;
        }

        TimeSpan due = stopAt - _timeProvider.GetUtcNow();
        _deadlineTimer = _timeProvider.CreateTimer(
            _ => _dispatcher.Run(OnDeadlineReached),
            null,
            due > TimeSpan.Zero ? due : TimeSpan.Zero,
            Timeout.InfiniteTimeSpan);
    }

    private void ShowSignIn(string? infoBannerKey)
    {
        if (_services is null)
        {
            return;
        }

        _loadCts?.Cancel();
        GroupTag.Reset();
        SetPage(AutopilotPage.SignIn);
        SignIn.Start(_services.DeviceCodeClient, infoBannerKey);
    }

    private void OnSignedIn(object? sender, AuthenticatedSession session)
    {
        if (_services is not { } services || _isEnding)
        {
            return;
        }

        SignIn.ShowLoadingGroupTags();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = LoadGroupTagPageAsync(services, session, _loadCts.Token);
    }

    private async Task LoadGroupTagPageAsync(AutopilotServiceSet services, AuthenticatedSession session, CancellationToken token)
    {
        try
        {
            await GroupTag.LoadAsync(services, session, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Microsoft Graph rejected the access token while loading the registration page; signing in again.");
            ShowSignIn("SignIn.Banner.SessionExpired");
            return;
        }
        catch (Exception exception)
        {
            // The registration service reports its own failures; anything else must not leave the page loading forever.
            _logger.LogError("The registration page could not be loaded. FailureType={FailureType}", exception.GetType().Name);
            ShowSignIn("SignIn.Banner.LoadFailed");
            return;
        }

        if (token.IsCancellationRequested || _isEnding)
        {
            return;
        }

        SignIn.Stop();
        SetPage(AutopilotPage.GroupTag);
        GroupTag.OnPageShown();
    }

    private void OnSessionExpired(object? sender, EventArgs e)
    {
        _logger.LogWarning("Microsoft Graph rejected the access token; signing in again.");
        ShowSignIn("SignIn.Banner.SessionExpired");
    }

    private void OnSignOutRequested(object? sender, EventArgs e)
    {
        _logger.LogInformation("The technician chose another account; signing in again.");
        ShowSignIn("SignIn.Banner.SignedOut");
    }

    private void OnRegistrationFailed(object? sender, string failureCode)
    {
        WriteResult(new RegistrationResult(RegistrationResult.StatusFailed, failureCode, null, null, null, _timeProvider.GetUtcNow()));
    }

    private void OnRegistrationSucceeded(object? sender, RegistrationOutcome outcome)
    {
        _registration = outcome;

        // "Skip the registration?" no longer applies to a registered device.
        CancelConfirmation();
    }

    private void OnCompleted(object? sender, ProfileAssignmentOutcome? profile)
    {
        if (_registration is not { } registration || _isEnding)
        {
            return;
        }

        WriteCompleted(registration, profile);
        _isCompleted = true;
        ExitCode = CompletedExitCode;
        OnPropertyChanged(nameof(ExitCode));
        RefreshSkipAvailability();
    }

    private void OnContinueRequested(object? sender, EventArgs e)
    {
        if (_isCompleted)
        {
            EndRun(CompletedExitCode);
        }
    }

    private void OnDeadlineReached()
    {
        if (_isDisposed || _isEnding || _isExitRequested)
        {
            return;
        }

        if (_isCompleted)
        {
            _logger.LogInformation("The setup deadline is near; the registration is complete. Exiting with code {ExitCode}.", CompletedExitCode);
            EndRun(CompletedExitCode);
            return;
        }

        if (_registration is { } registration)
        {
            _logger.LogInformation("The setup deadline is near during the profile assignment wait. Exiting with code {ExitCode}.", CompletedExitCode);
            CancelWork();
            WriteCompleted(registration, ProfileAssignmentOutcome.TimedOut);
            EndRun(CompletedExitCode);
            return;
        }

        _logger.LogWarning("The setup deadline is near and the device is not registered. Exiting with code {ExitCode}.", IncompleteExitCode);
        CancelWork();
        WriteResult(new RegistrationResult(
            RegistrationResult.StatusFailed,
            RegistrationFailureCodes.DeadlineReached,
            null,
            null,
            null,
            _timeProvider.GetUtcNow()));
        _isEnding = true;
        ExitCode = IncompleteExitCode;
        _deadlineMessageKey = "Deadline.Reached";
        OnPropertyChanged(nameof(ExitCode));
        OnPropertyChanged(nameof(DeadlineMessage));
        OnPropertyChanged(nameof(HasDeadlineMessage));
        RefreshSkipAvailability();
        _exitTimer = _timeProvider.CreateTimer(
            _ => _dispatcher.Run(() => EndRun(IncompleteExitCode)),
            null,
            DeadlineMessageDuration,
            Timeout.InfiniteTimeSpan);

        // Last, once the outcome is recorded: an open skip question closes so the deadline message is visible.
        CancelConfirmation();
    }

    private void EndRun(int exitCode)
    {
        if (_isExitRequested || _isDisposed)
        {
            return;
        }

        _isEnding = true;
        _isExitRequested = true;
        ExitCode = exitCode;
        OnPropertyChanged(nameof(ExitCode));
        RefreshSkipAvailability();
        _logger.LogInformation("Foundry.AutopilotInteractive is ending. ExitCode={ExitCode}", exitCode);
        ExitRequested?.Invoke(this, EventArgs.Empty);

        // An open skip question would otherwise hold the window until PostInstall stops the process.
        CancelConfirmation();
    }

    private void CancelConfirmation()
    {
        _confirmationCts?.Cancel();
    }

    private void RefreshSkipAvailability()
    {
        OnPropertyChanged(nameof(CanSkipRegistration));
        SkipRegistrationCommand.NotifyCanExecuteChanged();
    }

    private void CancelWork()
    {
        _loadCts?.Cancel();
        SignIn.Stop();
        GroupTag.Cancel();
    }

    private void WriteCompleted(RegistrationOutcome registration, ProfileAssignmentOutcome? profile)
    {
        WriteResult(new RegistrationResult(
            RegistrationResult.StatusCompleted,
            null,
            registration.ImportId,
            registration.AutopilotDeviceId,
            profile is { } outcome ? RegistrationResult.ToResultValue(outcome) : null,
            _timeProvider.GetUtcNow()));
    }

    private void WriteResult(RegistrationResult result)
    {
        try
        {
            _resultStore.Write(result);
            _logger.LogInformation(
                "Registration result written. Status={Status}, FailureCode={FailureCode}, ProfileAssignment={ProfileAssignment}",
                result.Status,
                result.FailureCode,
                result.ProfileAssignment);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exit code still tells PostInstall the outcome; a missing file only costs a later attempt.
            _logger.LogWarning(
                "The registration result could not be written. Status={Status}, FailureType={FailureType}, ErrorCode={ErrorCode}",
                result.Status,
                exception.GetType().Name,
                exception.HResult);
        }
    }

    private void SetPage(AutopilotPage page)
    {
        if (CurrentPage == page)
        {
            return;
        }

        CurrentPage = page;
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(IsSignInPage));
        OnPropertyChanged(nameof(IsGroupTagPage));
    }

    private void SetTheme(ThemeMode theme)
    {
        _themeService.SetTheme(theme);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    private void RefreshSupportedCultures()
    {
        SupportedCultures.Clear();
        foreach (SupportedCultureOption option in LocalizationService.CreateSupportedCultureOptions())
        {
            SupportedCultures.Add(option);
        }
    }

    private void RefreshKeyboardMenu()
    {
        IReadOnlyList<KeyboardLayout> layouts = _keyboardLayoutSwitcher.GetLayouts();
        string activeLayoutId = _keyboardLayoutSwitcher.GetActiveLayoutId();
        KeyboardMenuEntries.Clear();
        foreach (KeyboardMenuEntry entry in KeyboardLayoutSelection.BuildMenuEntries(layouts, activeLayoutId, LocalizationService.GetString("Keyboard.OtherLanguages")))
        {
            KeyboardMenuEntries.Add(entry);
        }

        KeyboardLayout? activeLayout = KeyboardLayoutSelection.Find(layouts, activeLayoutId);
        KeyboardMenuHeader = activeLayout is null
            ? LocalizationService.GetString("Menu.Keyboard")
            : LocalizationService.Format("Menu.KeyboardFormat", activeLayout.ShortName);
        OnPropertyChanged(nameof(KeyboardMenuHeader));
    }

    private void RefreshDebugScenarios()
    {
        DebugScenarios.Clear();
        if (!IsDebugMenuVisible)
        {
            return;
        }

        foreach (DebugScenario scenario in DebugScenarioCatalog.All)
        {
            DebugScenarios.Add(new DebugScenarioOption(
                scenario,
                LocalizationService.GetString(DebugScenarioCatalog.ResourceKey(scenario)),
                scenario == ActiveDebugScenario));
        }
    }

    // The unknown-value text is localized, so the footer is rebuilt from the cached identity instead of re-reading WMI.
    private void RefreshDeviceFooter()
    {
        DeviceFooter = DeviceFooterFormatter.Format(
            _deviceInformation,
            _assignedComputerName,
            LocalizationService.GetString("Footer.Unknown"));
        OnPropertyChanged(nameof(DeviceFooter));
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            RefreshSupportedCultures();
            RefreshKeyboardMenu();
            RefreshDebugScenarios();
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(VersionDisplay));
            OnPropertyChanged(nameof(DeadlineMessage));
            if (_hasDeviceInformation)
            {
                RefreshDeviceFooter();
            }
        });
    }
}

/// <summary>
/// One Debug menu entry.
/// </summary>
/// <param name="Scenario">The scenario it selects.</param>
/// <param name="Header">Localized menu text.</param>
/// <param name="IsSelected">Whether it is the active scenario.</param>
public sealed record DebugScenarioOption(DebugScenario Scenario, string Header, bool IsSelected);
