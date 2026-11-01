// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.AutopilotInteractive.Services.Simulation;

/// <summary>
/// Simulated situations behind Debug › scenarios. Every external effect is simulated: no request reaches Microsoft Entra ID
/// or Microsoft Graph, and the hardware hash of the development machine is never read or uploaded.
/// </summary>
public enum DebugScenario
{
    /// <summary>Page 1 with a fake code and QR code; the sign-in never completes.</summary>
    SignInCode,

    /// <summary>Page 1 with a code that expires after 20 seconds, so the code and the QR code are renewed together.</summary>
    CodeRenewal,

    /// <summary>The code request fails twice for network reasons before a code is shown.</summary>
    NetworkWait,

    /// <summary>The first sign-in fails with a blocking error; Retry gets a code that stays pending.</summary>
    SignInError,

    /// <summary>Page 2 with many discovered group tags.</summary>
    ManyGroupTags,

    /// <summary>Page 2 without any discovered group tag.</summary>
    NoGroupTags,

    /// <summary>Page 2 after the group tag discovery failed.</summary>
    GroupTagDiscoveryFailure,

    /// <summary>Page 2 for a device already registered in Windows Autopilot.</summary>
    AlreadyRegistered,

    /// <summary>Page 2 when several Autopilot devices share the serial number.</summary>
    SeveralMatches,

    /// <summary>Page 2 with the assigned user field shown.</summary>
    AssignedUser,

    /// <summary>The upload succeeds and the profile wait runs until its stop time or Continue without waiting.</summary>
    ProfileWait,

    /// <summary>The upload succeeds and the profile is assigned after a short wait.</summary>
    ProfileAssigned,

    /// <summary>The upload succeeds and the profile wait times out after a short wait.</summary>
    ProfileTimeout,

    /// <summary>The upload fails: the device is registered in another tenant.</summary>
    UploadFailureOtherTenant,

    /// <summary>The upload fails: the account lacks the Intune permission (HTTP 403).</summary>
    UploadFailureForbidden,

    /// <summary>The upload fails for an unknown reason.</summary>
    UploadFailureGeneric,

    /// <summary>The first upload is rejected with HTTP 401, which returns to page 1.</summary>
    SessionExpired,

    /// <summary>The setup deadline is reached 30 seconds after the scenario starts, while the upload never ends.</summary>
    DeadlineReached,

    /// <summary>An earlier run completed the registration: the application exits with code 0 at once.</summary>
    CompletedGuard
}

/// <summary>
/// Lists the debug scenarios and their menu texts.
/// </summary>
public static class DebugScenarioCatalog
{
    /// <summary>Gets the scenario a debug session starts with.</summary>
    public const DebugScenario Default = DebugScenario.SignInCode;

    /// <summary>Gets every scenario in declaration order.</summary>
    public static IReadOnlyList<DebugScenario> All { get; } = Array.AsReadOnly(Enum.GetValues<DebugScenario>());

    /// <summary>
    /// Gets the resource key of a scenario's menu text.
    /// </summary>
    /// <param name="scenario">The scenario.</param>
    /// <returns><c>Debug.Scenario.&lt;name&gt;</c>.</returns>
    public static string ResourceKey(DebugScenario scenario) => $"Debug.Scenario.{scenario}";
}
