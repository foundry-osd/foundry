// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.Input;
using Foundry.AutopilotInteractive.Services;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Autopilot;
using Foundry.Localization;
using Foundry.Wpf.Controls;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.ViewModels;

/// <summary>
/// One entry of the group tag list.
/// </summary>
/// <param name="Choice">Kind of entry.</param>
/// <param name="Tag">The discovered tag for <see cref="GroupTagChoice.Existing"/>, otherwise <see langword="null"/>.</param>
/// <param name="Label">Text shown in the list.</param>
public sealed record GroupTagOption(GroupTagChoice Choice, string? Tag, string Label);

/// <summary>
/// Page 2: group tag choice, optional assigned user, registration, profile assignment wait and success countdown.
/// </summary>
/// <remarks>
/// The page is loaded on page 1 (<see cref="LoadAsync"/>) and shown once loaded. Outcomes are reported through events so the
/// shell writes the result and decides when to exit: <see cref="RegistrationFailed"/> keeps the page open for another try,
/// <see cref="Completed"/> starts the countdown, <see cref="ContinueRequested"/> ends it. A Graph 401 raises
/// <see cref="SessionExpired"/>. After <see cref="Cancel"/> or <see cref="Reset"/>, a late outcome of the cancelled work is
/// ignored. Every member runs on the UI thread.
/// </remarks>
public sealed partial class GroupTagViewModel : LocalizedViewModelBase
{
    /// <summary>Duration of the countdown before Windows setup continues after a registration.</summary>
    public static readonly TimeSpan SuccessCountdown = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly AutopilotInteractiveRuntimeConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<GroupTagViewModel> _logger;
    private AutopilotServiceSet? _services;
    private AuthenticatedSession? _session;
    private CancellationTokenSource _workCts = new();
    private CancellationTokenSource? _skipCts;
    private ITimer? _timer;
    private IReadOnlyList<string> _tags = [];
    private AutopilotDeviceLookup _lookup = new(AutopilotDeviceLookupStatus.NotFound, null);
    private bool _tagsUnavailable;
    private bool _isCustomFocusPending;
    private string? _statusKey;
    private string? _failureCode;
    private ProfileAssignmentOutcome? _profileOutcome;
    private DateTimeOffset _profileWaitStartUtc;
    private DateTimeOffset _profileStopAtUtc;
    private int _secondsRemaining;
    private GroupTagOption? _selectedChoice;
    private string _customText = string.Empty;
    private string _assignedUser = string.Empty;
    private bool _isDisposed;

