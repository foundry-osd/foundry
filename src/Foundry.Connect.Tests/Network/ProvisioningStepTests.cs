// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using Foundry.Connect.Configuration;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Configuration;
using Foundry.Utilities.Networking;

namespace Foundry.Connect.Tests.Network;

public sealed class ProvisioningStepTests
{
    private static readonly Guid WifiA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid WifiB = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    [Fact]
    public async Task RunAsync_RunsOncePerProcess()
    {
        using var directory = new TemporaryDirectory();
        var wired = new RecordingWiredClient();
        ProvisioningStep step = CreateStep(directory, WiredConfiguration(directory), ConnectRuntimeSecrets.None(), wired: wired);

        ProvisioningOutcome first = await step.RunAsync(TestContext.Current.CancellationToken);
        ProvisioningOutcome second = await step.RunAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Single(wired.AddedProfiles);
    }

    [Fact]
    public async Task Wired_ReconnectsEveryLinkedEthernetInterface()
    {
        using var directory = new TemporaryDirectory();
        var wired = new RecordingWiredClient();
        NetworkAdapterSnapshot[] adapters =
        [
            Adapter("Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up),
            Adapter("Ethernet 2", NetworkInterfaceType.GigabitEthernet, OperationalStatus.Up),
            Adapter("Ethernet 3", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
            Adapter("Wi-Fi", NetworkInterfaceType.Wireless80211, OperationalStatus.Up)
        ];

        ProvisioningOutcome outcome = await CreateStep(directory, WiredConfiguration(directory), ConnectRuntimeSecrets.None(), wired: wired, adapters: adapters)
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Ethernet", "Ethernet 2"], wired.Reconnected);
        Assert.Equal(Dot1xAuthenticationStatus.ProfileApplied, outcome.Wired.Status);
        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public async Task Wired_WhenTemplateIsMissing_IsFailedWithoutNetsh()
    {
        using var directory = new TemporaryDirectory();
        var wired = new RecordingWiredClient();
        FoundryConnectConfigurationDocument configuration = WiredConfiguration(directory) with
        {
            Dot1x = new ConnectDot1xSettings { IsEnabled = true, ProfileTemplatePath = "Network\\missing.xml" }
        };

        ProvisioningOutcome outcome = await CreateStep(directory, configuration, ConnectRuntimeSecrets.None(), wired: wired)
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Dot1xState(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileTemplateMissing), outcome.Wired);
        Assert.Equal([NetworkFailureCode.WiredProfileTemplateMissing], outcome.Failures);
        Assert.Empty(wired.AddedProfiles);
    }

    [Fact]
    public async Task Wired_WhenPfxPasswordIsUnavailable_IsUnavailableAndSkipsProfile()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("Network\\client.pfx", [1, 2, 3]);
        var wired = new RecordingWiredClient();
        FoundryConnectConfigurationDocument configuration = WiredConfiguration(directory) with
        {
            Dot1x = new ConnectDot1xSettings
            {
                IsEnabled = true,
                ProfileTemplatePath = "Network\\wired.xml",
                CertificatePath = "Network\\client.pfx",
                CertificatePfxPasswordSecret = new SecretEnvelope()
            }
        };
        var secrets = new ConnectRuntimeSecrets(
            ConnectSecret.NotProvisioned(),
            ConnectSecret.NotProvisioned(),
            ConnectSecret.Unavailable(ConnectSecretFailure.MediaKeyMissing));

        ProvisioningOutcome outcome = await CreateStep(directory, configuration, secrets, wired: wired).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Dot1xState(Dot1xAuthenticationStatus.Unavailable, Dot1xFailureReason.SecretUnavailable), outcome.Wired);
        Assert.Contains(NetworkFailureCode.WiredSecretUnavailable, outcome.Failures);
        Assert.Empty(wired.AddedProfiles);
    }

