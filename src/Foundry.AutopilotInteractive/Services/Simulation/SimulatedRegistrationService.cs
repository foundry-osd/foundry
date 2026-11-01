// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Net;
using System.Net.Http;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.AutopilotInteractive.Services.Simulation;

/// <summary>
/// <see cref="IRegistrationService"/> for debug scenarios: returns fixed tags and lookups and simulates the upload steps.
/// </summary>
/// <remarks>
/// Nothing is read from the device and nothing is sent to Microsoft Graph. Progress texts reuse the shared Graph client's
/// messages so the page maps them exactly as in production.
/// </remarks>
/// <param name="scenario">Scenario that decides the answers.</param>
/// <param name="delay">Waits for the given duration; throws <see cref="OperationCanceledException"/> when the token is cancelled.</param>
public sealed class SimulatedRegistrationService(
    DebugScenario scenario,
    Func<TimeSpan, CancellationToken, Task> delay) : IRegistrationService
{
    /// <summary>Simulated duration of the page loading.</summary>
    public static readonly TimeSpan LoadDuration = TimeSpan.FromSeconds(1.5);

    /// <summary>Simulated duration of each upload step (upload, visibility wait, property update).</summary>
    public static readonly TimeSpan StepDuration = TimeSpan.FromSeconds(2);

    private static readonly string[] DefaultTags = ["Engineering", "Kiosk", "Sales", "Shared"];

    private int _registerCount;

    /// <inheritdoc />
    public async Task<RegistrationContext> LoadAsync(string accessToken, CancellationToken ct)
    {
        await delay(LoadDuration, ct).ConfigureAwait(false);
        return scenario switch
        {
            DebugScenario.ManyGroupTags => new RegistrationContext(ManyTags(), false, NotFound),
            DebugScenario.NoGroupTags => new RegistrationContext([], false, NotFound),
            DebugScenario.GroupTagDiscoveryFailure => new RegistrationContext([], true, NotFound),
            DebugScenario.AlreadyRegistered => new RegistrationContext(
                DefaultTags,
                false,
                new AutopilotDeviceLookup(
                    AutopilotDeviceLookupStatus.Found,
                    new AutopilotRegisteredDevice("simulated-autopilot-device", "Kiosk", "FOUNDRY-DEBUG", "user@contoso.com"))),
            DebugScenario.SeveralMatches => new RegistrationContext(
                DefaultTags,
                false,
                new AutopilotDeviceLookup(AutopilotDeviceLookupStatus.Ambiguous, null)),
            _ => new RegistrationContext(DefaultTags, false, NotFound)
        };
    }

    /// <inheritdoc />
    public async Task<RegistrationOutcome> RegisterAsync(
        RegistrationRequest request,
        IProgress<AutopilotHardwareHashUploadProgress> progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);

        int attempt = Interlocked.Increment(ref _registerCount);
        string importId = "simulated-import-" + attempt.ToString(CultureInfo.InvariantCulture);

        progress.Report(new AutopilotHardwareHashUploadProgress("Uploading Autopilot hardware hash..."));
        await delay(StepDuration, ct).ConfigureAwait(false);
        if (scenario == DebugScenario.SessionExpired && attempt == 1)
        {
            throw new HttpRequestException("Simulated expired token.", null, HttpStatusCode.Unauthorized);
        }

        if (scenario == DebugScenario.DeadlineReached)
        {
            // The upload never ends: only the deadline (or a scenario change) stops it.
            await delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }

        progress.Report(new AutopilotHardwareHashUploadProgress("Waiting for Autopilot device visibility..."));
        await delay(StepDuration, ct).ConfigureAwait(false);
        string? failureCode = scenario switch
        {
            DebugScenario.UploadFailureOtherTenant => RegistrationFailureCodes.DeviceAssignedToOtherTenant,
            DebugScenario.UploadFailureForbidden => RegistrationFailureCodes.GraphForbidden,
            DebugScenario.UploadFailureGeneric => RegistrationFailureCodes.RegistrationFailed,
            _ => null
        };
        if (failureCode is not null)
        {
            return new RegistrationOutcome(false, failureCode, importId, null);
        }

        progress.Report(new AutopilotHardwareHashUploadProgress("Updating Autopilot device..."));
        await delay(StepDuration, ct).ConfigureAwait(false);
        return new RegistrationOutcome(true, null, importId, "simulated-autopilot-device");
    }

    private static AutopilotDeviceLookup NotFound => new(AutopilotDeviceLookupStatus.NotFound, null);

    private static string[] ManyTags() =>
        [.. Enumerable.Range(1, 40).Select(index => "Site" + index.ToString("00", CultureInfo.InvariantCulture) + "-Laptop")];
}
