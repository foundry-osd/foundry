// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Authentication;
using System.Text;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.Core.Services.Autopilot;

namespace Foundry.AutopilotInteractive.Tests.Authentication;

public sealed class DeviceCodeClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly DeviceCodeSession Session = new(
        "device-code",
        "ABCD-EFGH",
        new Uri("https://microsoft.com/devicelogin"),
        Now.AddMinutes(15),
        TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Request_ParsesDeviceCodeResponse()
    {
        FakeHttpMessageHandler handler = new((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"device_code":"dc","user_code":"UC-123","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":5,"message":"m"}"""));
        DeviceCodeClient client = CreateClient(handler);

        DeviceCodeSession session = await client.RequestAsync(CancellationToken.None);

        Assert.Equal("dc", session.DeviceCode);
        Assert.Equal("UC-123", session.UserCode);
        Assert.Equal(new Uri("https://microsoft.com/devicelogin"), session.VerificationUri);
        Assert.Equal(Now.AddSeconds(900), session.ExpiresAtUtc);
        Assert.Equal(TimeSpan.FromSeconds(5), session.Interval);

        (Uri? uri, string body) = Assert.Single(handler.Requests);
        Assert.Equal(AutopilotInteractiveAuthentication.DeviceCodeEndpoint, uri?.AbsoluteUri);
        Assert.Contains("client_id=" + AutopilotInteractiveAuthentication.ClientId, body);
        Assert.Contains("scope=openid+profile+DeviceManagementServiceConfig.ReadWrite.All", body);
    }

    [Fact]
    public async Task Request_ClampsIntervalToFiveSeconds()
    {
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"device_code":"dc","user_code":"UC-123","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1}""")));

        DeviceCodeSession session = await client.RequestAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(5), session.Interval);
    }

    [Fact]
    public async Task Request_ThrowsTransient_OnNetworkFailure()
    {
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("offline")));

        DeviceCodeRequestException exception = await Assert.ThrowsAsync<DeviceCodeRequestException>(
            () => client.RequestAsync(CancellationToken.None));

        Assert.True(exception.IsTransient);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task Request_ThrowsBlockingSecureConnection_OnTlsFailure()
    {
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) => throw SecureConnectionFailure()));

        DeviceCodeRequestException exception = await Assert.ThrowsAsync<DeviceCodeRequestException>(
            () => client.RequestAsync(CancellationToken.None));

        Assert.False(exception.IsTransient);
        Assert.Equal(DeviceCodeClient.SecureConnectionErrorCode, exception.ErrorCode);
    }

    [Fact]
    public async Task Request_ThrowsBlocking_OnOAuthError()
    {
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.BadRequest,
            """{"error":"unauthorized_client"}""")));

        DeviceCodeRequestException exception = await Assert.ThrowsAsync<DeviceCodeRequestException>(
            () => client.RequestAsync(CancellationToken.None));

        Assert.False(exception.IsTransient);
        Assert.Equal("unauthorized_client", exception.ErrorCode);
    }

    [Fact]
    public async Task Poll_MapsAuthorizationPendingToPending()
    {
        DeviceCodePollResult result = await PollAsync(HttpStatusCode.BadRequest, """{"error":"authorization_pending"}""");

        Assert.Equal(DeviceCodePollStatus.Pending, result.Status);
    }

    [Fact]
    public async Task Poll_MapsSlowDown()
    {
        DeviceCodePollResult result = await PollAsync(HttpStatusCode.BadRequest, """{"error":"slow_down"}""");

        Assert.Equal(DeviceCodePollStatus.SlowDown, result.Status);
    }

    [Fact]
    public async Task Poll_MapsExpiredToken()
    {
        DeviceCodePollResult result = await PollAsync(HttpStatusCode.BadRequest, """{"error":"expired_token"}""");

        Assert.Equal(DeviceCodePollStatus.Expired, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Poll_MapsServerErrorToTransient(HttpStatusCode status)
    {
        DeviceCodePollResult result = await PollAsync(status, "{}");

        Assert.Equal(DeviceCodePollStatus.TransientFailure, result.Status);
    }

    [Fact]
    public async Task Poll_MapsNetworkFailureToTransient()
    {
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("offline")));

        DeviceCodePollResult result = await client.PollAsync(Session, CancellationToken.None);

        Assert.Equal(DeviceCodePollStatus.TransientFailure, result.Status);
        Assert.IsType<HttpRequestException>(result.TransportFailure);
    }

    [Fact]
    public async Task Poll_MapsTlsFailureToBlockingSecureConnection()
    {
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) => throw SecureConnectionFailure()));

        DeviceCodePollResult result = await client.PollAsync(Session, CancellationToken.None);

        Assert.Equal(DeviceCodePollStatus.Failed, result.Status);
        Assert.Equal(DeviceCodeClient.SecureConnectionErrorCode, result.ErrorCode);
    }

    [Fact]
    public async Task Poll_MapsTimeoutToTransient()
    {
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout")));

        DeviceCodePollResult result = await client.PollAsync(Session, CancellationToken.None);

        Assert.Equal(DeviceCodePollStatus.TransientFailure, result.Status);
    }

    [Fact]
    public async Task Poll_PropagatesCancellation()
    {
        using CancellationTokenSource cts = new();
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PollAsync(Session, cts.Token));
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("authorization_declined")]
    [InlineData("invalid_grant")]
    public async Task Poll_MapsAccessDeniedToFailed(string errorCode)
    {
        DeviceCodePollResult result = await PollAsync(HttpStatusCode.BadRequest, "{\"error\":\"" + errorCode + "\"}");

        Assert.Equal(DeviceCodePollStatus.Failed, result.Status);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public async Task Poll_MapsUnparseableClientErrorToFailed()
    {
        DeviceCodePollResult result = await PollAsync(HttpStatusCode.Forbidden, "not json");

        Assert.Equal(DeviceCodePollStatus.Failed, result.Status);
        Assert.Equal("http_403", result.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Bad Request</html>")]
    public async Task Poll_MapsBareBadRequestToFailed(string body)
    {
        DeviceCodePollResult result = await PollAsync(HttpStatusCode.BadRequest, body);

        Assert.Equal(DeviceCodePollStatus.Failed, result.Status);
        Assert.Equal("http_400", result.ErrorCode);
    }

    [Fact]
    public async Task Poll_ReadsAccountFromIdToken()
    {
        string idToken = CreateIdToken("""{"preferred_username":"admin@contoso.com"}""");
        FakeHttpMessageHandler handler = new((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"access_token\":\"at\",\"expires_in\":3600,\"id_token\":\"" + idToken + "\"}"));
        DeviceCodeClient client = CreateClient(handler);

        DeviceCodePollResult result = await client.PollAsync(Session, CancellationToken.None);

        Assert.Equal(DeviceCodePollStatus.Succeeded, result.Status);
        Assert.NotNull(result.Session);
        Assert.Equal("at", result.Session.AccessToken);
        Assert.Equal(Now.AddSeconds(3600), result.Session.ExpiresAtUtc);
        Assert.Equal("admin@contoso.com", result.Session.AccountName);

        (Uri? uri, string body) = Assert.Single(handler.Requests);
        Assert.Equal(AutopilotInteractiveAuthentication.TokenEndpoint, uri?.AbsoluteUri);
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", body);
        Assert.Contains("device_code=device-code", body);
        Assert.Contains("client_id=" + AutopilotInteractiveAuthentication.ClientId, body);
    }

    [Fact]
    public async Task Poll_MapsSuccessWithoutAccessTokenToFailed()
    {
        DeviceCodePollResult result = await PollAsync(HttpStatusCode.OK, "{}");

        Assert.Equal(DeviceCodePollStatus.Failed, result.Status);
        Assert.Equal("invalid_response", result.ErrorCode);
    }

    private static Task<DeviceCodePollResult> PollAsync(HttpStatusCode status, string json)
    {
        DeviceCodeClient client = CreateClient(new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(status, json)));
        return client.PollAsync(Session, CancellationToken.None);
    }

    private static HttpRequestException SecureConnectionFailure() =>
        new(HttpRequestError.SecureConnectionError, "The SSL connection could not be established.", new AuthenticationException());

    private static DeviceCodeClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), new FixedTimeProvider(Now));

    private static string CreateIdToken(string payloadJson)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return Encode("""{"alg":"none"}""") + "." + Encode(payloadJson) + ".signature";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
