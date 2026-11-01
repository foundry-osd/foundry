// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Startup;
using Foundry.Core.Models.Autopilot;

namespace Foundry.AutopilotInteractive.Services.Simulation;

/// <summary>
/// Debug factory: builds simulated services for a scenario. Registered only in debug safety mode, instead of the real
/// services.
/// </summary>
/// <param name="timeProvider">Clock and timers of the simulated waits.</param>
/// <param name="configuration">Runtime configuration (debug default when no file was passed).</param>
/// <param name="commandLine">Process arguments; their deadline applies unless the scenario simulates one.</param>
public sealed class SimulatedAutopilotServiceSetFactory(
    TimeProvider timeProvider,
    AutopilotInteractiveRuntimeConfiguration configuration,
    AutopilotInteractiveCommandLine commandLine) : IAutopilotServiceSetFactory
{
    /// <summary>Time between the start of <see cref="DebugScenario.DeadlineReached"/> and the moment long waits must stop.</summary>
    public static readonly TimeSpan SimulatedTimeBeforeDeadline = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    /// <remarks><see langword="null"/> means <see cref="DebugScenarioCatalog.Default"/>.</remarks>
    public AutopilotServiceSet Create(DebugScenario? scenario)
    {
        DebugScenario active = scenario ?? DebugScenarioCatalog.Default;
        Func<TimeSpan, CancellationToken, Task> delay = (duration, ct) => Task.Delay(duration, timeProvider, ct);
        DateTimeOffset? deadline = active == DebugScenario.DeadlineReached
            ? timeProvider.GetUtcNow() + SimulatedTimeBeforeDeadline + DeadlinePolicy.Margin
            : commandLine.DeadlineUtc;

        return new AutopilotServiceSet(
            new SimulatedDeviceCodeClient(active, timeProvider),
            new SimulatedRegistrationService(active, delay),
            new SimulatedProfileAssignment(active, delay, timeProvider.GetUtcNow),
            configuration.OfferAssignedUser || active == DebugScenario.AssignedUser,
            deadline);
    }
}
