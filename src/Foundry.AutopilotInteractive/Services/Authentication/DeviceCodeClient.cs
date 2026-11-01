// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Http;

namespace Foundry.AutopilotInteractive.Services.Authentication;

/// <summary>
/// A device code issued by Microsoft Entra ID, waiting for the technician to sign in on another device.
/// </summary>
/// <param name="DeviceCode">Opaque code the application polls with. Secret: never log or display it.</param>
/// <param name="UserCode">Short code the technician types (or scans) at the verification page.</param>
/// <param name="VerificationUri">Page where the technician enters the user code.</param>
/// <param name="ExpiresAtUtc">Instant after which the code is no longer accepted.</param>
/// <param name="Interval">Minimum delay between two polls.</param>
public sealed record DeviceCodeSession(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    DateTimeOffset ExpiresAtUtc,
    TimeSpan Interval);

/// <summary>
/// Result of a successful sign-in.
/// </summary>
/// <param name="AccessToken">Graph access token. Secret: never log it.</param>
/// <param name="ExpiresAtUtc">Instant after which the access token is no longer valid.</param>
/// <param name="AccountName">Account shown to the technician, or <see langword="null"/> when the id_token has none.</param>
public sealed record AuthenticatedSession(string AccessToken, DateTimeOffset ExpiresAtUtc, string? AccountName);

/// <summary>
/// Outcome of one poll of the token endpoint.
/// </summary>
public enum DeviceCodePollStatus
{
    /// <summary>The technician has not completed the sign-in yet.</summary>
    Pending,

    /// <summary>The endpoint asks the application to poll less often.</summary>
    SlowDown,

    /// <summary>The device code expired; a new one is required.</summary>
    Expired,

    /// <summary>The poll failed for a reason that may go away on retry (network, timeout, throttling, server error).</summary>
    TransientFailure,

    /// <summary>The technician signed in; <see cref="DeviceCodePollResult.Session"/> is set.</summary>
    Succeeded,

    /// <summary>The sign-in cannot succeed (denied, declined, invalid request); <see cref="DeviceCodePollResult.ErrorCode"/> is set.</summary>
    Failed
}

/// <summary>
/// Classified result of one poll.
/// </summary>
/// <param name="Status">Classified outcome.</param>
/// <param name="Session">Signed-in session, only for <see cref="DeviceCodePollStatus.Succeeded"/>.</param>
/// <param name="ErrorCode">
/// OAuth error code (or <see cref="DeviceCodeClient.SecureConnectionErrorCode"/>) for <see cref="DeviceCodePollStatus.Failed"/>;
/// for a <see cref="DeviceCodePollStatus.TransientFailure"/> caused by an HTTP status, the OAuth error code or
/// <c>http_&lt;status&gt;</c>.
/// </param>
/// <param name="TransportFailure">
/// The exception of a request that got no HTTP answer, for diagnostics: only its type and HResult may be logged, never its
/// message, which could echo request content.
/// </param>
public sealed record DeviceCodePollResult(
    DeviceCodePollStatus Status,
    AuthenticatedSession? Session = null,
    string? ErrorCode = null,
    Exception? TransportFailure = null);

/// <summary>
/// The device code request failed.
/// </summary>
/// <param name="isTransient"><see langword="true"/> when retrying later may succeed (network, timeout, throttling, 5xx).</param>
/// <param name="errorCode">
/// OAuth error code returned by the endpoint, <c>http_&lt;status&gt;</c>, or <see cref="DeviceCodeClient.SecureConnectionErrorCode"/>.
/// </param>
/// <param name="innerException">Transport failure when no HTTP answer was received; only its type and HResult may be logged.</param>
public sealed class DeviceCodeRequestException(bool isTransient, string? errorCode, Exception? innerException = null)
    : Exception("The device code request failed.", innerException)
{
    /// <summary>Gets a value indicating whether retrying later may succeed.</summary>
    public bool IsTransient { get; } = isTransient;

    /// <summary>Gets the OAuth error code returned by the endpoint, if any.</summary>
    public string? ErrorCode { get; } = errorCode;
}

/// <summary>
/// Talks to the Microsoft Entra device code and token endpoints.
/// </summary>
public interface IDeviceCodeClient
{
    /// <summary>
    /// Requests a new device code.
    /// </summary>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The issued device code.</returns>
    /// <exception cref="DeviceCodeRequestException">The request failed; <see cref="DeviceCodeRequestException.IsTransient"/> tells whether to retry.</exception>
    Task<DeviceCodeSession> RequestAsync(CancellationToken ct);

    /// <summary>
    /// Polls the token endpoint once. Failures are classified, not thrown.
    /// </summary>
    /// <param name="session">Device code to poll with.</param>
    /// <param name="ct">Cancels the poll.</param>
    /// <returns>The classified poll result.</returns>
    Task<DeviceCodePollResult> PollAsync(DeviceCodeSession session, CancellationToken ct);
}

