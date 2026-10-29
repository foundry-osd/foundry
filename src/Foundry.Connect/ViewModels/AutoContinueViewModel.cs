// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// D3 automatic continue: a 10 second countdown once the internet is online, paused for the session by a networking
/// interaction (and never started once one happened), cancelled by "Stay here", and ended by the final probe of
/// <see cref="ISuccessfulExitService"/>.
/// </summary>
/// <remarks>
/// <see cref="AutoContinuePolicy"/> decides every transition; this view model owns the clock and the only timer, a one
/// second tick created through the injected <see cref="TimeProvider"/> and marshaled through <see cref="IUiDispatcher"/>.
/// A failed final probe returns to <see cref="AutoContinueStatus.Idle"/>, so the next online state starts a fresh
/// countdown. In live mode the countdown runs only when <c>isAutoContinueEnabled</c> is set (no debugger attached,
/// L3-6); under a debug scenario it always runs, and its end and Continue never probe or exit (D16). Continue stays
/// available whenever the internet is online and no exit was requested; only one final probe runs at a time, and a probe
/// that throws counts as a failed probe. Every member runs on the UI thread; timer and completion callbacks that arrive
/// after <see cref="Dispose"/> are ignored.
/// </remarks>
public sealed partial class AutoContinueViewModel : LocalizedViewModelBase
{
    /// <summary>
    /// Interval between two countdown ticks.
    /// </summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly ISuccessfulExitService _successfulExit;
    private readonly IApplicationLifetimeService _lifetime;
    private readonly TimeProvider _timeProvider;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<AutoContinueViewModel> _logger;
    private readonly bool _isAutoContinueEnabled;
    private readonly CancellationTokenSource _disposeCts = new();
    private InternetStatus _internet = InternetStatus.Checking;
    private ITimer? _timer;
    private bool _isSimulation;
    private bool _hasNetworkingInteraction;
    private bool _isCompleting;
    private bool _isDisposed;

    /// <summary>
    /// Initializes the view model in the <see cref="AutoContinueState.Initial"/> state.
    /// </summary>
    /// <param name="localization">Source of the caption texts.</param>
    /// <param name="successfulExit">Single exit path; performs the final probe.</param>
    /// <param name="lifetime">Tells whether an exit was already requested.</param>
    /// <param name="timeProvider">Clock and timer factory of the countdown.</param>
    /// <param name="dispatcher">Marshals timer ticks to the UI thread.</param>
    /// <param name="logger">Logger for the countdown lifecycle.</param>
    /// <param name="isAutoContinueEnabled">Whether the countdown runs in live mode.</param>
    public AutoContinueViewModel(
        ILocalizationService localization,
        ISuccessfulExitService successfulExit,
        IApplicationLifetimeService lifetime,
        TimeProvider timeProvider,
        IUiDispatcher dispatcher,
        ILogger<AutoContinueViewModel> logger,
        bool isAutoContinueEnabled)
        : base(localization)
    {
        _successfulExit = successfulExit ?? throw new ArgumentNullException(nameof(successfulExit));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isAutoContinueEnabled = isAutoContinueEnabled;
    }

    /// <summary>
    /// Raised after "Stay here", which hides its own button: the view moves the keyboard focus to Continue.
    /// </summary>
    public event EventHandler? ContinueFocusRequested;

    /// <summary>
    /// Gets the current countdown state.
    /// </summary>
    public AutoContinueState State { get; private set; } = AutoContinueState.Initial;

    /// <summary>
    /// Gets whether the countdown is running.
    /// </summary>
    public bool IsCounting => State.Status == AutoContinueStatus.Counting;

    /// <summary>
    /// Gets whether a networking interaction paused the countdown, or kept it from starting, for the session.
    /// </summary>
    public bool IsPaused => State.Status == AutoContinueStatus.Paused;

    /// <summary>
    /// Gets whether the technician chose to stay for the session.
    /// </summary>
    public bool IsCancelled => State.Status == AutoContinueStatus.Cancelled;

