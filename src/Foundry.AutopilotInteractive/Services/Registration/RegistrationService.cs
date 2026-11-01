// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http;
using Foundry.AutopilotInteractive.Services.Device;
using Foundry.Core.Services.Autopilot;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.Services.Registration;

/// <summary>
/// What the registration page needs before the technician chooses.
/// </summary>
/// <param name="GroupTags">Distinct group tags found in the tenant; empty when <paramref name="GroupTagsUnavailable"/>.</param>
/// <param name="GroupTagsUnavailable">The tag discovery failed, so only a custom tag can be offered.</param>
/// <param name="Lookup">Lookup of this device by serial number; <c>NotFound</c> when the lookup could not run.</param>
public sealed record RegistrationContext(
    IReadOnlyList<string> GroupTags,
    bool GroupTagsUnavailable,
    AutopilotDeviceLookup Lookup);

/// <summary>
/// The technician's choices for one registration.
/// </summary>
/// <param name="AccessToken">Graph access token of the signed-in account.</param>
/// <param name="GroupTag">Group tag to set; <see langword="null"/> means no tag and clears any tag already on the device (None is an explicit choice).</param>
/// <param name="AssignedUserPrincipalName">User to assign; <see langword="null"/> never clears a user already assigned.</param>
/// <param name="AssignedComputerName">Computer name to assign; <see langword="null"/> preserves any name already assigned.</param>
public sealed record RegistrationRequest(
    string AccessToken,
    string? GroupTag,
    string? AssignedUserPrincipalName,
    string? AssignedComputerName);

/// <summary>
/// Outcome of one registration attempt.
/// </summary>
/// <param name="Succeeded">The device is visible in Windows Autopilot devices.</param>
/// <param name="FailureCode">A code from <see cref="RegistrationFailureCodes"/> when <paramref name="Succeeded"/> is <see langword="false"/>.</param>
/// <param name="ImportId">Graph import identifier, when an import started.</param>
/// <param name="AutopilotDeviceId">Autopilot device identity, when the device became visible.</param>
public sealed record RegistrationOutcome(bool Succeeded, string? FailureCode, string? ImportId, string? AutopilotDeviceId);

/// <summary>
/// Registers this device in Windows Autopilot.
/// </summary>
public interface IRegistrationService
{
    /// <summary>
    /// Loads the tenant's group tags and looks this device up, in parallel.
    /// </summary>
    /// <remarks>
    /// A failed tag discovery sets <see cref="RegistrationContext.GroupTagsUnavailable"/>; a failed lookup returns
    /// <c>NotFound</c>. Neither blocks the page.
    /// </remarks>
    /// <param name="accessToken">Graph access token.</param>
    /// <param name="ct">Cancels the loading.</param>
    /// <returns>The data for the page.</returns>
    /// <exception cref="HttpRequestException">Graph answered 401: the caller must sign in again.</exception>
    Task<RegistrationContext> LoadAsync(string accessToken, CancellationToken ct);

    /// <summary>
    /// Imports the hardware hash and waits until the device is visible in Windows Autopilot devices.
    /// </summary>
    /// <param name="request">The technician's choices.</param>
    /// <param name="progress">Receives the upload progress texts.</param>
    /// <param name="ct">Cancels the registration.</param>
    /// <returns>The outcome; failures are reported as a code, never thrown.</returns>
    /// <exception cref="HttpRequestException">Graph answered 401: the caller must sign in again.</exception>
    Task<RegistrationOutcome> RegisterAsync(
        RegistrationRequest request,
        IProgress<AutopilotHardwareHashUploadProgress> progress,
        CancellationToken ct);
}

/// <summary>
/// Default <see cref="IRegistrationService"/> over the shared Graph client.
/// </summary>
/// <remarks>
/// The client must be created with <see cref="GraphClientOptions"/> so the visibility wait is long enough for an interactive
/// registration. This class never logs the hardware hash, the serial number, the group tag or the user name; the Graph client logs
/// only a masked serial number and whether a group tag is set.
/// </remarks>
public sealed class RegistrationService : IRegistrationService
{
    /// <summary>
    /// Options the Graph client must be constructed with: the import is visible in Autopilot devices after up to 15 minutes.
    /// </summary>
    public static readonly AutopilotGraphImportClientOptions GraphClientOptions = new()
    {
        VisibilityTimeout = TimeSpan.FromMinutes(15)
    };

    private readonly AutopilotGraphImportClient _client;
    private readonly IHardwareIdentityReader _hardwareIdentityReader;
    private readonly ILogger<RegistrationService> _logger;

