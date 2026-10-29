// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class NetworkMediaReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenNetworkFeaturesAreDisabled_ReturnsReady()
    {
        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(new NetworkSettings(), MediaInputs.None, isMediaProtected: false);

        Assert.True(evaluation.IsNetworkConfigurationReady);
        Assert.True(evaluation.IsConnectProvisioningReady);
        Assert.True(evaluation.AreRequiredSecretsReady);
    }

    [Fact]
    public void Evaluate_WhenWiredProfileIsMissing_ReturnsNetworkAndConnectNotReady()
    {
        NetworkSettings settings = new()
        {
            Dot1x = new Dot1xSettings
            {
                IsEnabled = true,
                ProfileTemplate = TestFileReferences.Detached("wired.xml")
            }
        };

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings, MediaInputs.None, isMediaProtected: false);

        Assert.False(evaluation.IsNetworkConfigurationReady);
        Assert.False(evaluation.IsConnectProvisioningReady);
        Assert.True(evaluation.AreRequiredSecretsReady);
        Assert.Equal(NetworkConfigurationValidationCode.WiredProfileTemplateMissing, evaluation.NetworkConfigurationValidationCode);
    }

    [Fact]
    public void Evaluate_WhenWifiValidationFailsBeforeEnabledWiredConfiguration_ExposesWifiCode()
    {
        var settings = new NetworkSettings
        {
            WifiProvisioned = false,
            Wifi = new WifiSettings { IsEnabled = true },
            Dot1x = new Dot1xSettings { IsEnabled = true }
        };

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings, MediaInputs.None, isMediaProtected: false);

        Assert.False(evaluation.IsNetworkConfigurationReady);
        Assert.Equal(NetworkConfigurationValidationCode.WifiProvisioningRequired, evaluation.NetworkConfigurationValidationCode);
    }

    [Fact]
    public void Evaluate_WhenPersonalWifiSecretIsMissing_ReturnsSecretAndConnectNotReady()
    {
        NetworkSettings settings = CreatePersonalWifiSettings();

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings, MediaInputs.None, isMediaProtected: true);

        Assert.True(evaluation.IsNetworkConfigurationReady);
        Assert.False(evaluation.IsConnectProvisioningReady);
        Assert.False(evaluation.AreRequiredSecretsReady);
    }

    [Fact]
    public void Evaluate_WhenPersonalWifiSecretIsValid_ReturnsReady()
    {
        NetworkSettings settings = CreatePersonalWifiSettings();

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings, WithPassphrase("ValidPassphrase123"), isMediaProtected: true);

        Assert.True(evaluation.IsNetworkConfigurationReady);
        Assert.True(evaluation.IsConnectProvisioningReady);
        Assert.True(evaluation.AreRequiredSecretsReady);
    }

    [Fact]
    public void Evaluate_WhenPersonalWifiSecretIsInvalid_ReturnsSecretAndConnectNotReady()
    {
        NetworkSettings settings = CreatePersonalWifiSettings();

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings, WithPassphrase("short"), isMediaProtected: true);

        Assert.True(evaluation.IsNetworkConfigurationReady);
        Assert.False(evaluation.IsConnectProvisioningReady);
        Assert.False(evaluation.AreRequiredSecretsReady);
    }

    [Fact]
    public void Evaluate_PersonalWifiWithoutProtection_IsNotReadyForMedia()
    {
        NetworkSettings settings = CreatePersonalWifiSettings();
        MediaInputs inputs = WithPassphrase("lab-passphrase");

        NetworkMediaReadinessEvaluation unprotected = NetworkMediaReadinessEvaluator.Evaluate(settings, inputs, isMediaProtected: false);
        NetworkMediaReadinessEvaluation protectedMedia = NetworkMediaReadinessEvaluator.Evaluate(settings, inputs, isMediaProtected: true);

        Assert.False(unprotected.IsConnectProvisioningReady);
        Assert.Equal(NetworkConfigurationValidationCode.WifiMediaProtectionRequired, unprotected.NetworkConfigurationValidationCode);
        Assert.True(protectedMedia.IsConnectProvisioningReady);
    }

    private static MediaInputs WithPassphrase(string passphrase) =>
        MediaInputs.None with { Network = new NetworkSecrets { WifiPassphrase = passphrase } };

    private static NetworkSettings CreatePersonalWifiSettings()
    {
        return new NetworkSettings
        {
            WifiProvisioned = true,
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Foundry",
                SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal
            }
        };
    }
}