/// <summary>
/// <see cref="IDeviceCodeClient"/> over <see cref="HttpClient"/>, using the endpoints and client identifier from
/// <see cref="AutopilotInteractiveAuthentication"/>.
/// </summary>
/// <remarks>
/// Entra answers pending, slow-down, expiry and denial as HTTP 400 with an <c>error</c> member, so classification looks
/// at the status code first (transient statuses) and then at the OAuth error code. A TLS handshake failure is blocking,
/// not transient: a proxy that inspects HTTPS or a wrong clock does not fix itself, so waiting for the network would
/// hide it. Nothing is logged here: requests and responses carry the device code and tokens.
/// </remarks>
public sealed class DeviceCodeClient : IDeviceCodeClient
{
    /// <summary>Error code of a sign-in that failed because no secure connection to Microsoft could be established.</summary>
    public const string SecureConnectionErrorCode = "secure_connection";

    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";
    private const int DefaultIntervalSeconds = 5;

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    /// <param name="httpClient">Client used for both endpoints; its timeout bounds each call.</param>
    /// <param name="timeProvider">Clock used to turn <c>expires_in</c> into an absolute instant; defaults to the system clock.</param>
    public DeviceCodeClient(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<DeviceCodeSession> RequestAsync(CancellationToken ct)
    {
        Dictionary<string, string> form = new()
        {
            ["client_id"] = AutopilotInteractiveAuthentication.ClientId,
            ["scope"] = AutopilotInteractiveAuthentication.Scopes
        };

        (HttpStatusCode status, JsonElement? body) response;
        try
        {
            response = await PostFormAsync(AutopilotInteractiveAuthentication.DeviceCodeEndpoint, form, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportFailure(exception, ct))
        {
            throw HttpConnectionFailure.IsSecureConnectionFailure(exception)
                ? new DeviceCodeRequestException(isTransient: false, SecureConnectionErrorCode, exception)
                : new DeviceCodeRequestException(isTransient: true, errorCode: null, exception);
        }

        if (response.status != HttpStatusCode.OK)
        {
            throw new DeviceCodeRequestException(IsTransientStatus(response.status), ReadErrorCode(response.body, response.status));
        }

        JsonElement? json = response.body;
        string? deviceCode = ReadString(json, "device_code");
        string? userCode = ReadString(json, "user_code");
        string? verificationUri = ReadString(json, "verification_uri");
        if (deviceCode is null
            || userCode is null
            || !Uri.TryCreate(verificationUri, UriKind.Absolute, out Uri? uri)
            || ReadInt(json, "expires_in") is not int expiresIn)
        {
            throw new DeviceCodeRequestException(isTransient: false, errorCode: "invalid_response");
        }

        // Floor the polling interval: a server value below 5 s would hammer the endpoint and trigger slow_down.
        int interval = Math.Max(DefaultIntervalSeconds, ReadInt(json, "interval") ?? DefaultIntervalSeconds);
        return new DeviceCodeSession(
            deviceCode,
            userCode,
            uri,
            _timeProvider.GetUtcNow().AddSeconds(expiresIn),
            TimeSpan.FromSeconds(interval));
    }

    /// <inheritdoc />
    public async Task<DeviceCodePollResult> PollAsync(DeviceCodeSession session, CancellationToken ct)
    {
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = DeviceCodeGrantType,
            ["client_id"] = AutopilotInteractiveAuthentication.ClientId,
            ["device_code"] = session.DeviceCode
        };

        (HttpStatusCode status, JsonElement? body) response;
        try
        {
            response = await PostFormAsync(AutopilotInteractiveAuthentication.TokenEndpoint, form, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportFailure(exception, ct))
        {
            return HttpConnectionFailure.IsSecureConnectionFailure(exception)
                ? new DeviceCodePollResult(DeviceCodePollStatus.Failed, ErrorCode: SecureConnectionErrorCode, TransportFailure: exception)
                : new DeviceCodePollResult(DeviceCodePollStatus.TransientFailure, TransportFailure: exception);
        }

        if (response.status == HttpStatusCode.OK)
        {
            return ReadSuccess(response.body);
        }

        string errorCode = ReadErrorCode(response.body, response.status);
        if (IsTransientStatus(response.status))
        {
            return new DeviceCodePollResult(DeviceCodePollStatus.TransientFailure, ErrorCode: errorCode);
        }

        return errorCode switch
        {
            "authorization_pending" => new DeviceCodePollResult(DeviceCodePollStatus.Pending),
            "slow_down" => new DeviceCodePollResult(DeviceCodePollStatus.SlowDown),
            "expired_token" => new DeviceCodePollResult(DeviceCodePollStatus.Expired),
            _ => new DeviceCodePollResult(DeviceCodePollStatus.Failed, ErrorCode: errorCode)
        };
    }

    private DeviceCodePollResult ReadSuccess(JsonElement? body)
    {
        string? accessToken = ReadString(body, "access_token");
        if (accessToken is null || ReadInt(body, "expires_in") is not int expiresIn)
        {
            return new DeviceCodePollResult(DeviceCodePollStatus.Failed, ErrorCode: "invalid_response");
        }

        AuthenticatedSession session = new(
            accessToken,
            _timeProvider.GetUtcNow().AddSeconds(expiresIn),
            IdTokenAccountReader.Read(ReadString(body, "id_token")));
        return new DeviceCodePollResult(DeviceCodePollStatus.Succeeded, session);
    }

    private async Task<(HttpStatusCode Status, JsonElement? Body)> PostFormAsync(
        string endpoint,
        Dictionary<string, string> form,
        CancellationToken ct)
    {
        using FormUrlEncodedContent content = new(form);
        using HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (response.StatusCode, TryParseObject(text));
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken ct) =>
        !ct.IsCancellationRequested && exception is HttpRequestException or TaskCanceledException or IOException;

    private static bool IsTransientStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static string ReadErrorCode(JsonElement? body, HttpStatusCode status) =>
        ReadString(body, "error") ?? "http_" + (int)status;

    private static JsonElement? TryParseObject(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement? body, string name) =>
        body is { } element
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement? body, string name)
    {
        if (body is not { } element || !element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        // Entra documents numbers, but some proxies serialize them as strings.
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int number) => number,
            _ => null
        };
    }
}
