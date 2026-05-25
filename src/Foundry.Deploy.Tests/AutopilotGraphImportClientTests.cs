using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotGraphImportClientTests
{
    [Fact]
    public async Task ImportHardwareHashAsync_SerializesGraphImportPayload()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "imported-id",
                  "serialNumber": "SER123",
                  "importId": "import-123",
                  "state": { "deviceImportStatus": "complete" }
                }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id", "serialNumber": "SER123" }
              ]
            }
            """);

        AutopilotGraphImportClient client = CreateClient(handler);

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                "Sales",
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.Completed, result.State);
        RecordedGraphRequest importRequest = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, importRequest.Method);
        Assert.Equal("/v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities/import", importRequest.PathAndQuery);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token").ToString(), importRequest.Authorization);

        using JsonDocument body = JsonDocument.Parse(importRequest.Body!);
        JsonElement importedDevice = body.RootElement
            .GetProperty("importedWindowsAutopilotDeviceIdentities")[0];
        Assert.Equal("#microsoft.graph.importedWindowsAutopilotDeviceIdentity", importedDevice.GetProperty("@odata.type").GetString());
        Assert.Equal("SER123", importedDevice.GetProperty("serialNumber").GetString());
        Assert.Equal("aGFyZHdhcmVIYXNo", importedDevice.GetProperty("hardwareIdentifier").GetString());
        Assert.Equal("Sales", importedDevice.GetProperty("groupTag").GetString());
        Assert.Equal("import-123", importedDevice.GetProperty("importId").GetString());
        Assert.DoesNotContain("access-token", importRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("pfx", importRequest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", importRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenTransientGraphFailureOccurs_RetriesAndSucceeds()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueText(HttpStatusCode.ServiceUnavailable, "temporary");
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "imported-id",
                  "serialNumber": "SER123",
                  "importId": "import-123",
                  "state": { "deviceImportStatus": "complete" }
                }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """
            { "value": [ { "id": "device-id", "serialNumber": "SER123" } ] }
            """);

        AutopilotGraphImportClient client = CreateClient(handler);

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                null,
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.Completed, result.State);
        Assert.Equal(2, handler.Requests.Count(request =>
            request.Method == HttpMethod.Post &&
            request.PathAndQuery == "/v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities/import"));
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenGraphValidationFails_DoesNotRetry()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.BadRequest, """
            {
              "error": {
                "code": "BadRequest",
                "message": "Invalid hardware identifier."
              }
            }
            """);

        AutopilotGraphImportClient client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "invalid",
                null,
                null,
                "import-123"),
            CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenImportFails_ReturnsNonBlockingUploadFailure()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "imported-id",
                  "serialNumber": "SER123",
                  "importId": "import-123",
                  "state": {
                    "deviceImportStatus": "error",
                    "deviceErrorCode": 806,
                    "deviceErrorName": "ZtdDeviceAlreadyAssigned"
                  }
                }
              ]
            }
            """);

        AutopilotGraphImportClient client = CreateClient(handler);

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                null,
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.UploadFailed, result.State);
        Assert.Contains("ZtdDeviceAlreadyAssigned", result.Message, StringComparison.Ordinal);
        Assert.Equal("806", result.FailureCode);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenDeviceVisibilityTimesOut_ReturnsTimedOutWithoutThrowing()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "imported-id",
                  "serialNumber": "SER123",
                  "importId": "import-123",
                  "state": { "deviceImportStatus": "complete" }
                }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");

        AutopilotGraphImportClient client = CreateClient(
            handler,
            new AutopilotGraphImportClientOptions
            {
                RetryDelay = TimeSpan.Zero,
                PollInterval = TimeSpan.Zero,
                VisibilityTimeout = TimeSpan.Zero
            });
        List<AutopilotHardwareHashUploadProgress> progressReports = [];

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                null,
                null,
                "import-123"),
            new CapturingAutopilotUploadProgress(progressReports),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.UploadTimedOut, result.State);
        Assert.Contains("did not appear", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(progressReports, progress =>
            progress.IsIndeterminate &&
            progress.Detail?.Contains("remaining", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenImportIsCompleteButDeviceIsAbsent_KeepsPollingUntilTimeout()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "imported-id",
                  "serialNumber": "SER123",
                  "importId": "import-123",
                  "state": { "deviceImportStatus": "complete" }
                }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");

        AutopilotGraphImportClient client = CreateClient(
            handler,
            new AutopilotGraphImportClientOptions
            {
                RetryDelay = TimeSpan.Zero,
                PollInterval = TimeSpan.FromMilliseconds(20),
                VisibilityTimeout = TimeSpan.FromMilliseconds(70)
            });

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                null,
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.UploadTimedOut, result.State);
        Assert.True(DateTimeOffset.UtcNow - startedAt >= TimeSpan.FromMilliseconds(60));
        Assert.True(handler.Requests.Count(request =>
            request.PathAndQuery.StartsWith(
                "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities",
                StringComparison.Ordinal)) >= 3);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenDeviceAppearsBeforeImportCompletion_Completes()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "imported-id",
                  "serialNumber": "SER123",
                  "importId": "import-123",
                  "state": { "deviceImportStatus": "pending" }
                }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """
            { "value": [ { "id": "device-id", "serialNumber": "SER123" } ] }
            """);
        AutopilotGraphImportClient client = CreateClient(handler);

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                null,
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.Completed, result.State);
        Assert.Equal("device-id", result.AutopilotDeviceId);
        Assert.Equal(
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$filter=serialNumber%20eq%20%27SER123%27",
            handler.Requests[^1].PathAndQuery);
    }

    private static AutopilotGraphImportClient CreateClient(
        QueuedGraphHandler handler,
        AutopilotGraphImportClientOptions? options = null)
    {
        return new AutopilotGraphImportClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://graph.microsoft.com/", UriKind.Absolute)
            },
            NullLogger<AutopilotGraphImportClient>.Instance,
            options ?? new AutopilotGraphImportClientOptions
            {
                RetryDelay = TimeSpan.Zero,
                PollInterval = TimeSpan.Zero
            });
    }

    private sealed class CapturingAutopilotUploadProgress(List<AutopilotHardwareHashUploadProgress> reports)
        : IProgress<AutopilotHardwareHashUploadProgress>
    {
        public void Report(AutopilotHardwareHashUploadProgress value)
        {
            reports.Add(value);
        }
    }

    private sealed class QueuedGraphHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new();

        public List<RecordedGraphRequest> Requests { get; } = [];

        public void EnqueueJson(HttpStatusCode statusCode, string json)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(json)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            responses.Enqueue(response);
        }

        public void EnqueueText(HttpStatusCode statusCode, string text)
        {
            responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(text)
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedGraphRequest(
                request.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                body));

            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No queued response for Graph request.");
            }

            return responses.Dequeue();
        }
    }

    private sealed record RecordedGraphRequest(
        HttpMethod Method,
        string PathAndQuery,
        string? Authorization,
        string? Body);
}
