// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
                { "id": "device-id", "serialNumber": "SER123", "groupTag": "Sales" }
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
                    "deviceErrorCode": 400,
                    "deviceErrorName": "InvalidHardwareIdentifier"
                  }
                }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");

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
        Assert.Contains("InvalidHardwareIdentifier", result.Message, StringComparison.Ordinal);
        Assert.Equal("400", result.FailureCode);
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
        for (int i = 0; i < 500; i++)
        {
            handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");
        }

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
                StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenWaitingForDeviceVisibility_UpdatesProgressMoreOftenThanGraphPolling()
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

        AutopilotGraphImportClient client = CreateClient(
            handler,
            new AutopilotGraphImportClientOptions
            {
                RetryDelay = TimeSpan.Zero,
                PollInterval = TimeSpan.FromMilliseconds(300),
                VisibilityTimeout = TimeSpan.FromMilliseconds(280),
                ProgressInterval = TimeSpan.FromMilliseconds(50)
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

        int visibilityPolls = handler.Requests.Count(request =>
            request.PathAndQuery.StartsWith(
                "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities",
                StringComparison.Ordinal));
        int countdownReports = progressReports.Count(progress =>
            progress.Detail?.StartsWith("Checking Windows Autopilot devices", StringComparison.Ordinal) == true);

        Assert.Equal(AutopilotHardwareHashUploadState.UploadTimedOut, result.State);
        Assert.Equal(2, visibilityPolls);
        Assert.True(countdownReports > visibilityPolls);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenCheckingDeviceVisibility_UsesUnfilteredDeviceList()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "imported-id",
                  "serialNumber": "VMware-56 4d a9 71 fa ed 42 8b-e2 4c 5e e8 f9 e9 72 04",
                  "importId": "import-123",
                  "state": { "deviceImportStatus": "complete" }
                }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "device-id",
                  "serialNumber": "VMware-56 4d a9 71 fa ed 42 8b-e2 4c 5e e8 f9 e9 72 04"
                }
              ]
            }
            """);

        AutopilotGraphImportClient client = CreateClient(handler);

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "VMware-56 4d a9 71 fa ed 42 8b-e2 4c 5e e8 f9 e9 72 04",
                "aGFyZHdhcmVIYXNo",
                null,
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.Completed, result.State);
        Assert.Equal("device-id", result.AutopilotDeviceId);
        Assert.Contains(handler.Requests, request =>
            request.PathAndQuery == "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities");
        Assert.DoesNotContain(handler.Requests, request =>
            request.PathAndQuery.Contains("$filter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenDeviceVisibilityListIsPaged_FollowsNextLink()
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
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$skiptoken=page-2",
              "value": []
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id", "serialNumber": "SER123" }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");

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
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities",
            handler.Requests[^2].PathAndQuery);
        Assert.Equal(
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$skiptoken=page-2",
            handler.Requests[^1].PathAndQuery);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenImportStatusListIsPaged_FollowsNextLink()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                {
                  "id": "initial-imported-id",
                  "serialNumber": "SER123",
                  "importId": "import-123",
                  "state": { "deviceImportStatus": "pending" }
                }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities?$skiptoken=page-2",
              "value": []
            }
            """);
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
                null,
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.Completed, result.State);
        Assert.Equal("device-id", result.AutopilotDeviceId);
        Assert.Contains(handler.Requests, request =>
            request.PathAndQuery.StartsWith(
                "/v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities?$filter=",
                StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request =>
            request.PathAndQuery == "/v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities?$skiptoken=page-2");
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
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities",
            handler.Requests[^1].PathAndQuery);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenAutopilotDeviceAlreadyExistsWithDifferentGroupTag_UpdatesGroupTagBeforeCompleting()
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
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id", "serialNumber": "SER123", "groupTag": "A" }
              ]
            }
            """);
        handler.EnqueueText(HttpStatusCode.NoContent, string.Empty);
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id", "serialNumber": "SER123", "groupTag": "B" }
              ]
            }
            """);

        AutopilotGraphImportClient client = CreateClient(handler);

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                "B",
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.Completed, result.State);
        Assert.Equal("device-id", result.AutopilotDeviceId);
        Assert.Equal(
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities",
            handler.Requests[1].PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);
        Assert.Equal(
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities/device-id/updateDeviceProperties",
            handler.Requests[2].PathAndQuery);
        Assert.Equal(
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities",
            handler.Requests[3].PathAndQuery);
        Assert.Contains(handler.Requests, request =>
            request.PathAndQuery == "/v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities/import");

        using JsonDocument body = JsonDocument.Parse(handler.Requests[2].Body!);
        Assert.Equal("B", body.RootElement.GetProperty("groupTag").GetString());
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenAutopilotDeviceAlreadyExistsAndSelectedGroupTagIsNone_ClearsGroupTag()
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
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id", "serialNumber": "SER123", "groupTag": "A" }
              ]
            }
            """);
        handler.EnqueueText(HttpStatusCode.NoContent, string.Empty);
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id", "serialNumber": "SER123", "groupTag": "" }
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

        Assert.Equal(AutopilotHardwareHashUploadState.Completed, result.State);
        Assert.Equal("device-id", result.AutopilotDeviceId);

        using JsonDocument body = JsonDocument.Parse(handler.Requests[2].Body!);
        Assert.Equal(string.Empty, body.RootElement.GetProperty("groupTag").GetString());
        Assert.Equal(
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities",
            handler.Requests[3].PathAndQuery);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenImportReportsExistingDevice_WaitsForVisibleDeviceUntilTimeout()
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
        handler.EnqueueJson(HttpStatusCode.OK, """{ "value": [] }""");

        AutopilotGraphImportClient client = CreateClient(
            handler,
            new AutopilotGraphImportClientOptions
            {
                RetryDelay = TimeSpan.Zero,
                PollInterval = TimeSpan.Zero,
                VisibilityTimeout = TimeSpan.Zero
            });

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
        Assert.Equal("AutopilotDeviceTimedOut", result.FailureCode);
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenMultipleAutopilotDevicesMatchSerial_DoesNotUpdateGroupTag()
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
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id-1", "serialNumber": "SER123", "groupTag": "A" },
                { "id": "device-id-2", "serialNumber": "SER123", "groupTag": "B" }
              ]
            }
            """);

        AutopilotGraphImportClient client = CreateClient(handler);

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                "C",
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.UploadFailed, result.State);
        Assert.Equal("AutopilotDeviceAmbiguous", result.FailureCode);
        Assert.DoesNotContain(handler.Requests, request =>
            request.PathAndQuery.Contains("updateDeviceProperties", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportHardwareHashAsync_WhenExistingDeviceGroupTagUpdateIsNotConfirmed_ReturnsTimedOut()
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
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id", "serialNumber": "SER123", "groupTag": "A" }
              ]
            }
            """);
        handler.EnqueueText(HttpStatusCode.NoContent, string.Empty);
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-id", "serialNumber": "SER123", "groupTag": "A" }
              ]
            }
            """);

        AutopilotGraphImportClient client = CreateClient(
            handler,
            new AutopilotGraphImportClientOptions
            {
                RetryDelay = TimeSpan.Zero,
                PollInterval = TimeSpan.Zero,
                VisibilityTimeout = TimeSpan.Zero
            });

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest(
                "access-token",
                "SER123",
                "aGFyZHdhcmVIYXNo",
                "B",
                null,
                "import-123"),
            CancellationToken.None);

        Assert.Equal(AutopilotHardwareHashUploadState.UploadTimedOut, result.State);
        Assert.Equal("AutopilotGroupTagUpdateTimedOut", result.FailureCode);
        Assert.Equal("device-id", result.AutopilotDeviceId);
    }

    [Fact]
    public async Task ListGroupTagsAsync_ReadsPagedAutopilotDevicesAndReturnsDistinctTags()
    {
        var handler = new QueuedGraphHandler();
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$skiptoken=page-2",
              "value": [
                { "id": "device-1", "serialNumber": "SER1", "groupTag": " KIOSK " },
                { "id": "device-2", "serialNumber": "SER2", "groupTag": "" }
              ]
            }
            """);
        handler.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "device-3", "serialNumber": "SER3", "groupTag": "HAADJ" },
                { "id": "device-4", "serialNumber": "SER4", "groupTag": "kiosk" }
              ]
            }
            """);
        AutopilotGraphImportClient client = CreateClient(handler);

        IReadOnlyList<string> groupTags = await client.ListGroupTagsAsync("access-token", CancellationToken.None);

        Assert.Equal(["HAADJ", "KIOSK"], groupTags);
        Assert.Equal(
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities",
            handler.Requests[0].PathAndQuery);
        Assert.Equal(
            "/v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$skiptoken=page-2",
            handler.Requests[1].PathAndQuery);
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
