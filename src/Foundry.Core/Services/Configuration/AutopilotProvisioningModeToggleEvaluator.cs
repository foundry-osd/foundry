// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public sealed record AutopilotProvisioningModeToggleResult(
    bool IsEnabled,
    AutopilotProvisioningMode Mode,
    bool RequiresConfirmation);

public static class AutopilotProvisioningModeToggleEvaluator
{
    public static AutopilotProvisioningModeToggleResult Evaluate(
        bool isEnabled,
        AutopilotProvisioningMode currentMode,
        AutopilotProvisioningMode requestedMode)
    {
        bool isRequestedModeActive = isEnabled && currentMode == requestedMode;
        return new AutopilotProvisioningModeToggleResult(
            !isRequestedModeActive,
            requestedMode,
            isEnabled && !isRequestedModeActive);
    }
}
