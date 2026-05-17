using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.Telemetry;
using Foundry.Core.Services.WinPe;
using Foundry.Telemetry;

namespace Foundry.Core.Tests;

public sealed class BootMediaTelemetryPropertyBuilderTests
{
    [Fact]
    public void Build_CreatesFinalBootMediaPropertiesWithoutSensitiveReadinessFlags()
    {
        var options = new MediaPreflightOptions
        {
            Architecture = WinPeArchitecture.Arm64,
            WinPeLanguage = "en-US",
            UsbPartitionStyle = UsbPartitionStyle.Gpt,
            UsbFormatMode = UsbFormatMode.Quick,
            BootImageSource = WinPeBootImageSource.WinReWifi,
            SignatureMode = WinPeSignatureMode.Pca2023,
            DriverVendors = [WinPeVendorSelection.Dell],
            CustomDriverDirectoryPath = @"C:\Drivers",
            IsAutopilotEnabled = true,
            AreRequiredSecretsReady = true
        };
        var document = new FoundryExpertConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = true,
                    AutoGenerateName = true,
                    AllowManualSuffixEdit = true,
                    Prefix = "LAB"
                },
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    SkipLicenseTerms = true,
                    DiagnosticDataLevel = OobeDiagnosticDataLevel.Off,
                    HidePrivacySetup = true,
                    AllowTailoredExperiences = false,
                    AllowAdvertisingId = false,
                    AllowOnlineSpeechRecognition = false,
                    AllowInkingAndTypingDiagnostics = false,
                    LocationAccess = OobeLocationAccessMode.ForceOff
                },
                AppxRemoval = new AppxRemovalSettings
                {
                    IsEnabled = true,
                    PackageNames = AppxRemovalCatalog.Entries
                        .Where(entry => entry.Category == "Gaming / Xbox")
                        .Select(entry => entry.PackageName)
                        .ToArray()
                }
            },
            Localization = new LocalizationSettings
            {
                VisibleLanguageCodes = ["en-US", "fr-FR"],
                DefaultLanguageCodeOverride = "en-US",
                DefaultTimeZoneId = "Romance Standard Time"
            },
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    ProfileTemplatePath = @"C:\Network\wired.xml",
                    RequiresCertificate = true,
                    CertificatePath = @"C:\Network\wired.cer"
                },
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWifi",
                    SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal
                }
            }
        };

        IReadOnlyDictionary<string, object?> result = BootMediaTelemetryPropertyBuilder.Build(
            TelemetryBootMediaTargets.Usb,
            options,
            document,
            success: false,
            failedStepName: "Customize boot image",
            duration: TimeSpan.FromSeconds(42.25),
            connectRuntimePayloadSource: TelemetryRuntimePayloadSources.Release,
            deployRuntimePayloadSource: TelemetryRuntimePayloadSources.Debug);

        Assert.Equal(TelemetryBootMediaTargets.Usb, result["boot_media_target"]);
        Assert.False((bool)result["boot_media_creation_success"]!);
        Assert.Equal(42.25, result["boot_media_creation_duration_seconds"]);
        Assert.Equal("Customize boot image", result["boot_media_creation_failed_step_name"]);
        Assert.Equal("arm64", result["boot_media_architecture"]);
        Assert.Equal("en-us", result["boot_media_winpe_language"]);
        Assert.Equal("winrewifi", result["boot_media_boot_image_source"]);
        Assert.Equal("pca2023", result["boot_media_signature_mode"]);
        Assert.Equal("gpt", result["boot_media_usb_partition_style"]);
        Assert.Equal("quick", result["boot_media_usb_format_mode"]);
        Assert.True((bool)result["boot_media_drivers_dell_enabled"]!);
        Assert.False((bool)result["boot_media_drivers_hp_enabled"]!);
        Assert.True((bool)result["boot_media_drivers_custom_enabled"]!);
        Assert.Equal(TelemetryRuntimePayloadSources.Release, result["boot_media_connect_runtime_payload_source"]);
        Assert.Equal(TelemetryRuntimePayloadSources.Debug, result["boot_media_deploy_runtime_payload_source"]);
        Assert.True((bool)result["autopilot_enabled"]!);
        Assert.True((bool)result["customization_any_enabled"]!);
        Assert.Equal("auto_generated_editable", result["customization_machine_naming_mode"]);
        Assert.True((bool)result["customization_appx_removal_enabled"]!);
        Assert.Equal(8, result["customization_appx_removal_package_count"]);
        Assert.Equal("gaming_xbox", result["customization_appx_removal_profile"]);
        Assert.True((bool)result["localization_any_enabled"]!);
        Assert.Equal(2, result["localization_visible_languages_count"]);
        Assert.True((bool)result["localization_default_language_configured"]!);
        Assert.True((bool)result["localization_time_zone_configured"]!);
        Assert.True((bool)result["network_any_enabled"]!);
        Assert.True((bool)result["network_wired_dot1x_enabled"]!);
        Assert.True((bool)result["network_wired_dot1x_profile_configured"]!);
        Assert.True((bool)result["network_wired_dot1x_certificate_required"]!);
        Assert.True((bool)result["network_wired_dot1x_certificate_configured"]!);
        Assert.True((bool)result["network_wifi_provisioning_enabled"]!);
        Assert.True((bool)result["network_wifi_profile_configured"]!);
        Assert.Equal("personal", result["network_wifi_security_type"]);
        Assert.True((bool)result["network_wifi_ssid_configured"]!);
        Assert.True((bool)result["network_wifi_passphrase_configured"]!);
        Assert.False((bool)result["network_wifi_enterprise_profile_configured"]!);
        Assert.False((bool)result["network_wifi_enterprise_certificate_required"]!);
        Assert.False((bool)result["network_wifi_enterprise_certificate_configured"]!);
        Assert.False(result.ContainsKey("network_configured"));
        Assert.False(result.ContainsKey("connect_configured"));
        Assert.False(result.ContainsKey("deploy_configured"));
        Assert.DoesNotContain(result.Values.OfType<string>(), value => value.Contains("CorpWifi", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Values.OfType<string>(), value => value.Contains("Romance Standard Time", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Values.OfType<string>(), value => value.Contains("Microsoft.Xbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WhenAppxSelectionDoesNotMatchSingleCategory_ReportsCustomProfile()
    {
        var options = new MediaPreflightOptions();
        var document = new FoundryExpertConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                AppxRemoval = new AppxRemovalSettings
                {
                    IsEnabled = true,
                    PackageNames =
                    [
                        "Microsoft.Copilot",
                        "Microsoft.GamingApp"
                    ]
                }
            }
        };

        IReadOnlyDictionary<string, object?> result = BootMediaTelemetryPropertyBuilder.Build(
            TelemetryBootMediaTargets.Iso,
            options,
            document,
            success: true,
            failedStepName: null,
            duration: TimeSpan.Zero,
            connectRuntimePayloadSource: TelemetryRuntimePayloadSources.None,
            deployRuntimePayloadSource: TelemetryRuntimePayloadSources.None);

        Assert.True((bool)result["customization_appx_removal_enabled"]!);
        Assert.Equal(2, result["customization_appx_removal_package_count"]);
        Assert.Equal("custom", result["customization_appx_removal_profile"]);
    }

    [Fact]
    public void Build_WhenAppxSelectionMatchesMultipleCategories_ReportsMultipleProfile()
    {
        var options = new MediaPreflightOptions();
        var document = new FoundryExpertConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                AppxRemoval = new AppxRemovalSettings
                {
                    IsEnabled = true,
                    PackageNames = AppxRemovalCatalog.Entries
                        .Where(entry =>
                            entry.Category == "Gaming / Xbox" ||
                            entry.Category == "Phone / cross-device")
                        .Select(entry => entry.PackageName)
                        .ToArray()
                }
            }
        };

        IReadOnlyDictionary<string, object?> result = BootMediaTelemetryPropertyBuilder.Build(
            TelemetryBootMediaTargets.Iso,
            options,
            document,
            success: true,
            failedStepName: null,
            duration: TimeSpan.Zero,
            connectRuntimePayloadSource: TelemetryRuntimePayloadSources.None,
            deployRuntimePayloadSource: TelemetryRuntimePayloadSources.None);

        Assert.True((bool)result["customization_appx_removal_enabled"]!);
        Assert.Equal(10, result["customization_appx_removal_package_count"]);
        Assert.Equal("multiple", result["customization_appx_removal_profile"]);
    }

    [Fact]
    public void Build_WhenPersonalWifiIsNotProvisioned_DoesNotReportPassphraseConfigured()
    {
        var options = new MediaPreflightOptions
        {
            AreRequiredSecretsReady = true
        };
        var document = new FoundryExpertConfigurationDocument
        {
            Network = new NetworkSettings
            {
                WifiProvisioned = false,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWifi",
                    SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal
                }
            }
        };

        IReadOnlyDictionary<string, object?> result = BootMediaTelemetryPropertyBuilder.Build(
            TelemetryBootMediaTargets.Iso,
            options,
            document,
            success: true,
            failedStepName: null,
            duration: TimeSpan.Zero,
            connectRuntimePayloadSource: TelemetryRuntimePayloadSources.None,
            deployRuntimePayloadSource: TelemetryRuntimePayloadSources.None);

        Assert.False((bool)result["network_wifi_passphrase_configured"]!);
    }

    [Fact]
    public void Build_WhenOptionalCertificatesArePresent_ReportsCertificatesConfigured()
    {
        var options = new MediaPreflightOptions();
        var document = new FoundryExpertConfigurationDocument
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    RequiresCertificate = false,
                    CertificatePath = @"C:\Network\wired.cer"
                },
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    HasEnterpriseProfile = true,
                    RequiresCertificate = false,
                    CertificatePath = @"C:\Network\wifi.cer"
                }
            }
        };

        IReadOnlyDictionary<string, object?> result = BootMediaTelemetryPropertyBuilder.Build(
            TelemetryBootMediaTargets.Iso,
            options,
            document,
            success: true,
            failedStepName: null,
            duration: TimeSpan.Zero,
            connectRuntimePayloadSource: TelemetryRuntimePayloadSources.None,
            deployRuntimePayloadSource: TelemetryRuntimePayloadSources.None);

        Assert.True((bool)result["network_wired_dot1x_certificate_configured"]!);
        Assert.True((bool)result["network_wifi_enterprise_certificate_configured"]!);
    }
}
