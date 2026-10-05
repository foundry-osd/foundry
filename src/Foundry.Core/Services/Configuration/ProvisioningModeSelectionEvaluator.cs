// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Models a single mutually exclusive provisioning choice.</summary>
public enum ProvisioningSelection
{
    None, AutopilotJsonProfile, AutopilotHardwareHashUpload, AutopilotInteractiveHardwareHashUpload,
    DomainJoinInteractive, DomainJoinAutomatic
}

/// <summary>Describes the proposed choice; callers confirm replacement before applying it.</summary>
public sealed record ProvisioningSelectionDecision(ProvisioningSelection Next, bool RequiresReplacementConfirmation);

/// <summary>Evaluates technician selection without mutating saved settings.</summary>
public static class ProvisioningModeSelectionEvaluator
{
    /// <summary>Maps the active configuration to the shared policy while ignoring inactive nonsecret drafts.</summary>
    public static ProvisioningSelectionDecision Evaluate(AutopilotSettings autopilot, DomainJoinSettings domainJoin, ProvisioningSelection requested) =>
        Evaluate(GetCurrent(autopilot, domainJoin), requested);

    /// <summary>Resolves the single active choice, rejecting documents that enable both families.</summary>
    public static ProvisioningSelection GetCurrent(AutopilotSettings autopilot, DomainJoinSettings domainJoin)
    {
        DomainJoinConfigurationValidator.ThrowIfProvisioningModesConflict(autopilot, domainJoin);
        if (autopilot.IsEnabled)
        {
            return autopilot.ProvisioningMode switch
            {
                AutopilotProvisioningMode.JsonProfile => ProvisioningSelection.AutopilotJsonProfile,
                AutopilotProvisioningMode.HardwareHashUpload => ProvisioningSelection.AutopilotHardwareHashUpload,
                AutopilotProvisioningMode.InteractiveHardwareHashUpload => ProvisioningSelection.AutopilotInteractiveHardwareHashUpload,
                _ => throw new ArgumentOutOfRangeException(nameof(autopilot))
            };
        }

        if (domainJoin.IsEnabled)
        {
            return domainJoin.Mode switch
            {
                DomainJoinMode.Interactive => ProvisioningSelection.DomainJoinInteractive,
                DomainJoinMode.Automatic => ProvisioningSelection.DomainJoinAutomatic,
                _ => throw new ArgumentOutOfRangeException(nameof(domainJoin))
            };
        }

        return ProvisioningSelection.None;
    }

    /// <summary>Toggling the active choice disables it; replacing an active choice requires confirmation.</summary>
    public static ProvisioningSelectionDecision Evaluate(ProvisioningSelection current, ProvisioningSelection requested)
    {
        if (!Enum.IsDefined(current)) throw new ArgumentOutOfRangeException(nameof(current));
        if (!Enum.IsDefined(requested)) throw new ArgumentOutOfRangeException(nameof(requested));
        return new(current == requested ? ProvisioningSelection.None : requested,
            current != ProvisioningSelection.None && requested != ProvisioningSelection.None && current != requested);
    }
}
