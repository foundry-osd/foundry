// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ProvisioningModeSelectionTests
{
    [Theory]
    [InlineData(AutopilotProvisioningMode.JsonProfile, ProvisioningSelection.AutopilotJsonProfile)]
    [InlineData(AutopilotProvisioningMode.HardwareHashUpload, ProvisioningSelection.AutopilotHardwareHashUpload)]
    [InlineData(AutopilotProvisioningMode.InteractiveHardwareHashUpload, ProvisioningSelection.AutopilotInteractiveHardwareHashUpload)]
    public void ActiveAutopilotSettingsToggleTheirOwnSelectionOff(AutopilotProvisioningMode mode, ProvisioningSelection requested)
    {
        var autopilot = new AutopilotSettings { IsEnabled = true, ProvisioningMode = mode };
        var domainJoin = new DomainJoinSettings { Mode = DomainJoinMode.Automatic };
        ProvisioningSelectionDecision decision = ProvisioningModeSelectionEvaluator.Evaluate(autopilot, domainJoin, requested);
        Assert.Equal(ProvisioningSelection.None, decision.Next);
        Assert.False(decision.RequiresReplacementConfirmation);
    }

    [Theory]
    [InlineData(DomainJoinMode.Interactive, ProvisioningSelection.DomainJoinInteractive)]
    [InlineData(DomainJoinMode.Automatic, ProvisioningSelection.DomainJoinAutomatic)]
    public void ActiveDomainSettingsToggleTheirOwnSelectionOff(DomainJoinMode mode, ProvisioningSelection requested)
    {
        var domainJoin = new DomainJoinSettings { IsEnabled = true, Mode = mode };
        ProvisioningSelectionDecision decision = ProvisioningModeSelectionEvaluator.Evaluate(new(), domainJoin, requested);
        Assert.Equal(ProvisioningSelection.None, decision.Next);
        Assert.False(decision.RequiresReplacementConfirmation);
    }

    [Fact]
    public void ReplacingAutopilotWithDomainRequiresConfirmation()
    {
        ProvisioningSelectionDecision decision = ProvisioningModeSelectionEvaluator.Evaluate(
            new() { IsEnabled = true, ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload },
            new(), ProvisioningSelection.DomainJoinInteractive);
        Assert.Equal(ProvisioningSelection.DomainJoinInteractive, decision.Next);
        Assert.True(decision.RequiresReplacementConfirmation);
    }

    [Fact]
    public void ReplacingDomainWithAutopilotRequiresConfirmation()
    {
        ProvisioningSelectionDecision decision = ProvisioningModeSelectionEvaluator.Evaluate(
            new(), new() { IsEnabled = true, Mode = DomainJoinMode.Automatic }, ProvisioningSelection.AutopilotInteractiveHardwareHashUpload);
        Assert.Equal(ProvisioningSelection.AutopilotInteractiveHardwareHashUpload, decision.Next);
        Assert.True(decision.RequiresReplacementConfirmation);
    }

    [Fact]
    public void InactiveDraftModesDoNotRequireReplacementConfirmation()
    {
        ProvisioningSelectionDecision decision = ProvisioningModeSelectionEvaluator.Evaluate(
            new() { ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload },
            new() { Mode = DomainJoinMode.Automatic }, ProvisioningSelection.DomainJoinInteractive);
        Assert.Equal(ProvisioningSelection.DomainJoinInteractive, decision.Next);
        Assert.False(decision.RequiresReplacementConfirmation);
    }

    [Fact]
    public void ContradictorySettingsAreRejectedBeforeSelection()
    {
        Assert.Throws<InvalidOperationException>(() => ProvisioningModeSelectionEvaluator.Evaluate(
            new() { IsEnabled = true }, new() { IsEnabled = true }, ProvisioningSelection.DomainJoinAutomatic));
    }

    [Fact]
    public void NoEnabledModeResolvesToNoSelection()
    {
        Assert.Equal(ProvisioningSelection.None, ProvisioningModeSelectionEvaluator.GetCurrent(
            new() { ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload }, new() { Mode = DomainJoinMode.Automatic }));
    }
}
