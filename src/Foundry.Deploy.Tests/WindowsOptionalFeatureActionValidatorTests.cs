// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class WindowsOptionalFeatureActionValidatorTests
{
    [Fact]
    public void TryNormalize_WhenActionsAreValid_CanonicalizesCatalogOrder()
    {
        DeployWindowsOptionalFeatureAction telnet = Action("TelnetClient", enable: false);
        DeployWindowsOptionalFeatureAction netFx3 = Action("NetFx3", enable: true);

        bool valid = WindowsOptionalFeatureActionValidator.TryNormalize(
            new DeployWindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                Actions = [telnet, netFx3]
            },
            out DeployWindowsOptionalFeatureSettings normalized,
            out string error);

        Assert.True(valid, error);
        Assert.Equal(
            [WindowsOptionalFeatureCatalog.FindByFeatureName("NetFx3")!.Id, WindowsOptionalFeatureCatalog.FindByFeatureName("TelnetClient")!.Id],
            normalized.Actions.Select(action => action.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("wf:unknown-feature")]
    public void TryNormalize_WhenActionIdIsInvalid_ReturnsFalse(string id)
    {
        bool valid = WindowsOptionalFeatureActionValidator.TryNormalize(
            new DeployWindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                Actions = [new DeployWindowsOptionalFeatureAction { Id = id, Enable = true }]
            },
            out _,
            out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryNormalize_WhenActionIsDuplicated_ReturnsFalse()
    {
        DeployWindowsOptionalFeatureAction action = Action("TelnetClient", enable: true);

        bool valid = WindowsOptionalFeatureActionValidator.TryNormalize(
            new DeployWindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                Actions = [action, action]
            },
            out _,
            out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryNormalize_WhenDisabledAncestorHasEnabledDescendant_ReturnsFalse()
    {
        bool valid = WindowsOptionalFeatureActionValidator.TryNormalize(
            new DeployWindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                Actions =
                [
                    Action("Microsoft-Hyper-V", enable: false),
                    Action("Microsoft-Hyper-V-Hypervisor", enable: true)
                ]
            },
            out _,
            out _);

        Assert.False(valid);
    }

    private static DeployWindowsOptionalFeatureAction Action(string featureName, bool enable)
    {
        return new DeployWindowsOptionalFeatureAction
        {
            Id = WindowsOptionalFeatureCatalog.FindByFeatureName(featureName)!.Id,
            Enable = enable
        };
    }
}
