// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.AutopilotInteractive.Services.Authentication;

/// <summary>
/// What the sign-in screen shows while <see cref="SignInFlow"/> runs.
/// </summary>
public enum SignInPhase
{
    /// <summary>A device code is being requested; no code is available yet.</summary>
    RequestingCode,

    /// <summary>The code is displayed and the technician has not finished signing in.</summary>
    WaitingForUser,

    /// <summary>The network is unreachable; the flow keeps retrying.</summary>
    WaitingForNetwork,

    /// <summary>The sign-in cannot continue; <see cref="SignInState.ErrorCode"/> is set.</summary>
    Failed
}

/// <summary>
/// Progress report of <see cref="SignInFlow"/>.
/// </summary>
/// <param name="Phase">Current phase.</param>
/// <param name="Code">Device code to display; <see langword="null"/> while a new one is being requested.</param>
/// <param name="ErrorCode">OAuth error code, only for <see cref="SignInPhase.Failed"/>.</param>
public sealed record SignInState(SignInPhase Phase, DeviceCodeSession? Code, string? ErrorCode);

/// <summary>
/// The sign-in cannot succeed (access denied, declined, invalid request) or failed unexpectedly.
/// </summary>
public sealed class SignInFailedException : Exception
{
    /// <param name="errorCode">Error code that ended the sign-in.</param>
    public SignInFailedException(string errorCode)
        : this(errorCode, null)
    {
    }

    /// <param name="errorCode">Error code that ended the sign-in.</param>
    /// <param name="innerException">Unexpected failure that ended the sign-in, if any.</param>
    public SignInFailedException(string errorCode, Exception? innerException)
        : base("Sign-in failed: " + errorCode, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets the error code that ended the sign-in: an OAuth error code or <c>unexpected_error</c>.</summary>
    public string ErrorCode { get; }
}

/// <summary>
/// Runs the device code sign-in: requests a code, polls until the technician signs in, and renews the code when it
/// expires.
/// </summary>
/// <remarks>
/// Time is injected (<c>delay</c> and <c>utcNow</c>) so tests run without waiting. The flow ends only when the technician
/// signs in, a blocking error occurs (<see cref="SignInFailedException"/>) or <c>ct</c> is cancelled (the
/// <see cref="OperationCanceledException"/> propagates and no failure is reported). Network problems never end it: they are
/// reported as <see cref="SignInPhase.WaitingForNetwork"/> and retried; the first failure of each code request or polling
/// series logs one warning with the failure type and HResult, and its recovery logs one information entry. A TLS failure
/// is blocking (<see cref="DeviceCodeClient.SecureConnectionErrorCode"/>). Neither the user code, the device code, the
/// tokens nor exception messages are logged.
/// </remarks>
public sealed class SignInFlow
{
    private static readonly TimeSpan SlowDownIncrement = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private const string UnexpectedErrorCode = "unexpected_error";

    private readonly IDeviceCodeClient _client;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly ILogger _logger;

    /// <param name="client">Talks to the device code and token endpoints.</param>
    /// <param name="delay">Waits for the given duration; replaced in tests.</param>
    /// <param name="utcNow">Current UTC time; replaced in tests.</param>
    /// <param name="logger">Receives one start and one terminal outcome entry; optional.</param>
    public SignInFlow(
        IDeviceCodeClient client,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<DateTimeOffset> utcNow,
        ILogger<SignInFlow>? logger = null)
    {
        _client = client;
        _delay = delay;
        _utcNow = utcNow;
        _logger = logger ?? NullLogger<SignInFlow>.Instance;
    }

    /// <summary>
    /// Signs the technician in.
    /// </summary>
    /// <param name="progress">Receives the state to display; reports are made from the calling flow of control.</param>
    /// <param name="ct">Cancels the sign-in.</param>
    /// <returns>The authenticated session.</returns>
    /// <exception cref="SignInFailedException">A blocking or unexpected error ended the sign-in.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<AuthenticatedSession> RunAsync(IProgress<SignInState> progress, CancellationToken ct)
    {
        _logger.LogInformation("Sign-in started");
        try
        {
            AuthenticatedSession session = await RunCoreAsync(progress, ct).ConfigureAwait(false);
            _logger.LogInformation("Sign-in completed");
            return session;
        }
        catch (SignInFailedException exception)
        {
            // Only the failure types and HResult are logged: transport messages could echo request content.
            _logger.LogError(
                "Sign-in failed. ErrorCode={ErrorCode}, FailureType={FailureType}, HResult={HResult}, InnerFailureType={InnerFailureType}",
                exception.ErrorCode,
                exception.InnerException?.GetType().Name,
                exception.InnerException?.HResult,
                exception.InnerException?.InnerException?.GetType().Name);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sign-in cancelled");
            throw;
        }
        catch (Exception exception)
        {
            // Only the exception type is logged: messages from HTTP or JSON failures could echo request content.
            _logger.LogError("Sign-in failed. ErrorCode={ErrorCode}, ExceptionType={ExceptionType}", UnexpectedErrorCode, exception.GetType().Name);
            progress.Report(new SignInState(SignInPhase.Failed, null, UnexpectedErrorCode));
            throw new SignInFailedException(UnexpectedErrorCode, exception);
        }
    }

