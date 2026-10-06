// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

/// <summary>
/// Describes which ADK setup steps run, in order: uninstall registered bundles, install the ADK, then install the WinPE add-on.
/// </summary>
/// <param name="Action">The user-facing action that this plan implements.</param>
/// <param name="UninstallRegisteredBundles">Whether registered ADK and WinPE bundles are removed first.</param>
/// <param name="InstallAdk">Whether the ADK Deployment Tools setup runs.</param>
/// <param name="InstallWinPeAddon">Whether the WinPE add-on setup runs.</param>
public sealed record AdkSetupPlan(
    AdkSetupAction Action,
    bool UninstallRegisteredBundles,
    bool InstallAdk,
    bool InstallWinPeAddon)
{
    private static readonly AdkSetupPlan NoAction = new(AdkSetupAction.None, false, false, false);
    private static readonly AdkSetupPlan WinPeAddonInstall = new(AdkSetupAction.Install, false, false, true);
    private static readonly AdkSetupPlan FreshInstall = new(AdkSetupAction.Install, false, true, true);
    private static readonly AdkSetupPlan Reinstall = new(AdkSetupAction.Upgrade, true, true, true);

    /// <summary>
    /// Selects the setup steps for a detected ADK state.
    /// </summary>
    /// <remarks>
    /// ADK setup has been observed to refuse running over an existing bundle registration, so a compatible ADK only receives
    /// the missing WinPE add-on, and any registered ADK release other than the supported one is removed first even
    /// when its Deployment Tools folder is missing. A registered but unusable WinPE add-on keeps the manual repair path.
    /// </remarks>
    /// <param name="status">The detected installation status.</param>
    /// <returns>The setup plan for that status.</returns>
    public static AdkSetupPlan Create(AdkInstallationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (status.IsCompatible)
        {
            return !status.IsWinPeAddonInstalled && !status.IsWinPeAddonRegistered ? WinPeAddonInstall : NoAction;
        }

        if (status.IsInstalled
            || status.VersionRelation is AdkVersionRelation.BelowSupported or AdkVersionRelation.AboveSupported)
        {
            return Reinstall;
        }

        return FreshInstall;
    }
}
