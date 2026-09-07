// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Http;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>Correlates hardware imports with the registration ID reported by that exact imported identity.</summary>
public sealed class AutopilotGraphImportClient(HttpClient httpClient, ILogger<AutopilotGraphImportClient> logger,
    AutopilotGraphImportClientOptions? options = null)
{
    private const string ImportedPath = "v1.0/deviceManagement/importedWindowsAutopilotDeviceIdentities";
    private const string DevicesPath = "v1.0/deviceManagement/windowsAutopilotDeviceIdentities";
    private static readonly Uri GraphRoot = new("https://graph.microsoft.com/");
    private readonly AutopilotGraphImportClientOptions options = options ?? new();

    public Task<AutopilotHardwareHashUploadResult> ImportHardwareHashAsync(AutopilotGraphImportRequest request,
        CancellationToken cancellationToken = default) => ImportHardwareHashAsync(request, null, cancellationToken);

    public async Task<AutopilotHardwareHashUploadResult> ImportHardwareHashAsync(AutopilotGraphImportRequest request,
        IProgress<AutopilotHardwareHashUploadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOptions();
        cancellationToken.ThrowIfCancellationRequested();
        using var workflow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        workflow.CancelAfter(options.WorkflowTimeout);
        DateTimeOffset workflowDeadline = DateTimeOffset.UtcNow.Add(options.WorkflowTimeout);
        string? importedId = null;
        try
        {
            ValidateRequest(request);
            progress?.Report(new("Uploading Autopilot hardware hash...", "Submitting the current hardware identity..."));
            var body = new ImportRequestBody([new ImportRequestDevice("#microsoft.graph.importedWindowsAutopilotDeviceIdentity",
                request.SerialNumber, request.HardwareIdentifier, request.GroupTag, request.AssignedUserPrincipalName, request.ImportId)]);
            GraphCollectionResponse<ImportedWindowsAutopilotDeviceIdentity>? response = await SendGraphAsync<GraphCollectionResponse<ImportedWindowsAutopilotDeviceIdentity>>(
                HttpMethod.Post, ImportedPath + "/import", request.AccessToken, body, workflow.Token).ConfigureAwait(false);
            if (response?.Value is not { Count: 1 } || response.NextLink is not null)
                return Failed(request, "IdentityUnconfirmed");
            ImportedWindowsAutopilotDeviceIdentity imported = response.Value[0];
            if (!MatchesRequest(imported, request)) return Failed(request, "IdentityUnconfirmed");
            importedId = imported.Id;
            while (true)
            {
                workflow.Token.ThrowIfCancellationRequested();
                if (!MatchesRequest(imported, request) || !string.Equals(imported.Id, importedId, StringComparison.Ordinal))
                    return Failed(request, "IdentityUnconfirmed", importedId);
                string? status = imported.State?.DeviceImportStatus;
                bool error = string.Equals(status, "error", StringComparison.OrdinalIgnoreCase);
                bool complete = string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase);
                if (error && !IsSameTenantAlreadyAssigned(imported.State)) return Failed(request, "ImportFailed", importedId);
                if (error || complete)
                {
                    string? registrationId = imported.State?.DeviceRegistrationId;
                    if (string.IsNullOrWhiteSpace(registrationId)) return Failed(request, "IdentityUnconfirmed", importedId);
                    return await WaitForRegistrationAsync(request, importedId!, registrationId, DateTimeOffset.UtcNow.Add(options.VisibilityTimeout), progress, workflow.Token).ConfigureAwait(false);
                }
                if (status is null || !(status.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("partial", StringComparison.OrdinalIgnoreCase) || status.Equals("unknown", StringComparison.OrdinalIgnoreCase)))
                    return Failed(request, "IdentityUnconfirmed", importedId);
                if (DateTimeOffset.UtcNow >= workflowDeadline) return TimedOut(request, "WorkflowTimedOut", importedId);
                await DelayWithProgressAsync(workflowDeadline, progress, workflow.Token).ConfigureAwait(false);
                imported = await GetImportedIdentityAsync(request.AccessToken, importedId!, workflow.Token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The current imported identity is unavailable.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && workflow.IsCancellationRequested)
        {
            return TimedOut(request, "WorkflowTimedOut", importedId);
        }
        catch (TransferTimeoutException error) when (error.Kind == TransferTimeoutKind.Overall)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TimedOut(request, "WorkflowTimedOut", importedId);
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TimedOut(request, workflow.IsCancellationRequested ? "WorkflowTimedOut" : "RequestTimedOut", importedId);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        {
            return Failed(request, "IdentityUnconfirmed", importedId);
        }
    }

    public async Task<IReadOnlyList<string>> ListGroupTagsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ValidateOptions();
        using var workflow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        workflow.CancelAfter(options.WorkflowTimeout);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? path = DevicesPath;
        try
        {
            while (path is not null)
            {
                Uri uri = ValidateCollectionLink(path);
                if (visited.Count >= options.MaximumGroupTagPages || !visited.Add(uri.AbsoluteUri))
                    throw new InvalidDataException("Group tag pagination is cyclic or exceeds its page limit.");
                GraphCollectionResponse<WindowsAutopilotDeviceIdentity>? response = await SendGraphAsync<GraphCollectionResponse<WindowsAutopilotDeviceIdentity>>(
                    HttpMethod.Get, uri.AbsoluteUri, accessToken, null, workflow.Token).ConfigureAwait(false);
                if (response?.Value is null) throw new InvalidDataException("The group tag collection is unavailable.");
                foreach (string? tag in response.Value.Select(device => device.GroupTag?.Trim()))
                    if (!string.IsNullOrEmpty(tag)) tags.Add(tag);
                if (tags.Count > 100000) throw new InvalidDataException("Group tag discovery exceeds its item limit.");
                path = response.NextLink;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && workflow.IsCancellationRequested)
        {
            throw new TimeoutException("Group tag discovery exceeded its workflow deadline.");
        }
        return tags.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<AutopilotHardwareHashUploadResult> WaitForRegistrationAsync(AutopilotGraphImportRequest request,
        string importedId, string registrationId, DateTimeOffset deadline,
        IProgress<AutopilotHardwareHashUploadProgress>? progress, CancellationToken token)
    {
        bool updated = false;
        using var visibility = CancellationTokenSource.CreateLinkedTokenSource(token);
        TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return TimedOut(request, "AutopilotDeviceTimedOut", importedId);
        visibility.CancelAfter(remaining);
        try
        {
            while (true)
            {
                visibility.Token.ThrowIfCancellationRequested();
                WindowsAutopilotDeviceIdentity? device = await GetAutopilotDeviceByIdAsync(request.AccessToken, registrationId, visibility.Token).ConfigureAwait(false);
                if (device is not null)
                {
                    if (!string.Equals(device.Id, registrationId, StringComparison.Ordinal) ||
                        !string.Equals(device.SerialNumber, request.SerialNumber, StringComparison.Ordinal))
                        return Failed(request, "IdentityUnconfirmed", importedId);
                    if (string.Equals(NormalizeGroupTag(device.GroupTag), NormalizeGroupTag(request.GroupTag), StringComparison.OrdinalIgnoreCase))
                        return AutopilotHardwareHashUploadResult.Completed("The imported hardware identity is registered in Windows Autopilot.", request.ImportId, importedId, registrationId);
                    if (!updated)
                    {
                        await SendGraphAsync<object>(HttpMethod.Post, DevicesPath + "/" + Uri.EscapeDataString(registrationId) + "/updateDeviceProperties",
                            request.AccessToken, new UpdateDevicePropertiesRequest(NormalizeGroupTag(request.GroupTag)), visibility.Token, noContent: true).ConfigureAwait(false);
                        updated = true;
                    }
                }
                if (DateTimeOffset.UtcNow >= deadline) return TimedOut(request, updated ? "GroupTagTimedOut" : "AutopilotDeviceTimedOut", importedId);
                await DelayWithProgressAsync(deadline, progress, visibility.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && visibility.IsCancellationRequested)
        {
            return TimedOut(request, updated ? "GroupTagTimedOut" : "AutopilotDeviceTimedOut", importedId);
        }
    }

    private async Task<ImportedWindowsAutopilotDeviceIdentity?> GetImportedIdentityAsync(string accessToken, string id, CancellationToken token)
    {
        JsonElement response = await SendGraphAsync<JsonElement>(HttpMethod.Get, ImportedPath + "/" + Uri.EscapeDataString(id), accessToken, null, token).ConfigureAwait(false);
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("value", out JsonElement value)) response = value;
        return response.Deserialize<ImportedWindowsAutopilotDeviceIdentity>(AutopilotGraphJson.Options);
    }

    private async Task<WindowsAutopilotDeviceIdentity?> GetAutopilotDeviceByIdAsync(string accessToken, string registrationId, CancellationToken token)
    {
        try
        {
            return await SendGraphAsync<WindowsAutopilotDeviceIdentity>(HttpMethod.Get, DevicesPath + "/" + Uri.EscapeDataString(registrationId), accessToken, null, token).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    private async Task<T?> SendGraphAsync<T>(HttpMethod method, string path, string accessToken, object? body,
        CancellationToken token, bool noContent = false)
    {
        try
        {
            return await HttpRetryPolicy.ExecuteAsync(async ct =>
            {
                try
                {
                    using var request = new HttpRequestMessage(method, new Uri(GraphRoot, path));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    if (body is not null) request.Content = JsonContent.Create(body, options: AutopilotGraphJson.Options);
                    using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpResponseException(response.StatusCode, response.Headers.RetryAfter?.Delta, response.Headers.RetryAfter?.Date);
                    if (noContent) return default;
                    string json = await BoundedHttpContent.ReadStringAsync(response, 32L * 1024 * 1024, ct).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<T>(json, AutopilotGraphJson.Options);
                }
                catch (HttpRequestException error) when (method == HttpMethod.Post && error.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    throw new NonRetryableMutationException(error);
                }
                catch (TransferReadException error) when (method == HttpMethod.Post)
                {
                    throw new NonRetryableMutationException(error);
                }
                catch (OperationCanceledException error) when (method == HttpMethod.Post && !token.IsCancellationRequested)
                {
                    throw new NonRetryableMutationException(new TimeoutException("The mutation request exceeded its deadline; its server outcome is unknown.", error));
                }
            }, logger, "Autopilot Graph request", token, HttpOperationOptions.Metadata with
            {
                MaximumAttempts = checked(options.RetryCount + 1),
                OverallTimeout = options.WorkflowTimeout,
                RequestTimeout = options.RequestTimeout,
                InitialRetryDelay = options.RetryDelay,
                MaximumRetryDelay = options.RetryDelay > TimeSpan.FromSeconds(10) ? options.RetryDelay : TimeSpan.FromSeconds(10)
            }).ConfigureAwait(false);
        }
        catch (NonRetryableMutationException error)
        {
            ExceptionDispatchInfo.Capture(error.Failure).Throw();
            throw;
        }
    }

    private sealed class NonRetryableMutationException(Exception failure) : Exception("The mutation outcome must not be retried automatically.")
    {
        public Exception Failure { get; } = failure;
    }

    private async Task DelayWithProgressAsync(DateTimeOffset deadline, IProgress<AutopilotHardwareHashUploadProgress>? progress, CancellationToken token)
    {
        TimeSpan remainingDelay = options.PollInterval;
        do
        {
            token.ThrowIfCancellationRequested();
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            progress?.Report(new("Waiting for Autopilot registration...", $"Checking the imported identity ({Math.Max(0, Math.Ceiling(remaining.TotalSeconds)):0} seconds remaining)..."));
            TimeSpan delay = remainingDelay < options.ProgressInterval ? remainingDelay : options.ProgressInterval;
            if (delay > remaining) delay = remaining;
            if (delay <= TimeSpan.Zero) return;
            await Task.Delay(delay, token).ConfigureAwait(false);
            remainingDelay -= delay;
        } while (remainingDelay > TimeSpan.Zero);
    }

    private static bool MatchesRequest(ImportedWindowsAutopilotDeviceIdentity? imported, AutopilotGraphImportRequest request) =>
        imported is not null && !string.IsNullOrWhiteSpace(imported.Id) && string.Equals(imported.ImportId, request.ImportId, StringComparison.Ordinal) &&
        string.Equals(imported.SerialNumber, request.SerialNumber, StringComparison.Ordinal) && SameHardware(imported.HardwareIdentifier, request.HardwareIdentifier);

    private static bool SameHardware(string? actual, string expected)
    {
        try { return actual is not null && Convert.FromBase64String(actual).AsSpan().SequenceEqual(Convert.FromBase64String(expected)); }
        catch (FormatException) { return false; }
    }

    // Microsoft documents 806 as already registered to this tenant; serial visibility alone is never proof.
    private static bool IsSameTenantAlreadyAssigned(ImportedWindowsAutopilotDeviceIdentityState? state) =>
        string.Equals(state?.DeviceErrorName, "ZtdDeviceAlreadyAssigned", StringComparison.OrdinalIgnoreCase) &&
        (state.DeviceErrorCode is null or 806);

    private static void ValidateRequest(AutopilotGraphImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken) || string.IsNullOrWhiteSpace(request.ImportId) ||
            string.IsNullOrWhiteSpace(request.SerialNumber) || string.IsNullOrWhiteSpace(request.HardwareIdentifier)) throw new InvalidDataException("The import request identity is incomplete.");
        try { if (Convert.FromBase64String(request.HardwareIdentifier).Length > 0) return; }
        catch (FormatException) { }
        throw new InvalidDataException("The hardware identifier is invalid.");
    }

    private static Uri ValidateCollectionLink(string path)
    {
        if (!Uri.TryCreate(GraphRoot, path, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(GraphRoot.Host, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" + DevicesPath)
            throw new InvalidDataException("Graph pagination must remain on the requested collection.");
        return uri;
    }

    private void ValidateOptions()
    {
        if (options.WorkflowTimeout <= TimeSpan.Zero || options.WorkflowTimeout > TimeSpan.FromMinutes(15) ||
            options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromSeconds(30) ||
            options.VisibilityTimeout <= TimeSpan.Zero || options.VisibilityTimeout > TimeSpan.FromMinutes(10) ||
            options.ProgressInterval <= TimeSpan.Zero || options.PollInterval < TimeSpan.Zero ||
            options.RetryCount is < 0 or > 5 || options.RetryDelay < TimeSpan.Zero || options.MaximumGroupTagPages is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(options), "Autopilot limits must be finite and bounded.");
    }

    private static string NormalizeGroupTag(string? value) => value?.Trim() ?? string.Empty;
    private static AutopilotHardwareHashUploadResult Failed(AutopilotGraphImportRequest request, string code, string? importedId = null) =>
        AutopilotHardwareHashUploadResult.Failed(AutopilotHardwareHashUploadState.UploadFailed,
            code == "IdentityUnconfirmed" ? "The imported hardware identity could not be confirmed. Review the current import in Intune before retrying; no unrelated registration was changed."
                : "Microsoft Graph reported that the current hardware import failed. Review its status in Intune.", code, request.ImportId, importedId);
    private static AutopilotHardwareHashUploadResult TimedOut(AutopilotGraphImportRequest request, string code, string? importedId) =>
        AutopilotHardwareHashUploadResult.Failed(AutopilotHardwareHashUploadState.UploadTimedOut,
            "Autopilot registration did not finish within its network deadline.", code, request.ImportId, importedId);

    private sealed record ImportRequestBody(IReadOnlyList<ImportRequestDevice> ImportedWindowsAutopilotDeviceIdentities);
    private sealed record ImportRequestDevice([property: JsonPropertyName("@odata.type")] string ODataType,
        string SerialNumber, string HardwareIdentifier, string? GroupTag, string? AssignedUserPrincipalName, string ImportId);
    private sealed record UpdateDevicePropertiesRequest(string GroupTag);
}

public sealed record AutopilotGraphImportClientOptions
{
    public int RetryCount { get; init; } = HttpRetryPolicy.DefaultRetryCount;
    public TimeSpan RetryDelay { get; init; } = HttpRetryPolicy.DefaultRetryDelay;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan VisibilityTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan WorkflowTimeout { get; init; } = TimeSpan.FromMinutes(15);
    public int MaximumGroupTagPages { get; init; } = 100;
}

public sealed record AutopilotGraphImportRequest(string AccessToken, string SerialNumber, string HardwareIdentifier,
    string? GroupTag, string? AssignedUserPrincipalName, string ImportId);
internal sealed record GraphCollectionResponse<TItem>
{
    public List<TItem>? Value { get; init; }
    [JsonPropertyName("@odata.nextLink")] public string? NextLink { get; init; }
}
internal sealed record ImportedWindowsAutopilotDeviceIdentity
{
    public string? Id { get; init; }
    public string? SerialNumber { get; init; }
    public string? ImportId { get; init; }
    public string? HardwareIdentifier { get; init; }
    public ImportedWindowsAutopilotDeviceIdentityState? State { get; init; }
}
internal sealed record ImportedWindowsAutopilotDeviceIdentityState
{
    public string? DeviceImportStatus { get; init; }
    public string? DeviceRegistrationId { get; init; }
    public int? DeviceErrorCode { get; init; }
    public string? DeviceErrorName { get; init; }
}
internal sealed record WindowsAutopilotDeviceIdentity
{
    public string? Id { get; init; }
    public string? SerialNumber { get; init; }
    public string? GroupTag { get; init; }
}
internal static class AutopilotGraphJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