    [Fact]
    public async Task Wired_ClearsPfxPasswordAfterImport()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("Network\\client.pfx", [1, 2, 3]);
        var certificates = new RecordingCertificateInstaller();
        var secrets = new ConnectRuntimeSecrets(
            ConnectSecret.NotProvisioned(),
            ConnectSecret.NotProvisioned(),
            ConnectSecret.Available("wired-password".ToCharArray()));
        FoundryConnectConfigurationDocument configuration = WiredConfiguration(directory) with
        {
            Dot1x = new ConnectDot1xSettings
            {
                IsEnabled = true,
                ProfileTemplatePath = "Network\\wired.xml",
                CertificatePath = "Network\\client.pfx",
                CertificatePfxPasswordSecret = new SecretEnvelope()
            }
        };

        await CreateStep(directory, configuration, secrets, certificates: certificates).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("wired-password", Assert.Single(certificates.Calls).Password);
        Assert.Equal(ConnectSecretStatus.Cleared, secrets.WiredCertificatePfxPassword.Status);
    }

    [Fact]
    public async Task Wired_WhenClientThrows_IsFailedAndWifiContinues()
    {
        using var directory = new TemporaryDirectory();
        var wired = new RecordingWiredClient { AddException = new InvalidOperationException("netsh exploded") };
        FoundryConnectConfigurationDocument configuration = WiredConfiguration(directory) with
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = PersonalWifiConfiguration().Wifi
        };
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Available("correct horse".ToCharArray()), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

        ProvisioningOutcome outcome = await CreateStep(directory, configuration, secrets, wired: wired).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Dot1xState(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileImportFailed), outcome.Wired);
        Assert.Equal([NetworkFailureCode.Unexpected], outcome.Failures);
        Assert.Equal(ProvisionedWifiStatus.Ready, outcome.Wifi.Status);
    }

    [Fact]
    public async Task Wifi_Personal_SetsProfileOnEveryInterfaceAndClearsPassphrase()
    {
        using var directory = new TemporaryDirectory();
        var wlan = new FakeWlanClient { Snapshot = new(WifiAvailability.Available, [FakeWlanClient.Interface(WifiA), FakeWlanClient.Interface(WifiB)], []) };
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Available("correct horse".ToCharArray()), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

        ProvisioningOutcome outcome = await CreateStep(directory, PersonalWifiConfiguration(), secrets, wlan: wlan).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProvisionedWifiStatus.Ready, outcome.Wifi.Status);
        Assert.Equal(WifiSsid.FromText("Lab"), outcome.Wifi.Ssid);
        Assert.Equal("Lab", outcome.Wifi.ProfileName);
        Assert.Equal([WifiA, WifiB], wlan.SetProfileCalls.Select(static call => call.InterfaceId));
        Assert.Equal(ConnectSecretStatus.Cleared, secrets.WifiPassphrase.Status);
        Assert.NotNull(outcome.WifiProfile);
    }

    [Fact]
    public async Task Wifi_WhenNoAdapterYet_IsReadyWithProfileDocument()
    {
        using var directory = new TemporaryDirectory();
        var wlan = new FakeWlanClient { Snapshot = new(WifiAvailability.NoAdapter, [], []) };
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Available("correct horse".ToCharArray()), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

        ProvisioningOutcome outcome = await CreateStep(directory, PersonalWifiConfiguration(), secrets, wlan: wlan).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProvisionedWifiStatus.Ready, outcome.Wifi.Status);
        Assert.Empty(wlan.SetProfileCalls);
        Assert.Equal("Lab", outcome.WifiProfile!.Name);
    }

    [Fact]
    public async Task Wifi_WhenProfileIsRejected_StaysReadyWithOneFailure()
    {
        using var directory = new TemporaryDirectory();
        var wlan = new FakeWlanClient
        {
            Snapshot = new(WifiAvailability.Available, [FakeWlanClient.Interface(WifiA), FakeWlanClient.Interface(WifiB)], []),
            SetProfileResult = new WlanOperationResult(WlanOperationStatus.ProfileRejected, 1206, 524289)
        };
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Available("correct horse".ToCharArray()), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

        ProvisioningOutcome outcome = await CreateStep(directory, PersonalWifiConfiguration(), secrets, wlan: wlan).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProvisionedWifiStatus.Ready, outcome.Wifi.Status);
        Assert.Equal([NetworkFailureCode.WifiProfileRejected], outcome.Failures);
        Assert.Equal(2, wlan.SetProfileCalls.Count);
    }

    [Fact]
    public async Task Wifi_WhenPassphraseIsUnavailable_IsUnavailableButWiredContinues()
    {
        using var directory = new TemporaryDirectory();
        var wired = new RecordingWiredClient();
        FoundryConnectConfigurationDocument configuration = WiredConfiguration(directory) with
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = PersonalWifiConfiguration().Wifi
        };
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Unavailable(ConnectSecretFailure.DecryptionFailed), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

        ProvisioningOutcome outcome = await CreateStep(directory, configuration, secrets, wired: wired).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ProvisionedWifiState(ProvisionedWifiStatus.Unavailable, ProvisionedWifiUnavailableReason.SecretUnavailable), outcome.Wifi with { Ssid = null, ProfileName = null });
        Assert.Contains(NetworkFailureCode.WifiSecretUnavailable, outcome.Failures);
        Assert.Equal(Dot1xAuthenticationStatus.ProfileApplied, outcome.Wired.Status);
        Assert.Null(outcome.WifiProfile);
    }

    [Fact]
    public async Task Wifi_WhenPassphraseIsInvalid_IsUnavailableWithInvalidPassphrase()
    {
        using var directory = new TemporaryDirectory();
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Available("short".ToCharArray()), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

        ProvisioningOutcome outcome = await CreateStep(directory, PersonalWifiConfiguration(), secrets).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProvisionedWifiUnavailableReason.InvalidPassphrase, outcome.Wifi.Reason);
        Assert.Equal([NetworkFailureCode.InvalidPassphrase], outcome.Failures);
    }

    [Fact]
    public async Task Wifi_Enterprise_UsesTemplateIdentity()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("Network\\corp.xml", """
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>Corp Profile</name>
              <SSIDConfig><SSID><hex>436F7270</hex></SSID><nonBroadcast>true</nonBroadcast></SSIDConfig>
            </WLANProfile>
            """);
        FoundryConnectConfigurationDocument configuration = new()
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings { IsEnabled = true, HasEnterpriseProfile = true, EnterpriseProfileTemplatePath = "Network\\corp.xml" }
        };

        ProvisioningOutcome outcome = await CreateStep(directory, configuration, ConnectRuntimeSecrets.None()).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new ProvisionedWifiState(ProvisionedWifiStatus.Ready, ProvisionedWifiUnavailableReason.None, WifiSsid.FromText("Corp"), "Corp Profile", IsEnterprise: true, IsHidden: true),
            outcome.Wifi);
    }

    [Fact]
    public async Task Wifi_Enterprise_WhenTemplateSsidIsUnreadable_IsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("Network\\corp.xml", """
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>Corp Profile</name>
              <SSIDConfig><SSID><hex>43ZZ</hex><name>Corp</name></SSID></SSIDConfig>
            </WLANProfile>
            """);
        FoundryConnectConfigurationDocument configuration = new()
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings { IsEnabled = true, HasEnterpriseProfile = true, EnterpriseProfileTemplatePath = "Network\\corp.xml" }
        };

        ProvisioningOutcome outcome = await CreateStep(directory, configuration, ConnectRuntimeSecrets.None()).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProvisionedWifiStatus.Unavailable, outcome.Wifi.Status);
        Assert.Equal(ProvisionedWifiUnavailableReason.InvalidConfiguration, outcome.Wifi.Reason);
        Assert.Equal([NetworkFailureCode.ProvisionedProfileUnavailable], outcome.Failures);
        Assert.Null(outcome.WifiProfile);
    }

    [Fact]
    public async Task Wifi_NeverLogsSsidOrProfileName()
    {
        const string ssid = "Corp-Secret-Net";
        using var directory = new TemporaryDirectory();
        var logger = new RecordingLogger<ProvisioningStep>();
        var wlan = new FakeWlanClient
        {
            Snapshot = new(WifiAvailability.Available, [FakeWlanClient.Interface(WifiA)], []),
            SetProfileResult = new WlanOperationResult(WlanOperationStatus.ProfileRejected, 1206, 524289)
        };
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Available("correct horse".ToCharArray()), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

        await CreateStep(directory, PersonalWifiConfiguration(ssid), secrets, wlan: wlan, logger: logger).RunAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(ssid, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(entry.Properties.Values, value => value?.ToString()?.Contains(ssid, StringComparison.Ordinal) == true);
            Assert.DoesNotContain(ssid, entry.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
        });
    }

    private static FoundryConnectConfigurationDocument WiredConfiguration(TemporaryDirectory directory)
    {
        directory.CreateFile("Network\\wired.xml", "<LANProfile />");
        return new FoundryConnectConfigurationDocument
        {
            Dot1x = new ConnectDot1xSettings { IsEnabled = true, ProfileTemplatePath = "Network\\wired.xml" }
        };
    }

    private static FoundryConnectConfigurationDocument PersonalWifiConfiguration(string ssid = "Lab") => new()
    {
        Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
        Wifi = new ConnectWifiSettings { IsEnabled = true, Ssid = ssid, SecurityType = "WPA2-Personal" }
    };

    private static ProvisioningStep CreateStep(
        TemporaryDirectory directory,
        FoundryConnectConfigurationDocument configuration,
        ConnectRuntimeSecrets secrets,
        RecordingCertificateInstaller? certificates = null,
        RecordingWiredClient? wired = null,
        FakeWlanClient? wlan = null,
        IReadOnlyList<NetworkAdapterSnapshot>? adapters = null,
        RecordingLogger<ProvisioningStep>? logger = null)
    {
        return new ProvisioningStep(
            configuration,
            Path.Combine(directory.Path, "foundry.connect.config.json"),
            secrets,
            certificates ?? new RecordingCertificateInstaller(),
            wired ?? new RecordingWiredClient(),
            wlan ?? new FakeWlanClient(),
            new StaticAdapterProvider(adapters ?? []),
            logger ?? new RecordingLogger<ProvisioningStep>());
    }

    private static NetworkAdapterSnapshot Adapter(string name, NetworkInterfaceType type, OperationalStatus status) =>
        new(name, name, type, status, "00-11-22-33-44-55", [], [], [], false);

    private sealed class RecordingCertificateInstaller : ICertificateInstaller
    {
        public CertificateInstallResult Result { get; set; } = new(CertificateInstallStatus.Installed, "THUMBPRINT");

        public List<(string Path, string Password)> Calls { get; } = [];

        public CertificateInstallResult Install(string certificatePath, ReadOnlySpan<char> pfxPassword)
        {
            Calls.Add((certificatePath, new string(pfxPassword)));
            return Result;
        }
    }

    private sealed class RecordingWiredClient : IWiredDot1xClient
    {
        public WiredCommandResult AddResult { get; set; } = new(true, ConnectProcessStatus.Completed, 0);

        public Exception? AddException { get; set; }

        public WiredCommandResult ReconnectResult { get; set; } = new(true, ConnectProcessStatus.Completed, 0);

        public List<string> AddedProfiles { get; } = [];

        public List<string> Reconnected { get; } = [];

        public Task<WiredCommandResult> AddProfileAsync(string profilePath, CancellationToken cancellationToken)
        {
            AddedProfiles.Add(profilePath);
            return AddException is null ? Task.FromResult(AddResult) : Task.FromException<WiredCommandResult>(AddException);
        }

        public Task<WiredCommandResult> ReconnectAsync(string interfaceName, CancellationToken cancellationToken)
        {
            Reconnected.Add(interfaceName);
            return Task.FromResult(ReconnectResult);
        }

        public Task<IReadOnlyList<WiredInterfaceAuthentication>> ReadAuthenticationAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WiredInterfaceAuthentication>>([]);
    }

    private sealed class StaticAdapterProvider(IReadOnlyList<NetworkAdapterSnapshot> adapters) : INetworkAdapterSnapshotProvider
    {
        public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters() => adapters;
    }
}
