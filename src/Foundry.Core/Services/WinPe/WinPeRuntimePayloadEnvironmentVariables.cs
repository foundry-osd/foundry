// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public static class WinPeRuntimePayloadEnvironmentVariables
{
    /// <summary>Enables the debug Foundry.WinPE payload; PostInstall and AutopilotInteractive follow it.</summary>
    public const string DebugWinPeEnable = "FOUNDRY_WINPE_DEBUG_WINPE";
    public const string DebugWinPeArchive = "FOUNDRY_WINPE_DEBUG_WINPE_ARCHIVE";
    public const string DebugWinPeProject = "FOUNDRY_WINPE_DEBUG_WINPE_PROJECT";
    public const string DebugPostInstallArchive = "FOUNDRY_WINPE_DEBUG_POSTINSTALL_ARCHIVE";
    public const string DebugPostInstallProject = "FOUNDRY_WINPE_DEBUG_POSTINSTALL_PROJECT";
    public const string DebugAutopilotInteractiveArchive = "FOUNDRY_WINPE_DEBUG_AUTOPILOTINTERACTIVE_ARCHIVE";
    public const string DebugAutopilotInteractiveProject = "FOUNDRY_WINPE_DEBUG_AUTOPILOTINTERACTIVE_PROJECT";
}
