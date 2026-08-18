// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConfigurationNavigationTargetResolverTests
{
    [Theory]
    [InlineData(AutopilotProvisioningMode.JsonProfile, ConfigurationNavigationTarget.AutopilotJsonProfile)]
    [InlineData(AutopilotProvisioningMode.HardwareHashUpload, ConfigurationNavigationTarget.AutopilotHardwareHashUpload)]
    [InlineData(AutopilotProvisioningMode.InteractiveHardwareHashUpload, ConfigurationNavigationTarget.AutopilotInteractiveHardwareHashUpload)]
    public void ResolveAutopilot_ReturnsPageForSelectedMode(
        AutopilotProvisioningMode mode,
        ConfigurationNavigationTarget expected)
    {
        Assert.Equal(expected, ConfigurationNavigationTargetResolver.ResolveAutopilot(mode));
    }

    [Theory]
    [InlineData(AutopilotProvisioningMode.JsonProfile, ConfigurationNavigationTarget.AutopilotJsonProfile)]
    [InlineData(AutopilotProvisioningMode.HardwareHashUpload, ConfigurationNavigationTarget.AutopilotHardwareHashUpload)]
    [InlineData(AutopilotProvisioningMode.InteractiveHardwareHashUpload, ConfigurationNavigationTarget.AutopilotInteractiveHardwareHashUpload)]
    public void ResolveDeployFailure_ReturnsActiveAutopilotPage(
        AutopilotProvisioningMode mode,
        ConfigurationNavigationTarget expected)
    {
        Assert.Equal(expected, ConfigurationNavigationTargetResolver.ResolveDeployFailure(mode));
    }

    [Fact]
    public void ResolveRequiredNetworkSecret_ReturnsWifiPage()
    {
        Assert.Equal(
            ConfigurationNavigationTarget.Wifi,
            ConfigurationNavigationTargetResolver.ResolveRequiredNetworkSecret());
    }

    [Theory]
    [InlineData(NetworkConfigurationValidationCode.WifiProvisioningRequired, ConfigurationNavigationTarget.Wifi)]
    [InlineData(NetworkConfigurationValidationCode.WifiSsidRequired, ConfigurationNavigationTarget.Wifi)]
    [InlineData(NetworkConfigurationValidationCode.UnsupportedWifiSecurityType, ConfigurationNavigationTarget.Wifi)]
    [InlineData(NetworkConfigurationValidationCode.WifiEnterpriseCertificateMissing, ConfigurationNavigationTarget.Wifi)]
    [InlineData(NetworkConfigurationValidationCode.WiredProfileTemplateRequired, ConfigurationNavigationTarget.EthernetDot1x)]
    [InlineData(NetworkConfigurationValidationCode.WiredCertificateMissing, ConfigurationNavigationTarget.EthernetDot1x)]
    [InlineData(NetworkConfigurationValidationCode.None, ConfigurationNavigationTarget.None)]
    public void ResolveNetwork_ReturnsPageForValidationCode(
        NetworkConfigurationValidationCode validationCode,
        ConfigurationNavigationTarget expected)
    {
        Assert.Equal(expected, ConfigurationNavigationTargetResolver.ResolveNetwork(validationCode));
    }
}
