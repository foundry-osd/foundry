// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.AutopilotInteractive.Services.Authentication;

namespace Foundry.AutopilotInteractive.Services.Simulation;

/// <summary>
/// <see cref="IDeviceCodeClient"/> for debug scenarios: issues fake codes and answers polls without any network request.
/// </summary>
/// <remarks>
/// The user codes (<c>FDRYDBGnn</c>) and the token are obviously fake, so a screenshot never shows a usable code. The
/// verification address is the real public page; nothing is sent to it.
/// </remarks>
/// <param name="scenario">Scenario that decides the answers.</param>
/// <param name="timeProvider">Clock used for the code lifetimes.</param>
public sealed class SimulatedDeviceCodeClient(DebugScenario scenario, TimeProvider timeProvider) : IDeviceCodeClient
{
    /// <summary>Lifetime of a code in the <see cref="DebugScenario.CodeRenewal"/> scenario.</summary>
    public static readonly TimeSpan ShortCodeLifetime = TimeSpan.FromSeconds(20);

    /// <summary>Polling interval of the simulated codes.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly Uri VerificationUri = new("https://microsoft.com/devicelogin");
    private const int NetworkFailureCount = 2;

    private int _requestCount;

    /// <inheritdoc />
    public Task<DeviceCodeSession> RequestAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int request = Interlocked.Increment(ref _requestCount);
        if (scenario == DebugScenario.NetworkWait && request <= NetworkFailureCount)
        {
            throw new DeviceCodeRequestException(isTransient: true, errorCode: null);
        }

        TimeSpan lifetime = scenario == DebugScenario.CodeRenewal ? ShortCodeLifetime : CodeLifetime;
        string userCode = "FDRYDBG" + (request % 100).ToString("00", CultureInfo.InvariantCulture);
        return Task.FromResult(new DeviceCodeSession(
            "simulated-device-code-" + request.ToString(CultureInfo.InvariantCulture),
            userCode,
            VerificationUri,
            timeProvider.GetUtcNow() + lifetime,
            PollInterval));
    }

    /// <inheritdoc />
    public Task<DeviceCodePollResult> PollAsync(DeviceCodeSession session, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DeviceCodePollResult result = scenario switch
        {
            DebugScenario.SignInCode or DebugScenario.CodeRenewal or DebugScenario.NetworkWait =>
                new DeviceCodePollResult(DeviceCodePollStatus.Pending),

            // The first code fails as a Conditional Access block would; the code requested by Retry stays pending.
            DebugScenario.SignInError => Volatile.Read(ref _requestCount) == 1
                ? new DeviceCodePollResult(DeviceCodePollStatus.Failed, ErrorCode: "invalid_grant")
                : new DeviceCodePollResult(DeviceCodePollStatus.Pending),

            _ => new DeviceCodePollResult(
                DeviceCodePollStatus.Succeeded,
                new AuthenticatedSession("simulated-access-token", timeProvider.GetUtcNow().AddHours(1), "technician@contoso.com"))
        };
        return Task.FromResult(result);
    }
}
