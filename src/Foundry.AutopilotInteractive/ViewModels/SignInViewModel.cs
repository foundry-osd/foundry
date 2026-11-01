// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.QrCode;
using Foundry.Localization;
using Foundry.Wpf.Controls;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.ViewModels;

/// <summary>
/// Page 1: runs the device code sign-in and shows the code, the QR code, the remaining lifetime and the banners.
/// </summary>
/// <remarks>
/// The page renders the <see cref="SignInState"/> reports of <see cref="SignInFlow"/>, delivered on the UI thread. The user
/// code and the QR code always change in the same call and are hidden while a new code is requested, so the technician never
/// sees a code and a QR code that disagree. Every member runs on the UI thread.
/// </remarks>
public sealed partial class SignInViewModel : LocalizedViewModelBase
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _timeProvider;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<SignInFlow> _flowLogger;
    private IDeviceCodeClient? _client;
    private CancellationTokenSource? _runCts;
    private DeviceCodeSession? _code;
    private DateTimeOffset _codeReceivedAtUtc;
    private ITimer? _tickTimer;
    private string? _infoBannerKey;
    private string? _errorKey;
    private bool _isWaitingForNetwork;
    private bool _isDisposed;

    /// <param name="localizationService">Source of the texts.</param>
    /// <param name="timeProvider">Clock and timers of the flow and of the remaining lifetime display.</param>
    /// <param name="dispatcher">Delivers the flow reports on the UI thread.</param>
    /// <param name="flowLogger">Logger given to each <see cref="SignInFlow"/> run.</param>
    public SignInViewModel(
        ILocalizationService localizationService,
        TimeProvider timeProvider,
        IUiDispatcher dispatcher,
        ILogger<SignInFlow> flowLogger)
        : base(localizationService)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _flowLogger = flowLogger ?? throw new ArgumentNullException(nameof(flowLogger));
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Raised on the UI thread once the technician signed in.</summary>
    public event EventHandler<AuthenticatedSession>? SignedIn;

    /// <summary>Gets the user code to type, empty while no code is shown.</summary>
    public string UserCode { get; private set; } = string.Empty;

    /// <summary>Gets the plain verification address shown as text (the QR code adds the user code to it).</summary>
    public string VerificationUriText { get; private set; } = string.Empty;

    /// <summary>Gets the QR code of the verification address with the user code, or <see langword="null"/> while no code is shown.</summary>
    public ImageSource? QrCodeImage { get; private set; }

    /// <summary>Gets whether a code, its QR code and its remaining lifetime are shown.</summary>
    public bool IsCodeVisible { get; private set; }

    /// <summary>Gets whether a new code is being requested.</summary>
    public bool IsRequestingCode { get; private set; }

    /// <summary>Gets whether the sign-in succeeded and the registration page is loading.</summary>
    public bool IsLoadingGroupTags { get; private set; }

    /// <summary>Gets whether the progress ring replaces the code area.</summary>
    public bool IsBusyIndicatorVisible => IsRequestingCode || IsLoadingGroupTags;

    /// <summary>Gets the text under the progress ring.</summary>
    public string BusyText => LocalizationService.GetString(IsLoadingGroupTags ? "SignIn.LoadingGroupTags" : "SignIn.RequestingCode");

    /// <summary>Gets the remaining lifetime of the code, from 1 (just issued) to 0 (expired).</summary>
    public double RemainingFraction { get; private set; }

    /// <summary>Gets the remaining lifetime of the code as <c>mm:ss</c>.</summary>
    public string RemainingText { get; private set; } = string.Empty;

    /// <summary>Gets the caption "Code expires in mm:ss".</summary>
    public string RemainingCaption => LocalizationService.Format("SignIn.ExpiresFormat", RemainingText);

    /// <summary>Gets the severity of the banner.</summary>
    public InfoBarSeverity BannerSeverity => _errorKey is not null
        ? InfoBarSeverity.Error
        : _isWaitingForNetwork ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;

    /// <summary>Gets the banner text, empty when no banner is shown.</summary>
    public string BannerText
    {
        get
        {
            string? key = _errorKey ?? (_isWaitingForNetwork ? "SignIn.Banner.WaitingForNetwork" : _infoBannerKey);
            return key is null ? string.Empty : LocalizationService.GetString(key);
        }
    }

    /// <summary>Gets whether a banner is shown.</summary>
    public bool HasBanner => BannerText.Length > 0;

    /// <summary>Gets whether Retry is offered: only after a blocking error.</summary>
    public bool IsRetryVisible => _errorKey is not null;

    /// <summary>Gets the running sign-in, for tests.</summary>
    internal Task? PendingSignIn { get; private set; }

    /// <summary>
    /// Maps an error code of <see cref="SignInFailedException"/> to the resource key of its message.
    /// </summary>
    /// <param name="errorCode">OAuth error code, <c>invalid_response</c>, <c>unexpected_error</c> or <c>http_&lt;status&gt;</c>.</param>
    /// <returns>
    /// <c>SignIn.Error.Declined</c> for a declined sign-in, <c>SignIn.Error.Generic</c> for an unreadable answer (often a
    /// proxy or captive portal), and <c>SignIn.Error.Blocked</c> for any other OAuth error, usually a Conditional Access block.
    /// </returns>
    public static string ErrorResourceKey(string errorCode) => errorCode switch
    {
        "access_denied" or "authorization_declined" => "SignIn.Error.Declined",
        "invalid_response" or "unexpected_error" or "request_failed" or "unknown_error" => "SignIn.Error.Generic",
        _ when errorCode.StartsWith("http_", StringComparison.Ordinal) => "SignIn.Error.Generic",
        _ => "SignIn.Error.Blocked"
    };

    /// <summary>
    /// Starts a new sign-in with a new code, cancelling a running one.
    /// </summary>
    /// <param name="client">Device code client of the current run.</param>
    /// <param name="infoBannerKey">Resource key of an informational banner (session expired, signed out), or <see langword="null"/>.</param>
    public void Start(IDeviceCodeClient client, string? infoBannerKey)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _client = client;
        _infoBannerKey = infoBannerKey;
        _errorKey = null;
        _isWaitingForNetwork = false;
        IsLoadingGroupTags = false;
        IsRequestingCode = true;
        RaiseStateChanged();

        _runCts = new CancellationTokenSource();
        PendingSignIn = RunAsync(client, _runCts.Token);
    }

    /// <summary>Cancels the running sign-in and hides the code. Idempotent.</summary>
    public void Stop()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        StopTick();
        SetCode(null);
    }

    /// <summary>Replaces the code area by the "Loading group tags" progress ring once the sign-in succeeded.</summary>
    public void ShowLoadingGroupTags()
    {
        StopTick();
        SetCode(null);
        _infoBannerKey = null;
        _errorKey = null;
        _isWaitingForNetwork = false;
        IsRequestingCode = false;
        IsLoadingGroupTags = true;
        RaiseStateChanged();
    }

    /// <summary>
    /// Renders a state reported by the sign-in flow.
    /// </summary>
    /// <param name="state">The reported state.</param>
    internal void Apply(SignInState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        switch (state.Phase)
        {
            case SignInPhase.RequestingCode:
                StopTick();
                SetCode(null);
                IsRequestingCode = true;
                _isWaitingForNetwork = false;
                break;
            case SignInPhase.WaitingForUser:
                SetCode(state.Code);
                IsRequestingCode = state.Code is null;
                _isWaitingForNetwork = false;
                break;
            case SignInPhase.WaitingForNetwork:
                // A failed code request has no code: the ring stays. A failed poll keeps the current code on screen.
                if (state.Code is not null)
                {
                    SetCode(state.Code);
                    IsRequestingCode = false;
                }

                _isWaitingForNetwork = true;
                break;
            case SignInPhase.Failed:
                StopTick();
                SetCode(null);
                IsRequestingCode = false;
                _isWaitingForNetwork = false;
                _errorKey = ErrorResourceKey(state.ErrorCode ?? "unexpected_error");
                break;
        }

        RaiseStateChanged();
    }

    /// <summary>Cancels the sign-in and stops the timer. Idempotent.</summary>
    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        base.Dispose();
    }

    [RelayCommand(CanExecute = nameof(IsRetryVisible))]
    private void Retry()
    {
        if (_client is not null)
        {
            Start(_client, infoBannerKey: null);
        }
    }

    private async Task RunAsync(IDeviceCodeClient client, CancellationToken token)
    {
        SignInFlow flow = new(
            client,
            (duration, ct) => Task.Delay(duration, _timeProvider, ct),
            _timeProvider.GetUtcNow,
            _flowLogger);
        DispatcherProgress<SignInState> progress = new(_dispatcher, state =>
        {
            if (!token.IsCancellationRequested)
            {
                Apply(state);
            }
        });

        try
        {
            AuthenticatedSession session = await flow.RunAsync(progress, token).ConfigureAwait(false);
            _dispatcher.Run(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    SignedIn?.Invoke(this, session);
                }
            });
        }
        catch (SignInFailedException)
        {
            // The flow already reported the Failed state, which shows the error banner and Retry; it logged the outcome.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A new sign-in, a page change or the deadline replaced this one.
        }
    }

    // UserCode, VerificationUriText, QrCodeImage and IsCodeVisible change together and are announced together.
    private void SetCode(DeviceCodeSession? code)
    {
        if (ReferenceEquals(code, _code))
        {
            return;
        }

        _code = code;
        if (code is null)
        {
            UserCode = string.Empty;
            VerificationUriText = string.Empty;
            QrCodeImage = null;
            IsCodeVisible = false;
        }
        else
        {
            UserCode = code.UserCode;
            VerificationUriText = code.VerificationUri.OriginalString;
            QrCodeImage = QrCodeImageFactory.Create(VerificationUrlBuilder.Build(code.VerificationUri, code.UserCode).AbsoluteUri);
            IsCodeVisible = true;
            _codeReceivedAtUtc = _timeProvider.GetUtcNow();
            UpdateRemaining();
            _tickTimer ??= _timeProvider.CreateTimer(_ => _dispatcher.Run(OnTick), null, TickInterval, TickInterval);
        }

        OnPropertyChanged(nameof(UserCode));
        OnPropertyChanged(nameof(VerificationUriText));
        OnPropertyChanged(nameof(QrCodeImage));
        OnPropertyChanged(nameof(IsCodeVisible));
    }

    private void OnTick()
    {
        if (_isDisposed || _code is null)
        {
            return;
        }

        UpdateRemaining();
    }

    private void UpdateRemaining()
    {
        if (_code is null)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan remaining = _code.ExpiresAtUtc - now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        TimeSpan lifetime = _code.ExpiresAtUtc - _codeReceivedAtUtc;
        RemainingFraction = lifetime > TimeSpan.Zero ? Math.Clamp(remaining / lifetime, 0, 1) : 0;
        RemainingText = FormatMinutesSeconds(remaining);
        OnPropertyChanged(nameof(RemainingFraction));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(RemainingCaption));
    }

    /// <summary>Formats a duration as <c>mm:ss</c>, rounding partial seconds up so 00:00 means expired.</summary>
    internal static string FormatMinutesSeconds(TimeSpan value)
    {
        int totalSeconds = (int)Math.Ceiling(Math.Max(0, value.TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 60:00}:{totalSeconds % 60:00}");
    }

    private void StopTick()
    {
        _tickTimer?.Dispose();
        _tickTimer = null;
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(IsRequestingCode));
        OnPropertyChanged(nameof(IsLoadingGroupTags));
        OnPropertyChanged(nameof(IsBusyIndicatorVisible));
        OnPropertyChanged(nameof(BusyText));
        OnPropertyChanged(nameof(BannerSeverity));
        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(HasBanner));
        OnPropertyChanged(nameof(IsRetryVisible));
        RetryCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (!_isDisposed)
            {
                OnPropertyChanged(nameof(BusyText));
                OnPropertyChanged(nameof(BannerText));
                OnPropertyChanged(nameof(RemainingCaption));
            }
        });
    }
}