    /// <param name="client">Graph client created with <see cref="GraphClientOptions"/>.</param>
    /// <param name="hardwareIdentityReader">Reads the serial number and the hardware hash.</param>
    /// <param name="logger">Receives the start and terminal log of each operation.</param>
    public RegistrationService(
        AutopilotGraphImportClient client,
        IHardwareIdentityReader hardwareIdentityReader,
        ILogger<RegistrationService> logger)
    {
        _client = client;
        _hardwareIdentityReader = hardwareIdentityReader;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegistrationContext> LoadAsync(string accessToken, CancellationToken ct)
    {
        // Started in this order: the tag request is sent before the lookup waits for the WMI read.
        Task<(IReadOnlyList<string> Tags, bool Unavailable)> tagsTask = LoadGroupTagsAsync(accessToken, ct);
        Task<AutopilotDeviceLookup> lookupTask = LookupDeviceAsync(accessToken, ct);
        await Task.WhenAll(tagsTask, lookupTask).ConfigureAwait(false);

        (IReadOnlyList<string> tags, bool unavailable) = tagsTask.Result;
        AutopilotDeviceLookup lookup = lookupTask.Result;
        _logger.LogInformation(
            "Autopilot registration context loaded. GroupTagCount={GroupTagCount}, GroupTagsUnavailable={GroupTagsUnavailable}, LookupStatus={LookupStatus}",
            tags.Count,
            unavailable,
            lookup.Status);
        return new RegistrationContext(tags, unavailable, lookup);
    }

    /// <inheritdoc />
    public async Task<RegistrationOutcome> RegisterAsync(
        RegistrationRequest request,
        IProgress<AutopilotHardwareHashUploadProgress> progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);

        string importId = Guid.NewGuid().ToString();
        _logger.LogInformation(
            "Autopilot registration started. ImportId={ImportId}, HasGroupTag={HasGroupTag}, HasAssignedUser={HasAssignedUser}, HasAssignedComputerName={HasAssignedComputerName}",
            importId,
            request.GroupTag is not null,
            request.AssignedUserPrincipalName is not null,
            request.AssignedComputerName is not null);

        HardwareIdentity identity;
        try
        {
            identity = await Task.Run(_hardwareIdentityReader.Read, ct).ConfigureAwait(false);
        }
        catch (HardwareIdentityUnavailableException exception)
        {
            return Fail(RegistrationFailureCodes.HardwareIdentityUnavailable, importId, exception);
        }

        try
        {
            AutopilotHardwareHashUploadResult result = await _client.ImportHardwareHashAsync(
                new AutopilotGraphImportRequest(
                    request.AccessToken,
                    identity.SerialNumber,
                    identity.HardwareHash,
                    request.GroupTag,
                    request.AssignedUserPrincipalName,
                    importId,
                    request.AssignedComputerName),
                progress,
                ct).ConfigureAwait(false);

            if (result.IsCompleted)
            {
                _logger.LogInformation("Autopilot registration completed. ImportId={ImportId}", importId);
                return new RegistrationOutcome(true, null, result.ImportId ?? importId, result.AutopilotDeviceId);
            }

            string code = RegistrationFailureCodes.FromResult(result);
            _logger.LogWarning(
                "Autopilot registration failed. ImportId={ImportId}, FailureCode={FailureCode}, State={State}",
                importId,
                code,
                result.State);
            return new RegistrationOutcome(false, code, result.ImportId ?? importId, result.AutopilotDeviceId);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Autopilot registration stopped: the access token was rejected. ImportId={ImportId}", importId);
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Autopilot registration cancelled. ImportId={ImportId}", importId);
            throw;
        }
        catch (Exception exception)
        {
            return Fail(RegistrationFailureCodes.FromException(exception), importId, exception);
        }
    }

    private async Task<(IReadOnlyList<string> Tags, bool Unavailable)> LoadGroupTagsAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            return (await _client.ListGroupTagsAsync(accessToken, ct).ConfigureAwait(false), false);
        }
        catch (Exception exception) when (IsRecoverable(exception, ct))
        {
            LogRecoverable("Group tag discovery failed", exception);
            return ([], true);
        }
    }

    private async Task<AutopilotDeviceLookup> LookupDeviceAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            HardwareIdentity identity = await Task.Run(_hardwareIdentityReader.Read, ct).ConfigureAwait(false);
            return await _client.FindDeviceBySerialNumberAsync(accessToken, identity.SerialNumber, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverable(exception, ct))
        {
            LogRecoverable("Autopilot device lookup failed", exception);
            return new AutopilotDeviceLookup(AutopilotDeviceLookupStatus.NotFound, null);
        }
    }

    private static bool IsRecoverable(Exception exception, CancellationToken ct) =>
        !(exception is OperationCanceledException && ct.IsCancellationRequested)
        && exception is not HttpRequestException { StatusCode: HttpStatusCode.Unauthorized };

    private void LogRecoverable(string message, Exception exception)
    {
        _logger.LogWarning(
            "{Message}; continuing without it. FailureType={FailureType}, StatusCode={StatusCode}",
            message,
            exception.GetType().Name,
            (exception as HttpRequestException)?.StatusCode);
    }

    private RegistrationOutcome Fail(string code, string importId, Exception exception)
    {
        _logger.LogWarning(
            "Autopilot registration failed. ImportId={ImportId}, FailureCode={FailureCode}, FailureType={FailureType}, StatusCode={StatusCode}",
            importId,
            code,
            exception.GetType().Name,
            (exception as HttpRequestException)?.StatusCode);
        return new RegistrationOutcome(false, code, null, null);
    }
}
