// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class AutopilotProvisioningModeToggleEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenRequestedModeIsActive_DisablesItWithoutConfirmation()
    {
        AutopilotProvisioningModeToggleResult result = AutopilotProvisioningModeToggleEvaluator.Evaluate(
            isEnabled: true,
            AutopilotProvisioningMode.JsonProfile,
            AutopilotProvisioningMode.JsonProfile);

        Assert.False(result.IsEnabled);
        Assert.Equal(AutopilotProvisioningMode.JsonProfile, result.Mode);
        Assert.False(result.RequiresConfirmation);
    }

    [Fact]
    public void Evaluate_WhenAutopilotIsDisabled_EnablesRequestedModeWithoutConfirmation()
    {
        AutopilotProvisioningModeToggleResult result = AutopilotProvisioningModeToggleEvaluator.Evaluate(
            isEnabled: false,
            AutopilotProvisioningMode.JsonProfile,
            AutopilotProvisioningMode.HardwareHashUpload);

        Assert.True(result.IsEnabled);
        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, result.Mode);
        Assert.False(result.RequiresConfirmation);
    }

    [Fact]
    public void Evaluate_WhenAnotherModeIsActive_RequiresConfirmationBeforeReplacement()
    {
        AutopilotProvisioningModeToggleResult result = AutopilotProvisioningModeToggleEvaluator.Evaluate(
            isEnabled: true,
            AutopilotProvisioningMode.JsonProfile,
            AutopilotProvisioningMode.InteractiveHardwareHashUpload);

        Assert.True(result.IsEnabled);
        Assert.Equal(AutopilotProvisioningMode.InteractiveHardwareHashUpload, result.Mode);
        Assert.True(result.RequiresConfirmation);
    }
}
