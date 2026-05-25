using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Http;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Imports hardware hashes through Microsoft Graph and waits for Intune Autopilot visibility.
/// </summary>
public sealed class AutopilotGraphImportClient(
    HttpClient httpClient,
    ILogger<AutopilotGraphImportClient> logger,
    AutopilotGraphImportClientOptions? options = null)
{
    private const string ImportedIdentitiesImportPath = "v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities/import";
    private const string ImportedIdentitiesPath = "v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities";
    private const string WindowsAutopilotDevicesPath = "v1.0/deviceManagement/windowsAutopilotDeviceIdentities";
    private const string ODataType = "#microsoft.graph.importedWindowsAutopilotDeviceIdentity";

    private readonly HttpClient httpClient = httpClient;
    private readonly ILogger logger = logger;
    private readonly AutopilotGraphImportClientOptions options = options ?? new AutopilotGraphImportClientOptions();

    public Task<AutopilotHardwareHashUploadResult> ImportHardwareHashAsync(
        AutopilotGraphImportRequest request,
        CancellationToken cancellationToken = default)
    {
        return ImportHardwareHashAsync(request, progress: null, cancellationToken);
    }

    public async Task<AutopilotHardwareHashUploadResult> ImportHardwareHashAsync(
        AutopilotGraphImportRequest request,
        IProgress<AutopilotHardwareHashUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        progress?.Report(new AutopilotHardwareHashUploadProgress(
            "Uploading Autopilot hardware hash...",
            "Submitting import request to Microsoft Graph..."));
        ImportedWindowsAutopilotDeviceIdentity importedIdentity = await ImportAsync(request, cancellationToken)
            .ConfigureAwait(false);
        AutopilotImportWaitResult waitResult = await WaitForAutopilotDeviceReadinessAsync(
            request,
            importedIdentity,
            progress,
            cancellationToken)
            .ConfigureAwait(false);
        importedIdentity = waitResult.ImportedIdentity;
        if (waitResult.AutopilotDevice is not null)
        {
            return AutopilotHardwareHashUploadResult.Completed(
                "Autopilot hardware hash imported and visible in Windows Autopilot devices.",
                request.ImportId,
                importedIdentity.Id,
                waitResult.AutopilotDevice.Id);
        }

        string importStatus = importedIdentity.State?.DeviceImportStatus ?? "unknown";
        if (string.Equals(importStatus, "error", StringComparison.OrdinalIgnoreCase))
        {
            string failureCode = importedIdentity.State?.DeviceErrorCode?.ToString() ?? "ImportFailed";
            string deviceErrorName = importedIdentity.State?.DeviceErrorName ?? "ImportFailed";
            return AutopilotHardwareHashUploadResult.Failed(
                AutopilotHardwareHashUploadState.UploadFailed,
                $"Autopilot hardware hash import failed: {deviceErrorName}.",
                failureCode,
                request.ImportId,
                importedIdentity.Id);
        }

        return AutopilotHardwareHashUploadResult.Failed(
            AutopilotHardwareHashUploadState.UploadTimedOut,
            "Imported Autopilot device did not appear in Windows Autopilot devices before the timeout.",
            "AutopilotDeviceTimedOut",
            request.ImportId,
            importedIdentity.Id);
    }

    private async Task<ImportedWindowsAutopilotDeviceIdentity> ImportAsync(
        AutopilotGraphImportRequest request,
        CancellationToken cancellationToken)
    {
        ImportRequestBody body = new([
            new ImportRequestDevice(
                ODataType,
                request.SerialNumber,
                request.HardwareIdentifier,
                request.GroupTag,
                request.AssignedUserPrincipalName,
                request.ImportId)
        ]);

        GraphCollectionResponse<ImportedWindowsAutopilotDeviceIdentity>? response =
            await SendGraphAsync<ImportRequestBody, GraphCollectionResponse<ImportedWindowsAutopilotDeviceIdentity>>(
                HttpMethod.Post,
                ImportedIdentitiesImportPath,
                request.AccessToken,
                body,
                "Autopilot hardware hash import",
                cancellationToken).ConfigureAwait(false);

        return response?.Value?.FirstOrDefault()
            ?? throw new InvalidOperationException("Microsoft Graph did not return an imported Autopilot device identity.");
    }

    private async Task<AutopilotImportWaitResult> WaitForAutopilotDeviceReadinessAsync(
        AutopilotGraphImportRequest request,
        ImportedWindowsAutopilotDeviceIdentity importedIdentity,
        IProgress<AutopilotHardwareHashUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(options.VisibilityTimeout);
        logger.LogInformation(
            "Waiting up to {Timeout} for Windows Autopilot device visibility. SerialNumber={SerialNumber}, ImportId={ImportId}.",
            options.VisibilityTimeout,
            request.SerialNumber,
            request.ImportId);
        while (true)
        {
            if (IsImportError(importedIdentity.State?.DeviceImportStatus))
            {
                logger.LogWarning(
                    "Autopilot import entered error state before device visibility. SerialNumber={SerialNumber}, ImportId={ImportId}, ImportedIdentityId={ImportedIdentityId}, ErrorCode={ErrorCode}, ErrorName={ErrorName}.",
                    request.SerialNumber,
                    request.ImportId,
                    importedIdentity.Id,
                    importedIdentity.State?.DeviceErrorCode,
                    importedIdentity.State?.DeviceErrorName);
                return new AutopilotImportWaitResult(importedIdentity, null);
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            progress?.Report(new AutopilotHardwareHashUploadProgress(
                "Waiting for Autopilot device visibility...",
                $"Checking Windows Autopilot devices ({FormatRemaining(remaining)} remaining)..."));
            WindowsAutopilotDeviceIdentity? visibleDevice = await FindAutopilotDeviceAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (visibleDevice is not null)
            {
                logger.LogInformation(
                    "Windows Autopilot device became visible. SerialNumber={SerialNumber}, ImportId={ImportId}, AutopilotDeviceId={AutopilotDeviceId}.",
                    request.SerialNumber,
                    request.ImportId,
                    visibleDevice.Id);
                return new AutopilotImportWaitResult(importedIdentity, visibleDevice);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogWarning(
                    "Windows Autopilot device visibility timed out. SerialNumber={SerialNumber}, ImportId={ImportId}, ImportedIdentityId={ImportedIdentityId}, ImportStatus={ImportStatus}.",
                    request.SerialNumber,
                    request.ImportId,
                    importedIdentity.Id,
                    importedIdentity.State?.DeviceImportStatus);
                return new AutopilotImportWaitResult(importedIdentity, null);
            }

            if (!IsImportComplete(importedIdentity.State?.DeviceImportStatus))
            {
                ImportedWindowsAutopilotDeviceIdentity? current = await GetImportedIdentityAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (current is not null)
                {
                    importedIdentity = current;
                }

                if (IsImportError(importedIdentity.State?.DeviceImportStatus))
                {
                    logger.LogWarning(
                        "Autopilot import entered error state during device visibility wait. SerialNumber={SerialNumber}, ImportId={ImportId}, ImportedIdentityId={ImportedIdentityId}, ErrorCode={ErrorCode}, ErrorName={ErrorName}.",
                        request.SerialNumber,
                        request.ImportId,
                        importedIdentity.Id,
                        importedIdentity.State?.DeviceErrorCode,
                        importedIdentity.State?.DeviceErrorName);
                    return new AutopilotImportWaitResult(importedIdentity, null);
                }
            }

            TimeSpan delay = deadline - DateTimeOffset.UtcNow;
            if (delay > options.PollInterval)
            {
                delay = options.PollInterval;
            }

            await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ImportedWindowsAutopilotDeviceIdentity?> GetImportedIdentityAsync(
        AutopilotGraphImportRequest request,
        CancellationToken cancellationToken)
    {
        string filter = EscapeODataFilter($"importId eq '{EscapeODataString(request.ImportId)}'");
        string path = $"{ImportedIdentitiesPath}?$filter={filter}";
        GraphCollectionResponse<ImportedWindowsAutopilotDeviceIdentity>? response =
            await SendGraphAsync<object, GraphCollectionResponse<ImportedWindowsAutopilotDeviceIdentity>>(
                HttpMethod.Get,
                path,
                request.AccessToken,
                body: null,
                "Autopilot import status polling",
                cancellationToken).ConfigureAwait(false);

        return response?.Value?.FirstOrDefault(identity =>
            string.Equals(identity.ImportId, request.ImportId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(identity.SerialNumber, request.SerialNumber, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<WindowsAutopilotDeviceIdentity?> FindAutopilotDeviceAsync(
        AutopilotGraphImportRequest request,
        CancellationToken cancellationToken)
    {
        string filter = EscapeODataFilter($"serialNumber eq '{EscapeODataString(request.SerialNumber)}'");
        string path = $"{WindowsAutopilotDevicesPath}?$filter={filter}";
        GraphCollectionResponse<WindowsAutopilotDeviceIdentity>? response =
            await SendGraphAsync<object, GraphCollectionResponse<WindowsAutopilotDeviceIdentity>>(
                HttpMethod.Get,
                path,
                request.AccessToken,
                body: null,
                "Windows Autopilot device visibility polling",
                cancellationToken).ConfigureAwait(false);

        return response?.Value?.FirstOrDefault(device =>
            string.Equals(device.SerialNumber, request.SerialNumber, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<TResponse?> SendGraphAsync<TBody, TResponse>(
        HttpMethod method,
        string path,
        string accessToken,
        TBody? body,
        string operationName,
        CancellationToken cancellationToken)
        where TBody : class
    {
        return await HttpRetryPolicy.ExecuteAsync(
            async ct =>
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body, options: AutopilotGraphJson.Options);
                }

                using HttpResponseMessage response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string graphError = await ReadGraphErrorAsync(response, ct).ConfigureAwait(false);
                    throw new HttpRequestException(
                        string.IsNullOrWhiteSpace(graphError)
                            ? $"Microsoft Graph request failed with status code {(int)response.StatusCode}."
                            : $"Microsoft Graph request failed with status code {(int)response.StatusCode}: {graphError}.",
                        null,
                        response.StatusCode);
                }

                await using Stream responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<TResponse>(
                    responseStream,
                    AutopilotGraphJson.Options,
                    ct).ConfigureAwait(false);
            },
            logger,
            operationName,
            cancellationToken,
            options.RetryCount,
            options.RetryDelay).ConfigureAwait(false);
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }

    private static bool IsImportError(string? status)
    {
        return string.Equals(status, "error", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImportComplete(string? status)
    {
        return string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        double seconds = Math.Ceiling(remaining.TotalSeconds);
        return seconds == 1
            ? "1 second"
            : $"{seconds:0} seconds";
    }

    private static string EscapeODataFilter(string filter)
    {
        return Uri.EscapeDataString(filter);
    }

    private static string EscapeODataString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static async Task<string> ReadGraphErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                string? code = error.TryGetProperty("code", out JsonElement codeElement)
                    ? codeElement.GetString()
                    : null;
                string? message = error.TryGetProperty("message", out JsonElement messageElement)
                    ? messageElement.GetString()
                    : null;
                return string.Join(
                    ": ",
                    new[] { code, message }
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!.Trim()));
            }
        }
        catch (JsonException)
        {
        }

        return body.Length <= 500 ? body : body[..500];
    }

    private sealed record ImportRequestBody(
        IReadOnlyList<ImportRequestDevice> ImportedWindowsAutopilotDeviceIdentities);

    private sealed record ImportRequestDevice(
        [property: JsonPropertyName("@odata.type")] string ODataType,
        string SerialNumber,
        string HardwareIdentifier,
        string? GroupTag,
        string? AssignedUserPrincipalName,
        string ImportId);
}

/// <summary>
/// Controls Graph retry and polling bounds for the Autopilot import workflow.
/// </summary>
public sealed record AutopilotGraphImportClientOptions
{
    public int RetryCount { get; init; } = HttpRetryPolicy.DefaultRetryCount;
    public TimeSpan RetryDelay { get; init; } = HttpRetryPolicy.DefaultRetryDelay;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan VisibilityTimeout { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed record AutopilotGraphImportRequest(
    string AccessToken,
    string SerialNumber,
    string HardwareIdentifier,
    string? GroupTag,
    string? AssignedUserPrincipalName,
    string ImportId);

internal sealed record GraphCollectionResponse<TItem>
{
    public List<TItem>? Value { get; init; }

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }
}

internal sealed record ImportedWindowsAutopilotDeviceIdentity
{
    public string? Id { get; init; }
    public string? SerialNumber { get; init; }
    public string? ImportId { get; init; }
    public ImportedWindowsAutopilotDeviceIdentityState? State { get; init; }
}

internal sealed record ImportedWindowsAutopilotDeviceIdentityState
{
    public string? DeviceImportStatus { get; init; }
    public int? DeviceErrorCode { get; init; }
    public string? DeviceErrorName { get; init; }
}

internal sealed record WindowsAutopilotDeviceIdentity
{
    public string? Id { get; init; }
    public string? SerialNumber { get; init; }
}

internal sealed record AutopilotImportWaitResult(
    ImportedWindowsAutopilotDeviceIdentity ImportedIdentity,
    WindowsAutopilotDeviceIdentity? AutopilotDevice);

internal static class AutopilotGraphJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