    /// <param name="localizationService">Source of the texts.</param>
    /// <param name="configuration">Runtime configuration: custom preselection and computer name.</param>
    /// <param name="timeProvider">Clock and timers of the profile wait and the countdown.</param>
    /// <param name="dispatcher">Delivers progress and timer ticks on the UI thread.</param>
    /// <param name="logger">Logs the profile wait outcome.</param>
    public GroupTagViewModel(
        ILocalizationService localizationService,
        AutopilotInteractiveRuntimeConfiguration configuration,
        TimeProvider timeProvider,
        IUiDispatcher dispatcher,
        ILogger<GroupTagViewModel> logger)
        : base(localizationService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Raised when Graph rejected the access token: the technician must sign in again.</summary>
    public event EventHandler? SessionExpired;

    /// <summary>Raised by Use another account: the token is discarded and page 1 shows a new code.</summary>
    public event EventHandler? SignOutRequested;

    /// <summary>Raised with the failure code when a registration failed; the page stays open for another try.</summary>
    public event EventHandler<string>? RegistrationFailed;

    /// <summary>Raised when the device is registered, before the profile assignment wait starts.</summary>
    public event EventHandler<RegistrationOutcome>? RegistrationSucceeded;

    /// <summary>Raised when the registration is complete, with the profile outcome (<see langword="null"/> when the wait did not start).</summary>
    public event EventHandler<ProfileAssignmentOutcome?>? Completed;

    /// <summary>Raised when the success countdown ends or the technician selects Continue now.</summary>
    public event EventHandler? ContinueRequested;

    /// <summary>Raised when the page appears with Custom preselected: the view focuses the custom text box.</summary>
    public event EventHandler? CustomFocusRequested;

    /// <summary>Gets "Signed in as &lt;account&gt;".</summary>
    public string AccountText => LocalizationService.Format(
        "GroupTag.SignedInAsFormat",
        _session?.AccountName ?? LocalizationService.GetString("GroupTag.UnknownAccount"));

    /// <summary>Gets the title of the registered device banner, empty when none is shown.</summary>
    public string RegisteredDeviceBannerTitle => _lookup.Status == AutopilotDeviceLookupStatus.Found
        ? LocalizationService.GetString("GroupTag.RegisteredDevice")
        : string.Empty;

    /// <summary>Gets the banner about an already registered device or about several matches, empty when none is shown.</summary>
    public string RegisteredDeviceBanner => _lookup switch
    {
        { Status: AutopilotDeviceLookupStatus.Found, Device: { } device } => LocalizationService.Format(
            "GroupTag.RegisteredDeviceDetailsFormat",
            ValueOrNone(device.GroupTag),
            ValueOrNone(device.UserPrincipalName)),
        { Status: AutopilotDeviceLookupStatus.Ambiguous } => LocalizationService.GetString("GroupTag.SeveralMatches"),
        _ => string.Empty
    };

    /// <summary>Gets the severity of the registered device banner: warning for several matches.</summary>
    public InfoBarSeverity RegisteredDeviceBannerSeverity => _lookup.Status == AutopilotDeviceLookupStatus.Ambiguous
        ? InfoBarSeverity.Warning
        : InfoBarSeverity.Informational;

    /// <summary>Gets whether the registered device banner is shown.</summary>
    public bool HasRegisteredDeviceBanner => RegisteredDeviceBanner.Length > 0;

    /// <summary>Gets the warning shown when the tag discovery failed, empty otherwise.</summary>
    public string TagDiscoveryWarning => _tagsUnavailable ? LocalizationService.GetString("GroupTag.TagsUnavailable") : string.Empty;

    /// <summary>Gets whether the tag discovery warning is shown.</summary>
    public bool HasTagDiscoveryWarning => _tagsUnavailable;

    /// <summary>Gets the list entries: None, the discovered tags (sorted, distinct ignoring case), Custom.</summary>
    public ObservableCollection<GroupTagOption> GroupTagOptions { get; } = [];

    /// <summary>Gets or sets the selected list entry.</summary>
    public GroupTagOption? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (SetProperty(ref _selectedChoice, value))
            {
                OnPropertyChanged(nameof(IsCustomEnabled));
            }
        }
    }

    /// <summary>Gets or sets the custom group tag; blank means no tag, surrounding spaces are removed.</summary>
    public string CustomText
    {
        get => _customText;
        set => SetProperty(ref _customText, value ?? string.Empty);
    }

    /// <summary>Gets whether the custom text box is enabled: Custom is selected and no registration runs.</summary>
    public bool IsCustomEnabled => SelectedChoice?.Choice == GroupTagChoice.Custom && IsEditable;

    /// <summary>Gets whether the choices can be changed: no registration runs and none completed.</summary>
    public bool IsEditable => !IsBusy && !IsCompleted;

    /// <summary>Gets whether the assigned user field is shown.</summary>
    public bool ShowAssignedUser { get; private set; }

    /// <summary>Gets or sets the assigned user principal name; blank means no user.</summary>
    public string AssignedUser
    {
        get => _assignedUser;
        set
        {
            if (SetProperty(ref _assignedUser, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(AssignedUserError));
                RegisterCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the validation message of the assigned user, empty when the value is acceptable.</summary>
    public string AssignedUserError => ShowAssignedUser && !AssignedUserRules.IsValid(AssignedUser)
        ? LocalizationService.GetString("GroupTag.AssignedUserInvalid")
        : string.Empty;

    /// <summary>Gets whether a registration or a profile wait is running.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Gets the text of the current registration step, empty when idle.</summary>
    public string StatusText => _statusKey is null ? string.Empty : LocalizationService.GetString(_statusKey);

    /// <summary>Gets whether the progress bar is indeterminate (registration steps) rather than showing the profile wait.</summary>
    public bool IsProgressIndeterminate { get; private set; } = true;

    /// <summary>Gets the elapsed share of the profile wait, from 0 to 100.</summary>
    public double ProgressValue { get; private set; }

    /// <summary>Gets whether the profile assignment wait is running.</summary>
    public bool IsProfileWaiting { get; private set; }

    /// <summary>Gets "Waiting for the Autopilot profile assignment (mm:ss)", empty outside the wait.</summary>
    public string ProfileWaitText => IsProfileWaiting
        ? LocalizationService.Format("Registration.ProfileWaitFormat", SignInViewModel.FormatMinutesSeconds(_profileStopAtUtc - _timeProvider.GetUtcNow()))
        : string.Empty;

    /// <summary>Gets the localized failure message of the last registration, empty when none failed.</summary>
    public string FailureBanner => _failureCode is null
        ? string.Empty
        : LocalizationService.GetString(RegistrationFailureCodes.ResourceKey(_failureCode));

    /// <summary>Gets whether the failure banner is shown.</summary>
    public bool HasFailure => _failureCode is not null;

    /// <summary>Gets whether the registration completed; the success panel and countdown are shown.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Gets the profile outcome line of the success panel, empty when the wait did not start.</summary>
    public string ProfileOutcomeText => _profileOutcome is { } outcome
        ? LocalizationService.GetString("Registration.Profile." + outcome)
        : string.Empty;

    /// <summary>Gets whether the profile outcome deserves attention (failed, timed out or unavailable).</summary>
    public bool IsProfileOutcomeWarning => _profileOutcome is ProfileAssignmentOutcome.Failed
        or ProfileAssignmentOutcome.TimedOut
        or ProfileAssignmentOutcome.Unavailable;

    /// <summary>Gets "Continuing Windows setup in N seconds".</summary>
    public string CountdownText => IsCompleted
        ? LocalizationService.Format("Registration.CountdownFormat", _secondsRemaining)
        : string.Empty;

    private bool CanRegister => _services is not null && IsEditable && AssignedUserRules.IsValid(ShowAssignedUser ? AssignedUser : null);

    private bool CanUseAnotherAccount => IsEditable;

    /// <summary>
    /// Loads the tags and the device lookup for a signed-in account and prepares the initial selection.
    /// </summary>
    /// <param name="services">Services of the current run.</param>
    /// <param name="session">The signed-in account.</param>
    /// <param name="ct">Cancels the loading.</param>
    /// <returns>A task that completes when the page is ready to show.</returns>
    /// <exception cref="HttpRequestException">Graph answered 401: the technician must sign in again.</exception>
    public async Task LoadAsync(AutopilotServiceSet services, AuthenticatedSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(session);

        Reset();
        RegistrationContext context = await services.RegistrationService.LoadAsync(session.AccessToken, ct);
        ct.ThrowIfCancellationRequested();

        _services = services;
        _session = session;
        _tags = [.. context.GroupTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        _tagsUnavailable = context.GroupTagsUnavailable;
        _lookup = context.Lookup;
        ShowAssignedUser = services.OfferAssignedUser;

        GroupTagInitialSelection selection = GroupTagSelection.Resolve(_tags, _lookup, _configuration.PreselectCustomGroupTag);
        RebuildOptions(selection.Choice, selection.ExistingTag);
        CustomText = selection.CustomText ?? string.Empty;
        _isCustomFocusPending = selection.FocusCustom;
        AssignedUser = _lookup.Status == AutopilotDeviceLookupStatus.Found ? _lookup.Device?.UserPrincipalName ?? string.Empty : string.Empty;
        RaiseAll();
    }

    /// <summary>Asks the view to focus the custom box when the page appears with Custom preselected.</summary>
    public void OnPageShown()
    {
        if (_isCustomFocusPending)
        {
            _isCustomFocusPending = false;
            CustomFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Cancels any running registration, wait or countdown; their late outcomes are ignored.</summary>
    public void Cancel()
    {
        _workCts.Cancel();
        _workCts.Dispose();
        _workCts = new CancellationTokenSource();
        StopTimer();
    }

    /// <summary>Cancels the running work and clears the page for a new account.</summary>
    public void Reset()
    {
        Cancel();
        _services = null;
        _session = null;
        _tags = [];
        _tagsUnavailable = false;
        _lookup = new AutopilotDeviceLookup(AutopilotDeviceLookupStatus.NotFound, null);
        _statusKey = null;
        _failureCode = null;
        _profileOutcome = null;
        _isCustomFocusPending = false;
        IsBusy = false;
        IsProfileWaiting = false;
        IsCompleted = false;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        ShowAssignedUser = false;
        GroupTagOptions.Clear();
        SelectedChoice = null;
        CustomText = string.Empty;
        AssignedUser = string.Empty;
        RaiseAll();
    }

    /// <summary>Cancels the work and stops the timers. Idempotent.</summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _workCts.Cancel();
        _workCts.Dispose();
        StopTimer();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        base.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        if (_services is not { } services || _session is not { } session)
        {
            return;
        }

        CancellationToken token = _workCts.Token;
        GroupTagOption? choice = SelectedChoice;
        RegistrationRequest request = new(
            session.AccessToken,
            GroupTagSelection.ToRequestedTag(choice?.Choice ?? GroupTagChoice.None, choice?.Tag, CustomText),
            ShowAssignedUser ? AssignedUserRules.ToRequestedUser(AssignedUser) : null,
            string.IsNullOrWhiteSpace(_configuration.AssignedComputerName) ? null : _configuration.AssignedComputerName);

        _failureCode = null;
        _statusKey = "Registration.Status.Collecting";
        IsProgressIndeterminate = true;
        SetBusy(true);

        RegistrationOutcome outcome;
        try
        {
            DispatcherProgress<AutopilotHardwareHashUploadProgress> progress = new(_dispatcher, report =>
            {
                if (!token.IsCancellationRequested)
                {
                    ApplyProgress(report);
                }
            });
            outcome = await services.RegistrationService.RegisterAsync(request, progress, token);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (!token.IsCancellationRequested)
            {
                _statusKey = null;
                SetBusy(false);
                SessionExpired?.Invoke(this, EventArgs.Empty);
            }

            return;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (!outcome.Succeeded)
        {
            _failureCode = outcome.FailureCode ?? RegistrationFailureCodes.RegistrationFailed;
            _statusKey = null;
            SetBusy(false);
            RegistrationFailed?.Invoke(this, _failureCode);
            return;
        }

        RegistrationSucceeded?.Invoke(this, outcome);
        ProfileAssignmentOutcome? profile = await WaitForProfileAsync(services, session, outcome.AutopilotDeviceId, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        Complete(profile);
    }

    [RelayCommand(CanExecute = nameof(CanUseAnotherAccount))]
    private void UseAnotherAccount()
    {
        SignOutRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ContinueWithoutWaiting()
    {
        _skipCts?.Cancel();
    }

    [RelayCommand]
    private void ContinueNow()
    {
        if (!IsCompleted)
        {
            return;
        }

        StopTimer();
        ContinueRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task<ProfileAssignmentOutcome?> WaitForProfileAsync(
        AutopilotServiceSet services,
        AuthenticatedSession session,
        string? deviceId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            _logger.LogWarning("The registered device has no Autopilot identity; the profile assignment wait is skipped.");
            return null;
        }

        using CancellationTokenSource skip = CancellationTokenSource.CreateLinkedTokenSource(token);
        _skipCts = skip;
        _profileWaitStartUtc = _timeProvider.GetUtcNow();
        _profileStopAtUtc = ProfileAssignmentWaiter.ComputeStopAt(_profileWaitStartUtc, services.DeadlineUtc);
        _statusKey = null;
        IsProfileWaiting = true;
        IsProgressIndeterminate = false;
        UpdateProfileWait();
        StartTimer(UpdateProfileWait);
        _logger.LogInformation("Profile assignment wait started.");

        ProfileAssignmentOutcome outcome;
        try
        {
            outcome = await services.ProfileAssignment.WaitAsync(session.AccessToken, deviceId, _profileStopAtUtc, skip.Token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            // The wait is informational: an unexpected failure never fails the registration.
            _logger.LogWarning("The profile assignment wait failed. FailureType={FailureType}", exception.GetType().Name);
            outcome = ProfileAssignmentOutcome.Unavailable;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _skipCts = null;
        }

        if (token.IsCancellationRequested)
        {
            return null;
        }

        StopTimer();
        IsProfileWaiting = false;
        _logger.LogInformation("Profile assignment wait ended. Outcome={Outcome}", outcome);
        return outcome;
    }

    private void Complete(ProfileAssignmentOutcome? profile)
    {
        _profileOutcome = profile;
        _statusKey = null;
        IsProfileWaiting = false;
        IsCompleted = true;
        _secondsRemaining = (int)SuccessCountdown.TotalSeconds;
        SetBusy(false);
        Completed?.Invoke(this, profile);
        StartTimer(OnCountdownTick);
    }

    private void OnCountdownTick()
    {
        if (!IsCompleted || _timer is null)
        {
            return;
        }

        _secondsRemaining = Math.Max(0, _secondsRemaining - 1);
        OnPropertyChanged(nameof(CountdownText));
        if (_secondsRemaining == 0)
        {
            StopTimer();
            ContinueRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateProfileWait()
    {
        if (!IsProfileWaiting)
        {
            return;
        }

        TimeSpan total = _profileStopAtUtc - _profileWaitStartUtc;
        TimeSpan elapsed = _timeProvider.GetUtcNow() - _profileWaitStartUtc;
        ProgressValue = total > TimeSpan.Zero ? Math.Clamp(elapsed / total * 100, 0, 100) : 100;
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProfileWaitText));
    }

    /// <summary>
    /// Maps a progress message of the shared Graph client to the resource key of the localized step name.
    /// </summary>
    /// <remarks>
    /// The client reports English texts; a test pins the mapping against the messages it actually emits.
    /// </remarks>
    /// <param name="message">The <see cref="AutopilotHardwareHashUploadProgress.Message"/> reported by the client.</param>
    /// <returns>The step resource key, or <see langword="null"/> when the message names no known step.</returns>
    internal static string? StatusKeyForProgress(string message) =>
        message.StartsWith("Uploading", StringComparison.OrdinalIgnoreCase)
            ? "Registration.Status.Uploading"
            : message.Contains("visibility", StringComparison.OrdinalIgnoreCase)
                ? "Registration.Status.WaitingForRegistration"
                : message.StartsWith("Updating", StringComparison.OrdinalIgnoreCase) || message.Contains("property", StringComparison.OrdinalIgnoreCase)
                    ? "Registration.Status.Assigning"
                    : null;

    private void ApplyProgress(AutopilotHardwareHashUploadProgress report)
    {
        string? key = StatusKeyForProgress(report.Message);
        if (key is not null && key != _statusKey)
        {
            _statusKey = key;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void StartTimer(Action tick)
    {
        StopTimer();
        CancellationToken token = _workCts.Token;
        _timer = _timeProvider.CreateTimer(
            _ => _dispatcher.Run(() =>
            {
                if (!_isDisposed && !token.IsCancellationRequested)
                {
                    tick();
                }
            }),
            null,
            TickInterval,
            TickInterval);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        RaiseAll();
    }

    private void RebuildOptions(GroupTagChoice choice, string? tag)
    {
        GroupTagOptions.Clear();
        GroupTagOptions.Add(new GroupTagOption(GroupTagChoice.None, null, LocalizationService.GetString("GroupTag.None")));
        foreach (string discovered in _tags)
        {
            GroupTagOptions.Add(new GroupTagOption(GroupTagChoice.Existing, discovered, discovered));
        }

        GroupTagOptions.Add(new GroupTagOption(GroupTagChoice.Custom, null, LocalizationService.GetString("GroupTag.Custom")));
        SelectedChoice = GroupTagOptions.FirstOrDefault(option => option.Choice == choice
                && (choice != GroupTagChoice.Existing || string.Equals(option.Tag, tag, StringComparison.Ordinal)))
            ?? GroupTagOptions[0];
    }

    private string ValueOrNone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? LocalizationService.GetString("GroupTag.NoneValue") : value.Trim();

    private void RaiseAll()
    {
        // An empty name refreshes every binding of the page; the commands are refreshed separately.
        OnPropertyChanged(string.Empty);
        RegisterCommand.NotifyCanExecuteChanged();
        UseAnotherAccountCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            if (GroupTagOptions.Count > 0)
            {
                GroupTagOption? selected = SelectedChoice;
                RebuildOptions(selected?.Choice ?? GroupTagChoice.None, selected?.Tag);
            }

            OnPropertyChanged(string.Empty);
        });
    }
}
