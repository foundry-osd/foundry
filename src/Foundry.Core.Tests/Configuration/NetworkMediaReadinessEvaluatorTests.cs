using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class NetworkMediaReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_WhenNetworkFeaturesAreDisabled_ReturnsReady()
    {
        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(new NetworkSettings());

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
                ProfileTemplatePath = @"C:\Missing\wired.xml"
            }
        };

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings);

        Assert.False(evaluation.IsNetworkConfigurationReady);
        Assert.False(evaluation.IsConnectProvisioningReady);
        Assert.True(evaluation.AreRequiredSecretsReady);
    }

    [Fact]
    public void Evaluate_WhenPersonalWifiSecretIsMissing_ReturnsSecretAndConnectNotReady()
    {
        NetworkSettings settings = CreatePersonalWifiSettings();

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings);

        Assert.True(evaluation.IsNetworkConfigurationReady);
        Assert.False(evaluation.IsConnectProvisioningReady);
        Assert.False(evaluation.AreRequiredSecretsReady);
    }

    [Fact]
    public void Evaluate_WhenPersonalWifiSecretIsValid_ReturnsReady()
    {
        NetworkSettings settings = CreatePersonalWifiSettings();

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings, "ValidPassphrase123");

        Assert.True(evaluation.IsNetworkConfigurationReady);
        Assert.True(evaluation.IsConnectProvisioningReady);
        Assert.True(evaluation.AreRequiredSecretsReady);
    }

    [Fact]
    public void Evaluate_WhenPersonalWifiSecretExistsOnlyOnSettings_ReturnsSecretAndConnectNotReady()
    {
        NetworkSettings settings = CreatePersonalWifiSettings() with
        {
            Wifi = CreatePersonalWifiSettings().Wifi with
            {
                Passphrase = "PersistedPassphrase123"
            }
        };

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings);

        Assert.True(evaluation.IsNetworkConfigurationReady);
        Assert.False(evaluation.IsConnectProvisioningReady);
        Assert.False(evaluation.AreRequiredSecretsReady);
    }

    [Fact]
    public void ApplyRequiredSecrets_WhenPersonalWifiSecretIsValid_ReturnsSettingsWithPassphrase()
    {
        NetworkSettings settings = CreatePersonalWifiSettings();

        NetworkSettings result = NetworkMediaReadinessEvaluator.ApplyRequiredSecrets(settings, "ValidPassphrase123");

        Assert.Equal("ValidPassphrase123", result.Wifi.Passphrase);
    }

    [Fact]
    public void Evaluate_WhenPersonalWifiSecretIsInvalid_ReturnsSecretAndConnectNotReady()
    {
        NetworkSettings settings = CreatePersonalWifiSettings();

        NetworkMediaReadinessEvaluation evaluation = NetworkMediaReadinessEvaluator.Evaluate(settings, "short");

        Assert.True(evaluation.IsNetworkConfigurationReady);
        Assert.False(evaluation.IsConnectProvisioningReady);
        Assert.False(evaluation.AreRequiredSecretsReady);
    }

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
