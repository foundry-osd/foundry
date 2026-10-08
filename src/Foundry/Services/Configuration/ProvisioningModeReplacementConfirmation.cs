// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Services.Configuration;

/// <summary>Asks the same replacement question on every Autopilot and Domain Join page, naming both modes.</summary>
internal static class ProvisioningModeReplacementConfirmation
{
    /// <summary>Returns whether the user accepted replacing <paramref name="current"/> with <paramref name="requested"/>.</summary>
    public static Task<bool> ConfirmAsync(IDialogService dialogs, IApplicationLocalizationService localization,
        ProvisioningSelection current, ProvisioningSelection requested) =>
        dialogs.ConfirmAsync(new ConfirmationDialogRequest(
            localization.GetString("Autopilot.ModeSwitchConfirmationTitle"),
            localization.FormatString(
                "Autopilot.ModeSwitchConfirmationMessageFormat",
                GetDisplayName(localization, current),
                GetDisplayName(localization, requested)),
            localization.GetString("Autopilot.ModeSwitchConfirmationPrimaryButton"),
            localization.GetString("Common.Cancel"),
            IsPrimaryButtonAccent: true));

    private static string GetDisplayName(IApplicationLocalizationService localization, ProvisioningSelection selection) =>
        localization.GetString(selection switch
        {
            ProvisioningSelection.AutopilotJsonProfile => "Autopilot.JsonProfileHeader",
            ProvisioningSelection.AutopilotHardwareHashUpload => "Autopilot.HardwareHashHeader",
            ProvisioningSelection.AutopilotInteractiveHardwareHashUpload => "Autopilot.InteractiveHardwareHashHeader",
            ProvisioningSelection.DomainJoinInteractive => "InteractiveDomainJoinPageHeader.Title",
            ProvisioningSelection.DomainJoinAutomatic => "ZeroTouchDomainJoinPageHeader.Title",
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        });
}
