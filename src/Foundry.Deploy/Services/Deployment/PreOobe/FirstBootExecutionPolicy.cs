// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Chooses only setup hooks whose licensing and action-stage capabilities are established.</summary>
public static class FirstBootExecutionPolicy
{
    public static FirstBootExecutionPlan Evaluate(string editionId, string licenseChannel,
        bool customAnswer, bool customSpecializeLauncherPresent, bool effectiveKeyChannelKnown,
        bool needsCustomization, bool needsInteractiveRegistration, bool areSpecializeActionsQualified = false)
    {
        bool editionExempt = editionId.ToUpperInvariant() is "ENTERPRISE" or "ENTERPRISEN" or "ENTERPRISES" or
            "ENTERPRISESN" or "ENTERPRISEEVAL" or "ENTERPRISENEVAL" or "SERVERSTANDARD" or "SERVERDATACENTER" or
            "SERVERSTANDARDEVAL" or "SERVERDATACENTEREVAL";
        bool setupHookEligible = editionExempt || effectiveKeyChannelKnown &&
            licenseChannel.ToUpperInvariant() is "RETAIL" or "VOLUME" or "VOLUME:MAK" or "VOLUME:GVLK";
        if (!needsCustomization && !needsInteractiveRegistration)
        {
            return new(FirstBootEntryPoint.None, setupHookEligible, null);
        }
        if (needsInteractiveRegistration && !setupHookEligible)
        {
            return Unsupported();
        }

        FirstBootEntryPoint entryPoint = FirstBootEntryPoint.None;
        if (needsCustomization)
        {
            if (areSpecializeActionsQualified && (!customAnswer || customSpecializeLauncherPresent))
            {
                entryPoint = customAnswer ? FirstBootEntryPoint.VerifiedCustomSpecialize : FirstBootEntryPoint.GeneratedSpecialize;
            }
            else if (setupHookEligible)
            {
                entryPoint = FirstBootEntryPoint.SetupComplete;
            }
            else
            {
                return Unsupported();
            }
        }
        return new(entryPoint, setupHookEligible, null);
    }

    private static FirstBootExecutionPlan Unsupported() => new(FirstBootEntryPoint.None, false, "unsupported_setup_hook");
}