    private async Task<AuthenticatedSession> RunCoreAsync(IProgress<SignInState> progress, CancellationToken ct)
    {
        while (true)
        {
            progress.Report(new SignInState(SignInPhase.RequestingCode, null, null));
            DeviceCodeSession code = await RequestCodeAsync(progress, ct).ConfigureAwait(false);
            progress.Report(new SignInState(SignInPhase.WaitingForUser, code, null));

            AuthenticatedSession? session = await PollUntilEndAsync(code, progress, ct).ConfigureAwait(false);
            if (session is not null)
            {
                return session;
            }

            // The code expired: loop to request a new one.
        }
    }

    private async Task<DeviceCodeSession> RequestCodeAsync(IProgress<SignInState> progress, CancellationToken ct)
    {
        bool failureLogged = false;
        while (true)
        {
            try
            {
                DeviceCodeSession code = await _client.RequestAsync(ct).ConfigureAwait(false);
                if (failureLogged)
                {
                    _logger.LogInformation("Device code request recovered after a transient failure");
                }

                return code;
            }
            catch (DeviceCodeRequestException exception) when (exception.IsTransient)
            {
                if (!failureLogged)
                {
                    LogTransientFailure("Device code request", exception.InnerException, exception.ErrorCode);
                    failureLogged = true;
                }

                progress.Report(new SignInState(SignInPhase.WaitingForNetwork, null, null));
                await _delay(RetryDelay, ct).ConfigureAwait(false);
            }
            catch (DeviceCodeRequestException exception)
            {
                throw Fail(progress, exception.ErrorCode ?? "request_failed", exception.InnerException);
            }
        }
    }

    /// <returns>The session on success, or <see langword="null"/> when the code expired.</returns>
    private async Task<AuthenticatedSession?> PollUntilEndAsync(
        DeviceCodeSession code,
        IProgress<SignInState> progress,
        CancellationToken ct)
    {
        TimeSpan interval = code.Interval;
        TimeSpan nextDelay = interval;
        bool networkReported = false;

        while (true)
        {
            // Never wait past the expiry, so the renewal starts exactly when the code dies.
            TimeSpan remaining = code.ExpiresAtUtc - _utcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            await _delay(nextDelay < remaining ? nextDelay : remaining, ct).ConfigureAwait(false);
            if (_utcNow() >= code.ExpiresAtUtc)
            {
                return null;
            }

            DeviceCodePollResult result = await _client.PollAsync(code, ct).ConfigureAwait(false);
            if (result.Status == DeviceCodePollStatus.TransientFailure)
            {
                if (!networkReported)
                {
                    LogTransientFailure("Token polling", result.TransportFailure, result.ErrorCode);
                    progress.Report(new SignInState(SignInPhase.WaitingForNetwork, code, null));
                    networkReported = true;
                }

                nextDelay = RetryDelay;
                continue;
            }

            if (networkReported)
            {
                _logger.LogInformation("Token polling recovered after a transient failure");
                progress.Report(new SignInState(SignInPhase.WaitingForUser, code, null));
                networkReported = false;
            }

            nextDelay = interval;
            switch (result.Status)
            {
                case DeviceCodePollStatus.Succeeded when result.Session is not null:
                    return result.Session;
                case DeviceCodePollStatus.Expired:
                    return null;
                case DeviceCodePollStatus.SlowDown:
                    interval += SlowDownIncrement;
                    nextDelay = interval;
                    break;
                case DeviceCodePollStatus.Failed:
                    throw Fail(progress, result.ErrorCode ?? "unknown_error", result.TransportFailure);
                case DeviceCodePollStatus.Succeeded:
                    throw Fail(progress, "invalid_response");
            }
        }
    }

    private static SignInFailedException Fail(IProgress<SignInState> progress, string errorCode, Exception? cause = null)
    {
        progress.Report(new SignInState(SignInPhase.Failed, null, errorCode));
        return new SignInFailedException(errorCode, cause);
    }

    // Logged once per series: at specialize this is the only trace of a network or proxy problem.
    private void LogTransientFailure(string operation, Exception? failure, string? errorCode)
    {
        _logger.LogWarning(
            "{Operation} failed; retrying until the network recovers. FailureType={FailureType}, HResult={HResult}, InnerFailureType={InnerFailureType}, ErrorCode={ErrorCode}",
            operation,
            failure?.GetType().Name,
            failure?.HResult,
            failure?.InnerException?.GetType().Name,
            errorCode);
    }
}
