// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Services.Simulation;
using Foundry.AutopilotInteractive.Startup;
using Foundry.Core.Models.Autopilot;

namespace Foundry.AutopilotInteractive.Services;

/// <summary>
/// The external services and settings one run of the sign-in and registration pages uses.
/// </summary>
/// <param name="DeviceCodeClient">Device code sign-in endpoints.</param>
/// <param name="RegistrationService">Group tag discovery, device lookup and hardware hash import.</param>
/// <param name="ProfileAssignment">Wait for the deployment profile assignment.</param>
/// <param name="OfferAssignedUser">Whether the page offers the assigned user field.</param>
/// <param name="DeadlineUtc">Setup deadline passed by Foundry.PostInstall, or <see langword="null"/> without one.</param>
public sealed record AutopilotServiceSet(
    IDeviceCodeClient DeviceCodeClient,
    IRegistrationService RegistrationService,
    IProfileAssignmentService ProfileAssignment,
    bool OfferAssignedUser,
    DateTimeOffset? DeadlineUtc);

/// <summary>
/// Creates the service set of a run: the real services in production, a simulation in debug sessions.
/// </summary>
public interface IAutopilotServiceSetFactory
{
    /// <summary>
    /// Creates the services for a run.
    /// </summary>
    /// <param name="scenario">Debug scenario to simulate, or <see langword="null"/> for the default behavior.</param>
    /// <returns>The services of the run.</returns>
    AutopilotServiceSet Create(DebugScenario? scenario);
}

/// <summary>
/// Production factory: always returns the real services, which talk to Microsoft Entra ID and Microsoft Graph.
/// </summary>
/// <remarks>Never registered in debug safety mode, so a debug session cannot reach the real endpoints.</remarks>
/// <param name="deviceCodeClient">Real device code client.</param>
/// <param name="registrationService">Real registration service.</param>
/// <param name="profileAssignment">Real profile assignment waiter.</param>
/// <param name="configuration">Runtime configuration written by Foundry.Deploy.</param>
/// <param name="commandLine">Process arguments carrying the deadline.</param>
public sealed class LiveAutopilotServiceSetFactory(
    IDeviceCodeClient deviceCodeClient,
    IRegistrationService registrationService,
    IProfileAssignmentService profileAssignment,
    AutopilotInteractiveRuntimeConfiguration configuration,
    AutopilotInteractiveCommandLine commandLine) : IAutopilotServiceSetFactory
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException"><paramref name="scenario"/> is set: scenarios exist only in debug sessions.</exception>
    public AutopilotServiceSet Create(DebugScenario? scenario)
    {
        if (scenario is not null)
        {
            throw new InvalidOperationException("Debug scenarios are only available in debug safety mode.");
        }

        return new AutopilotServiceSet(
            deviceCodeClient,
            registrationService,
            profileAssignment,
            configuration.OfferAssignedUser,
            commandLine.DeadlineUtc);
    }
}
