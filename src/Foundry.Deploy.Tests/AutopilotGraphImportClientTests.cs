// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotGraphImportClientTests
{
    private static AutopilotGraphImportRequest Request => new("secret-token", "SERIAL", "aGFzaA==", "New", null, "request-current");
    private const string CompleteImport = """{"value":[{"id":"imported-current","importId":"request-current","serialNumber":"SERIAL","hardwareIdentifier":"aGFzaA==","state":{"deviceImportStatus":"complete","deviceRegistrationId":"registration-current"}}]}""";

    [Fact]
    public async Task ImportHardwareHashAsync_ReportsRequestDeadlineSeparately()
    {
        using var handler = new DelegateHandler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException(); });
        var client = CreateClient(handler, Limits() with { RequestTimeout = TimeSpan.FromMilliseconds(30) });
        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(Request, TestContext.Current.CancellationToken);
        Assert.Equal("RequestTimedOut", result.FailureCode);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_PreservesCallerCancellation()
    {
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        using var handler = new DelegateHandler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler, Limits()).ImportHardwareHashAsync(Request, cancelled.Token));
    }

    [Fact]
    public async Task ImportHardwareHashAsync_GroupTagConfirmationSharesWorkflowDeadline()
    {
        int updates = 0;
        using var handler = new DelegateHandler(async (request, token) =>
        {
            await Task.Delay(5, token);
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/import", StringComparison.Ordinal)) return Json(CompleteImport);
            if (path.EndsWith("/updateDeviceProperties", StringComparison.Ordinal)) { updates++; return new(HttpStatusCode.NoContent); }
            return Json("""{"id":"registration-current","serialNumber":"SERIAL","groupTag":"Old"}""");
        });
        AutopilotHardwareHashUploadResult result = await CreateClient(handler, Limits() with { WorkflowTimeout = TimeSpan.FromMilliseconds(100) })
            .ImportHardwareHashAsync(Request, TestContext.Current.CancellationToken);
        Assert.Equal("WorkflowTimedOut", result.FailureCode);
        Assert.Equal(1, updates);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_RetriesExplicitThrottlingAndPreservesPayload()
    {
        int imports = 0;
        using var handler = new DelegateHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/import", StringComparison.Ordinal))
                return Task.FromResult(++imports == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Json(CompleteImport));
            return Task.FromResult(Json("""{"id":"registration-current","serialNumber":"SERIAL","groupTag":"New"}"""));
        });
        Assert.True((await CreateClient(handler, Limits() with { RetryCount = 1 }).ImportHardwareHashAsync(Request, TestContext.Current.CancellationToken)).IsCompleted);
        Assert.Equal(2, imports);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ImportHardwareHashAsync_UnconfirmedMutationDoesNotRetryOrExposeBody(HttpStatusCode status)
    {
        using var handler = new DelegateHandler((_, _) => Task.FromResult(Json("private-token-and-tenant-details", status)));
        HttpRequestException failure = await Assert.ThrowsAnyAsync<HttpRequestException>(() => CreateClient(handler, Limits() with { RetryCount = 2 })
            .ImportHardwareHashAsync(Request, TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Count);
        Assert.DoesNotContain("private-token", failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://attacker.invalid/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?page=2")]
    [InlineData("http://graph.microsoft.com/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?page=2")]
    [InlineData("https://graph.microsoft.com/v1.0/users")]
    [InlineData("https://graph.microsoft.com/v1.0/deviceManagement/windowsAutopilotDeviceIdentities")]
    public async Task ListGroupTagsAsync_RejectsForeignOrCyclicLinksBeforeSending(string nextLink)
    {
        using var handler = new DelegateHandler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(new Dictionary<string, object> { ["value"] = Array.Empty<object>(), ["@odata.nextLink"] = nextLink }))));
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateClient(handler).ListGroupTagsAsync("secret-token", TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task ListGroupTagsAsync_BoundsPagesAndHonorsRetryAfter()
    {
        int calls = 0;
        using var handler = new DelegateHandler((_, _) =>
        {
            if (++calls == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(50));
                return Task.FromResult(response);
            }
            return Task.FromResult(Json("""{"value":[{"groupTag":" New "}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$skiptoken=second"}"""));
        });
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateClient(handler, Limits() with { RetryCount = 1, MaximumGroupTagPages = 1 })
            .ListGroupTagsAsync("secret-token", TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public async Task ListGroupTagsAsync_ReadsDistinctTagsAcrossBoundedPages()
    {
        int calls = 0;
        using var handler = new DelegateHandler((_, _) => Task.FromResult(Json(++calls == 1
            ? """{"value":[{"groupTag":" A "}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$skiptoken=second"}"""
            : """{"value":[{"groupTag":"a"},{"groupTag":"B"}]}""")));
        Assert.Equal(new[] { "A", "B" }, await CreateClient(handler).ListGroupTagsAsync("secret-token", TestContext.Current.CancellationToken));
    }

    private static AutopilotGraphImportClientOptions Limits() => new()
    {
        RetryCount = 0,
        RetryDelay = TimeSpan.Zero,
        PollInterval = TimeSpan.Zero,
        RequestTimeout = TimeSpan.FromSeconds(1),
        WorkflowTimeout = TimeSpan.FromSeconds(2),
        VisibilityTimeout = TimeSpan.FromSeconds(1)
    };
    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json) };
    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Count++; return send(request, token); }
    }

    [Fact]
    public async Task ImportHardwareHashAsync_RejectsOversizedMetadataBeforeReading()
    {
        using var handler = new DelegateHandler((_, _) =>
        {
            HttpResponseMessage response = Json(CompleteImport);
            response.Content.Headers.ContentLength = 32L * 1024 * 1024 + 1;
            return Task.FromResult(response);
        });
        Assert.Equal("IdentityUnconfirmed", (await CreateClient(handler).ImportHardwareHashAsync(Request, TestContext.Current.CancellationToken)).FailureCode);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_DelayedVisibilityHasProgressWithinPolls()
    {
        var reports = new List<AutopilotHardwareHashUploadProgress>();
        using var handler = new DelegateHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/import", StringComparison.Ordinal)
            ? Json(CompleteImport) : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = CreateClient(handler, Limits() with { VisibilityTimeout = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(70), ProgressInterval = TimeSpan.FromMilliseconds(10) });
        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(Request, new InlineProgress(reports), TestContext.Current.CancellationToken);
        Assert.Equal("AutopilotDeviceTimedOut", result.FailureCode);
        Assert.True(reports.Count > handler.Count);
    }

    private sealed class InlineProgress(List<AutopilotHardwareHashUploadProgress> reports) : IProgress<AutopilotHardwareHashUploadProgress>
    {
        public void Report(AutopilotHardwareHashUploadProgress value) => reports.Add(value);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_VisibilityDeadlinePreventsLatePropertyMutation()
    {
        int updates = 0;
        using var handler = new DelegateHandler(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/import", StringComparison.Ordinal)) return Json(CompleteImport);
            if (request.Method == HttpMethod.Post) { updates++; return new(HttpStatusCode.NoContent); }
            await Task.Delay(80, TestContext.Current.CancellationToken);
            return Json("""{"id":"registration-current","serialNumber":"SERIAL","groupTag":"Old"}""");
        });
        AutopilotHardwareHashUploadResult result = await CreateClient(handler, Limits() with { VisibilityTimeout = TimeSpan.FromMilliseconds(20) })
            .ImportHardwareHashAsync(Request, TestContext.Current.CancellationToken);
        Assert.Equal("AutopilotDeviceTimedOut", result.FailureCode);
        Assert.Equal(0, updates);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_PendingImportDoesNotConsumeRegistrationVisibilityBudget()
    {
        using var handler = new DelegateHandler(async (request, token) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/import", StringComparison.Ordinal)) return Json(CompleteImport.Replace("complete", "pending", StringComparison.Ordinal));
            if (path.Contains("/importedWindowsAutopilotDeviceIdentities/", StringComparison.Ordinal))
            {
                await Task.Delay(60, token);
                using JsonDocument complete = JsonDocument.Parse(CompleteImport);
                return Json(complete.RootElement.GetProperty("value")[0].GetRawText());
            }
            return Json("""{"id":"registration-current","serialNumber":"SERIAL","groupTag":"New"}""");
        });
        Assert.True((await CreateClient(handler, Limits() with { VisibilityTimeout = TimeSpan.FromMilliseconds(30) })
            .ImportHardwareHashAsync(Request, TestContext.Current.CancellationToken)).IsCompleted);
    }

    [Fact]
    public async Task ListGroupTagsAsync_RetriesTransientReadFailure()
    {
        int count = 0;
        using var handler = new DelegateHandler((_, _) => Task.FromResult(++count == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json("""{"value":[]}""")));
        Assert.Empty(await CreateClient(handler, Limits() with { RetryCount = 1 }).ListGroupTagsAsync("token", TestContext.Current.CancellationToken));
        Assert.Equal(2, count);
    }

    public static IEnumerable<object[]> ProtocolFixtures => Directory.EnumerateFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Autopilot"), "*.json").Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(ProtocolFixtures))]
    public async Task ImportHardwareHashAsync_FollowsAuthoritativeProtocolFixture(string fixturePath)
    {
        using JsonDocument fixture = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath, TestContext.Current.CancellationToken));
        using var handler = new FixtureHandler(fixture.RootElement);
        var client = CreateClient(handler);
        JsonElement input = fixture.RootElement.GetProperty("request");
        var request = new AutopilotGraphImportRequest("secret-access-token", input.GetProperty("serialNumber").GetString()!,
            input.GetProperty("hardwareIdentifier").GetString()!, input.GetProperty("groupTag").GetString(), null,
            input.GetProperty("importId").GetString()!);
        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(request, TestContext.Current.CancellationToken);
        JsonElement expected = fixture.RootElement.GetProperty("expected");
        Assert.Equal(expected.GetProperty("status").GetString() == "completed", result.IsCompleted);
        Assert.Equal(expected.GetProperty("code").GetString(), result.FailureCode);
        Assert.Equal(expected.GetProperty("propertyUpdateCount").GetInt32(), handler.UpdateCount);
        Assert.Equal(expected.GetProperty("registrationId").GetString(), result.AutopilotDeviceId);
        Assert.DoesNotContain(handler.Paths, path => path.Contains("?", StringComparison.Ordinal));
        using JsonDocument body = JsonDocument.Parse(handler.ImportBody!);
        JsonElement submitted = body.RootElement.GetProperty("importedWindowsAutopilotDeviceIdentities")[0];
        Assert.Equal(request.HardwareIdentifier, submitted.GetProperty("hardwareIdentifier").GetString());
        Assert.Equal(request.ImportId, submitted.GetProperty("importId").GetString());
        Assert.DoesNotContain(request.AccessToken, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private static AutopilotGraphImportClient CreateClient(HttpMessageHandler handler, AutopilotGraphImportClientOptions? options = null) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") },
        NullLogger<AutopilotGraphImportClient>.Instance,
        options ?? new AutopilotGraphImportClientOptions { RetryCount = 0, RetryDelay = TimeSpan.Zero, PollInterval = TimeSpan.Zero, VisibilityTimeout = TimeSpan.FromMilliseconds(200) });

    private sealed class FixtureHandler(JsonElement fixture) : HttpMessageHandler
    {
        private int _poll;
        private int _device;
        public int UpdateCount { get; private set; }
        public List<string> Paths { get; } = [];
        public string? ImportBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            if (path.EndsWith("/import", StringComparison.Ordinal))
            {
                ImportBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(HttpStatusCode.OK, fixture.GetProperty("importResponse"));
            }
            if (path.EndsWith("/updateDeviceProperties", StringComparison.Ordinal))
            {
                Assert.Contains("/registration-current/", path, StringComparison.Ordinal);
                UpdateCount++;
                return new(HttpStatusCode.NoContent);
            }
            if (path.Contains("/importedWindowsAutopilotDeviceIdentities/", StringComparison.Ordinal))
            {
                Assert.EndsWith("/imported-current", path, StringComparison.Ordinal);
                JsonElement polls = fixture.GetProperty("importedPolls");
                return Json(HttpStatusCode.OK, polls.GetArrayLength() == 0 ? fixture.GetProperty("importResponse").GetProperty("value")[0]
                    : polls[Math.Min(_poll++, polls.GetArrayLength() - 1)]);
            }
            if (path.Contains("/windowsAutopilotDeviceIdentities/", StringComparison.Ordinal))
            {
                Assert.EndsWith("/registration-current", path, StringComparison.Ordinal);
                JsonElement devices = fixture.GetProperty("deviceResponses");
                JsonElement response = devices[Math.Min(_device++, devices.GetArrayLength() - 1)];
                return Json((HttpStatusCode)response.GetProperty("statusCode").GetInt32(), response.GetProperty("body"));
            }
            // Legacy lookup responses expose a stale matching serial so the regression cannot pass by absence.
            if (path.Contains("/windowsAutopilotDeviceIdentities", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[" + fixture.GetProperty("unrelatedDevice").GetRawText() + "]}") };
            return Json(HttpStatusCode.OK, fixture.GetProperty("importResponse"));
        }
        private static HttpResponseMessage Json(HttpStatusCode status, JsonElement value) => new(status) { Content = new StringContent(value.GetRawText()) };
    }
}
