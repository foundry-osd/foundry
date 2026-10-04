// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ProvisioningModeSelectionTests
{
    [Fact]
    public void EveryProvisioningSwitchHasOneActiveSelection()
    {
        foreach (ProvisioningSelection current in Enum.GetValues<ProvisioningSelection>())
        {
            foreach (ProvisioningSelection requested in Enum.GetValues<ProvisioningSelection>())
            {
                ProvisioningSelectionDecision decision = ProvisioningModeSelectionEvaluator.Evaluate(current, requested);
                Assert.Equal(current == requested ? ProvisioningSelection.None : requested, decision.Next);
                Assert.Equal(current != ProvisioningSelection.None && requested != ProvisioningSelection.None && current != requested,
                    decision.RequiresReplacementConfirmation);
            }
        }
    }
}