    /// <summary>
    /// Gets the whole seconds left, rounded up; 0 unless the countdown is running.
    /// </summary>
    public int SecondsRemaining { get; private set; }

    /// <summary>
    /// Gets the countdown caption for the current state, or an empty string when there is nothing to say.
    /// </summary>
    public string CaptionText => State.Status switch
    {
        AutoContinueStatus.Counting => LocalizationService.Format("Status.AutoContinueFormat", SecondsRemaining),
        AutoContinueStatus.Paused => LocalizationService.GetString("Status.AutoContinuePaused"),
        AutoContinueStatus.Cancelled => LocalizationService.GetString("Status.AutoContinueCancelled"),
        _ => string.Empty
    };

    /// <summary>
    /// Gets whether the caption is shown: only while the internet is online.
    /// </summary>
    public bool HasCaption => _internet == InternetStatus.Online && CaptionText.Length > 0;

    /// <summary>
    /// Gets whether Continue is available: the internet is online and no exit was requested yet.
    /// </summary>
    public bool CanContinue => _internet == InternetStatus.Online && _lifetime.RequestedExitCode is null;

    /// <summary>
    /// Gets whether Continue is the default button. It stops being the default for the rest of the session after the
    /// first networking interaction, whatever the countdown state ("Stay here" included), so an Enter pressed in the Wi-Fi
    /// panel never leaves for Deploy (L3-3).
    /// </summary>
    public bool IsContinueDefault => CanContinue && !_hasNetworkingInteraction;

    /// <summary>
    /// Gets or sets whether a debug scenario is simulated. Setting it stops the timer, forgets the networking
    /// interactions and restarts from <see cref="AutoContinueStatus.Idle"/> with the last internet status, as the page's
    /// focus logic does.
    /// </summary>
    public bool IsSimulation
    {
        get => _isSimulation;
        set
        {
            if (_isDisposed)
            {
                return;
            }

            bool changed = _isSimulation != value;
            _isSimulation = value;
            _hasNetworkingInteraction = false;
            Transition(AutoContinueState.Initial);
            if (changed)
            {
                OnPropertyChanged();
            }

            OnInternetStatusChanged(_internet);
        }
    }

    /// <summary>
    /// Gets the automatic continue started by the last elapsed countdown, for tests.
    /// </summary>
    internal Task? PendingCompletion { get; private set; }

    private bool IsCountdownAllowed => _isAutoContinueEnabled || _isSimulation;

    /// <summary>
    /// Applies the latest internet status: online starts the countdown (or offers Continue without counting after a
    /// networking interaction), anything else resets a running one.
    /// </summary>
    /// <param name="internet">The internet status read from the coordinator on the UI thread.</param>
    public void OnInternetStatusChanged(InternetStatus internet)
    {
        if (_isDisposed)
        {
            return;
        }

        _internet = internet;
        if (IsCountdownAllowed)
        {
            AutoContinueState next = AutoContinuePolicy.OnInternetStatus(State, internet, _timeProvider.GetUtcNow(), _hasNetworkingInteraction);
            if (next.Status == AutoContinueStatus.Paused && State.Status != AutoContinueStatus.Paused)
            {
                _logger.LogInformation("Automatic continue not started; the technician already interacted with the network.");
            }

            Transition(next);
        }

        NotifyAvailability();
    }

    /// <summary>
    /// Records a networking interaction for the session (D3, L3-4): a running countdown pauses, a later online state
    /// does not start one, and Continue stops being the default button.
    /// </summary>
    public void OnNetworkingInteraction()
    {
        if (_isDisposed)
        {
            return;
        }

        _hasNetworkingInteraction = true;
        AutoContinueState next = AutoContinuePolicy.OnNetworkingInteraction(State);
        if (next != State)
        {
            _logger.LogInformation("Automatic continue paused by a network interaction.");
            Transition(next);
        }

        NotifyAvailability();
    }

    /// <summary>
    /// Raises <see cref="CaptionText"/> again after a language change; the countdown keeps its deadline.
    /// </summary>
    public void RefreshTexts() => OnPropertyChanged(nameof(CaptionText));

    /// <summary>
    /// Stops the timer and cancels a pending completion. Idempotent.
    /// </summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer?.Dispose();
        _timer = null;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        base.Dispose();
    }

    [RelayCommand(CanExecute = nameof(IsCounting))]
    private void StayHere()
    {
        if (_isDisposed)
        {
            return;
        }

        _logger.LogInformation("Automatic continue cancelled by the technician.");
        Transition(AutoContinuePolicy.OnStayHere(State));

        // The "Stay here" button collapses with the countdown; without this the keyboard focus would be lost.
        ContinueFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private Task ContinueAsync() => CompleteAsync(automatic: false);

    private void Transition(AutoContinueState next)
    {
        if (next == State)
        {
            return;
        }

        State = next;
        if (next.Status == AutoContinueStatus.Counting)
        {
            _timer ??= _timeProvider.CreateTimer(_ => _dispatcher.Run(OnTick), null, TickInterval, TickInterval);
        }
        else
        {
            _timer?.Dispose();
            _timer = null;
        }

        UpdateRemaining();
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsCounting));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(CaptionText));
        OnPropertyChanged(nameof(HasCaption));
        OnPropertyChanged(nameof(IsContinueDefault));
        StayHereCommand.NotifyCanExecuteChanged();
    }

    private void OnTick()
    {
        // A tick queued on the dispatcher can run after Dispose or after the countdown stopped.
        if (_isDisposed || State.Status != AutoContinueStatus.Counting)
        {
            return;
        }

        AutoContinueState next = AutoContinuePolicy.OnTick(State, _timeProvider.GetUtcNow());
        if (AutoContinuePolicy.ShouldContinue(next))
        {
            Transition(next);
            PendingCompletion = CompleteAsync(automatic: true);
            return;
        }

        UpdateRemaining();
        OnPropertyChanged(nameof(CaptionText));
    }

    private async Task CompleteAsync(bool automatic)
    {
        // A countdown ending while a manual Continue probes joins that probe instead of starting a second one.
        if (_isDisposed || _isCompleting)
        {
            return;
        }

        if (_isSimulation)
        {
            _logger.LogInformation("Debug scenario active; continue is simulated. Automatic={Automatic}", automatic);
            return;
        }

        if (automatic)
        {
            _logger.LogInformation("Automatic continue elapsed; confirming internet access before leaving.");
        }

        CancellationToken token = _disposeCts.Token;
        _isCompleting = true;
        try
        {
            // No ConfigureAwait(false): the continuation changes bound properties on the UI thread.
            SuccessfulExitResult result = await _successfulExit.TryCompleteAsync(token);
            if (_isDisposed)
            {
                return;
            }

            if (result == SuccessfulExitResult.NotOnline)
            {
                Transition(AutoContinuePolicy.OnFinalProbeFailed(State));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            // Never rethrown: AsyncRelayCommand would crash the app, and the countdown would stay at Elapsed. The exception
            // is not attached because its message may name a network or a profile file.
            _logger.LogWarning("Continue failed; waiting for the next online state. FailureType={FailureType}", exception.GetType().Name);
            if (_isDisposed)
            {
                return;
            }

            Transition(AutoContinuePolicy.OnFinalProbeFailed(State));
        }
        finally
        {
            _isCompleting = false;
        }

        NotifyAvailability();
    }

    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(HasCaption));
        OnPropertyChanged(nameof(IsContinueDefault));
        ContinueCommand.NotifyCanExecuteChanged();
    }

    private void UpdateRemaining()
    {
        int seconds = (int)Math.Ceiling(State.RemainingAt(_timeProvider.GetUtcNow()).TotalSeconds);
        if (seconds != SecondsRemaining)
        {
            SecondsRemaining = seconds;
            OnPropertyChanged(nameof(SecondsRemaining));
        }
    }
}
