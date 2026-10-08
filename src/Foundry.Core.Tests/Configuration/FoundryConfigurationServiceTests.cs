// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Configuration;

public sealed class FoundryConfigurationServiceTests
{
    [Fact]
    public void DomainJoinRoundTripsDomainsWithTheirOusAndLeavesOtherSectionsAlone()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument loaded = service.DeserializeLocalAuthoringDraft(service.Serialize(TwoDomainDraft()));

        Assert.Equal(["corp.test", "emea.test"], loaded.DomainJoin.Domains.Select(domain => domain.DomainName));
        Assert.Equal("emea", loaded.DomainJoin.DefaultDomainId);
        Assert.Equal("CORP\\join", loaded.DomainJoin.SharedAccountName);
        Assert.Equal("EMEA\\join", loaded.DomainJoin.Domains[1].AccountName);

        Assert.Equal(["devices", "servers"], loaded.DomainJoin.Domains[0].OrganizationalUnits.Select(unit => unit.Id));
        Assert.Equal("servers", loaded.DomainJoin.Domains[0].DefaultOuId);
        Assert.Empty(loaded.DomainJoin.Domains[1].OrganizationalUnits);
        Assert.Equal(@"E:\Media\retained.iso", loaded.General.IsoOutputPath);
        Assert.Equal("retained-wifi", loaded.Network.Wifi.Ssid);
        Assert.Equal("retained-machine", loaded.Customization.MachineNaming.ManualInitialValue);
    }

    [Fact]
    public void EarlierUnreleasedDomainJoinShapeLoadsAsAnEmptyDomainList()
    {
        const string json = "{\"schemaVersion\":18,\"domainJoin\":{\"isEnabled\":false,\"mode\":1,\"domainName\":\"corp.test\"," +
            "\"accountName\":\"CORP\\\\join\",\"ouCatalogDomain\":\"corp.test\",\"organizationalUnits\":[{\"id\":\"a\",\"displayName\":\"A\"," +
            "\"distinguishedName\":\"OU=A,DC=corp,DC=test\"}],\"defaultOuId\":\"a\",\"allowOuSelectionDuringDeployment\":true}}";

        FoundryConfigurationDocument document = new FoundryConfigurationService().Deserialize(json);

        Assert.Empty(document.DomainJoin.Domains);
        Assert.Null(document.DomainJoin.DefaultDomainId);
        Assert.Null(document.DomainJoin.SharedAccountName);
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(document.DomainJoin).IsValid);
    }

    [Theory]
    [InlineData("malformed-dn")]
    [InlineData("foreign-dn")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-dn")]
    [InlineData("missing-default-ou")]
    [InlineData("missing-default-domain")]
    [InlineData("duplicate-domain")]
    [InlineData("oversized-label")]
    [InlineData("overflow")]
    [InlineData("provisioning-conflict")]
    public void Deserialize_RejectsMalformedDomainListAndProvisioningConflict(string corruption)
    {
        FoundryConfigurationDocument draft = TwoDomainDraft();
        DomainJoinSettings settings = draft.DomainJoin;
        DomainJoinDomainSettings corp = settings.Domains[0];
        DomainJoinOrganizationalUnitSettings first = corp.OrganizationalUnits[0];
        DomainJoinOrganizationalUnitSettings second = corp.OrganizationalUnits[1];
        DomainJoinSettings WithCorp(DomainJoinDomainSettings changed) => settings with { Domains = [changed, settings.Domains[1]] };
        settings = corruption switch
        {
            "malformed-dn" => WithCorp(corp with { OrganizationalUnits = [first with { DistinguishedName = "invalid" }, second] }),
            "foreign-dn" => WithCorp(corp with { OrganizationalUnits = [first with { DistinguishedName = "OU=Devices,DC=emea,DC=test" }, second] }),
            "duplicate-id" => WithCorp(corp with { OrganizationalUnits = [first, second with { Id = "devices" }] }),
            "duplicate-dn" => WithCorp(corp with { OrganizationalUnits = [first, second with { DistinguishedName = "OU=Devices,DC=corp,DC=test" }] }),
            "missing-default-ou" => WithCorp(corp with { DefaultOuId = "missing" }),
            "missing-default-domain" => settings with { DefaultDomainId = "missing" },
            "duplicate-domain" => settings with { Domains = [corp, settings.Domains[1] with { DomainName = "CORP.test" }] },
            "oversized-label" => WithCorp(corp with { OrganizationalUnits = [first with { DisplayName = new string('x', 121) }, second] }),
            "overflow" => WithCorp(corp with
            {
                OrganizationalUnits = Enumerable.Range(0, 1025).Select(index => first with
                { Id = index.ToString(), DistinguishedName = $"OU=Devices{index},DC=corp,DC=test" }).ToArray()
            }),
            _ => settings
        };
        draft = draft with { DomainJoin = settings, Autopilot = draft.Autopilot with { IsEnabled = corruption == "provisioning-conflict" } };
        var service = new FoundryConfigurationService();
        string json = service.Serialize(draft);
        Assert.Throws<InvalidOperationException>(() => service.DeserializeLocalAuthoringDraft(json));
        Assert.Throws<InvalidOperationException>(() => service.Deserialize(json));
    }

    [Fact]
    public void DisabledDomainJoin_RemainsSavableAndPortable()
    {
        var service = new FoundryConfigurationService();
        FoundryConfigurationDocument draft = TwoDomainDraft();
        draft = draft with { DomainJoin = draft.DomainJoin with { IsEnabled = false } };

        FoundryConfigurationDocument loaded = service.Deserialize(service.Serialize(draft));
        FoundryConfigurationDocument portable = Foundry.Core.Services.Profiles.DeploymentProfileProjection.CreatePortable(loaded);

        Assert.False(portable.DomainJoin.IsEnabled);
        Assert.Equal(["devices", "servers"], portable.DomainJoin.Domains[0].OrganizationalUnits.Select(unit => unit.Id));
    }

    private static FoundryConfigurationDocument TwoDomainDraft() => new()
    {
        General = new() { IsoOutputPath = @"E:\Media\retained.iso", WinPeLanguage = "fr-FR", IncludeDellDrivers = true },
        Network = new() { Wifi = new() { Ssid = "retained-wifi" } },
        Customization = new() { MachineNaming = new() { IsEnabled = true, Mode = MachineNamingMode.Manual, ManualInitialValue = "retained-machine" } },
        DomainJoin = new()
        {
            IsEnabled = true,
            Mode = DomainJoinMode.Automatic,
            SharedAccountName = "CORP\\join",
            DefaultDomainId = "emea",
            Domains =
            [
                new()
                {
                    Id = "corp",
                    DomainName = "corp.test",
                    DefaultOuId = "servers",
                    OrganizationalUnits =
                    [
                        new() { Id = "devices", DisplayName = "Devices", DistinguishedName = "OU=Devices,DC=corp,DC=test" },
                        new() { Id = "servers", DisplayName = "Servers", DistinguishedName = "OU=Servers,DC=corp,DC=test" }
                    ]
                },
                new() { Id = "emea", DomainName = "emea.test", AccountName = "EMEA\\join" }
            ]
        }
    };

    [Fact]
    public void OldConfigurationDisablesDomainJoin()
    {
        FoundryConfigurationDocument document = new FoundryConfigurationService().Deserialize("{\"schemaVersion\":17}");
        Assert.Equal(18, document.SchemaVersion);
        Assert.False(document.DomainJoin.IsEnabled);
    }

    [Fact]
    public void Deserialize_RejectsContradictoryProvisioningModesWithoutChoosingWinner()
    {
        Assert.Throws<InvalidOperationException>(() => new FoundryConfigurationService().Deserialize(
            "{\"schemaVersion\":18,\"autopilot\":{\"isEnabled\":true},\"domainJoin\":{\"isEnabled\":true}}"));
    }

    [Fact]
    public void Deserialize_AutomaticSecretFreeDraftIsImportable()
    {
        FoundryConfigurationDocument document = new FoundryConfigurationService().Deserialize(
            "{\"schemaVersion\":18,\"domainJoin\":{\"isEnabled\":true,\"mode\":1}}");
        Assert.True(document.DomainJoin.IsEnabled);
        Assert.True(DomainJoinConfigurationValidator.ValidateMetadata(document.DomainJoin).IsValid);
        Assert.False(DomainJoinConfigurationValidator.EvaluateReadiness(document.DomainJoin, _ => false, false).IsValid);
    }

    [Theory]
    [InlineData("[\"99H1\"]", null)]
    [InlineData("[]", null)]
    [InlineData("[\"99H1\",\"24H2\"]", "24H2")]
    [InlineData("[\"26H2\"]", "26H2")]
    public void Deserialize_PublishedConfiguration_MigratesUnavailableReleasePolicy(string allowedReleases, string? expectedDefault)
    {
        var service = new FoundryConfigurationService();
        string json = $$$"""
            {
              "schemaVersion":16,
              "operatingSystemSelection":{
                "isEnabled":true,
                "allowedReleaseIds":{{{allowedReleases}}},
                "defaultReleaseId":"99H1",
                "allowedLanguageCodes":["fr-FR"],
                "defaultLanguageCode":"fr-FR",
                "defaultMediaOffset":2,
                "allowedLicenseChannels":["VOL"],
                "defaultLicenseChannel":"VOL",
                "allowedEditions":["Enterprise"],
                "defaultEdition":"Enterprise"
              }
            }
            """;

        FoundryConfigurationDocument document = service.Deserialize(json);
        var generated = new DeployConfigurationGenerator().Generate(document);

        Assert.Equal(18, document.SchemaVersion);
        Assert.Equal(ConfigurationSchemaVersions.DeployCurrent, generated.SchemaVersion);
        Assert.Equal(expectedDefault is null ? [] : new[] { expectedDefault }, document.OperatingSystemSelection.AllowedReleaseIds);
        Assert.Equal(expectedDefault, document.OperatingSystemSelection.DefaultReleaseId);
        Assert.Equal(["fr-FR"], document.OperatingSystemSelection.AllowedLanguageCodes);
        Assert.Equal("fr-FR", document.OperatingSystemSelection.DefaultLanguageCode);
        Assert.Equal(2, document.OperatingSystemSelection.DefaultMediaOffset);
        Assert.Equal(["VOL"], document.OperatingSystemSelection.AllowedLicenseChannels);
        Assert.Equal("VOL", document.OperatingSystemSelection.DefaultLicenseChannel);
        Assert.Equal(["Enterprise"], document.OperatingSystemSelection.AllowedEditions);
        Assert.Equal("Enterprise", document.OperatingSystemSelection.DefaultEdition);
        Assert.Equal(document.OperatingSystemSelection.AllowedReleaseIds, generated.OperatingSystemSelection.AllowedReleaseIds);
        Assert.Equal(expectedDefault, generated.OperatingSystemSelection.DefaultReleaseId);
        Assert.True(generated.OperatingSystemSelection.IsEnabled);
    }

    [Fact]
    public void DefaultConfiguration_DisablesNetworkProfileRoaming()
    {
        var document = new FoundryConfigurationDocument();

        Assert.False(document.Network.RoamWiredDot1xProfileToWindows);
        Assert.False(document.Network.RoamWiredDot1xPrivateKeyMaterialToWindows);
        Assert.False(document.Network.RoamWifiProfileToWindows);
        Assert.False(document.Network.RoamWifiPrivateKeyMaterialToWindows);
    }

    [Fact]
    public void DefaultConfiguration_EnablesPca2023Signature()
    {
        var document = new FoundryConfigurationDocument();

        Assert.True(document.General.UseCa2023);
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsBusinessSettings()
    {
        var service = new FoundryConfigurationService();

        var document = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWiFi",
                    SecurityType = "WPA2/WPA3-Personal",
                    Passphrase = "supersecret"
                }
            },
            OperatingSystemSelection = new OperatingSystemSelectionSettings
            {
                IsEnabled = true,
                AllowedLanguageCodes = ["en-US", "fr-FR"],
                DefaultLanguageCode = "fr-FR",
                AllowedReleaseIds = ["25H2"],
                DefaultReleaseId = "25H2",
                DefaultMediaOffset = 2,
                AllowedLicenseChannels = ["RET"],
                DefaultLicenseChannel = "RET",
                AllowedEditions = ["Pro"],
                DefaultEdition = "Pro"
            },
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = true,
                    Mode = MachineNamingMode.Composed,
                    UploadComputerNameToAutopilot = true,
                    Components =
                    [
                        new MachineNameComponentSettings
                        {
                            Type = MachineNameComponentType.StaticText,
                            StaticText = "FD"
                        },
                        new MachineNameComponentSettings
                        {
                            Type = MachineNameComponentType.SerialNumber,
                            MaximumLength = 12,
                            Truncation = MachineNameTruncation.KeepRight
                        }
                    ],
                    Separator = MachineNameSeparator.Hyphen,
                    Casing = MachineNameCasing.Uppercase,
                    AllowEditingDuringDeployment = false
                },
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    EnableAdministratorAccount = true,
                    UseAdministratorPassword = true,
                    SkipLicenseTerms = true,
                    DiagnosticDataLevel = OobeDiagnosticDataLevel.Off,
                    HidePrivacySetup = true,
                    AllowTailoredExperiences = false,
                    AllowAdvertisingId = false,
                    AllowOnlineSpeechRecognition = false,
                    AllowInkingAndTypingDiagnostics = false,
                    LocationAccess = OobeLocationAccessMode.ForceOff,
                    AdditionalAccounts =
                    [
                        new OobeAdditionalAccountSettings
                        {
                            Id = "account-1",
                            UserName = "Technician",
                            Type = OobeAccountType.Administrator,
                            UsePassword = true
                        }
                    ]
                },
                AppxRemoval = new AppxRemovalSettings
                {
                    IsEnabled = true,
                    PackageNames = ["Microsoft.BingWeather", "Microsoft.GamingApp"]
                },
                WindowsOptionalFeatures = new WindowsOptionalFeatureSettings
                {
                    IsEnabled = true,
                    EnabledFeatureIds = ["wf:netfx3", "wf:microsoft-windows-subsystem-linux"],
                    DisabledFeatureIds = ["wf:smb1protocol-server"]
                },
                AiComponentRemoval = new AiComponentRemovalSettings
                {
                    IsEnabled = true,
                    RemoveCopilot = true,
                    RemoveAiHub = true,
                    DisableRecall = true,
                    DisableClickToDo = true,
                    DisableAiServiceAutoStart = true,
                    DisableEdgeAi = true,
                    DisablePaintAi = true,
                    DisableNotepadAi = true
                }
            },
            Telemetry = new TelemetrySettings
            {
                IsEnabled = false,
                IsRemoteDiagnosticsEnabled = true,
                InstallId = "install-id",
                HostUrl = TelemetryDefaults.PostHogEuHost,
                ProjectToken = "project-token",
                RuntimePayloadSource = TelemetryRuntimePayloadSources.None
            }
        };

        string json = service.Serialize(document);
        FoundryConfigurationDocument loaded = service.Deserialize(json);

        Assert.True(loaded.Network.WifiProvisioned);
        Assert.Equal("CorpWiFi", loaded.Network.Wifi.Ssid);
        Assert.True(loaded.OperatingSystemSelection.IsEnabled);
        Assert.Equal(["en-US", "fr-FR"], loaded.OperatingSystemSelection.AllowedLanguageCodes);
        Assert.Equal("fr-FR", loaded.OperatingSystemSelection.DefaultLanguageCode);
        Assert.Equal(["25H2"], loaded.OperatingSystemSelection.AllowedReleaseIds);
        Assert.Equal("25H2", loaded.OperatingSystemSelection.DefaultReleaseId);
        Assert.Equal(2, loaded.OperatingSystemSelection.DefaultMediaOffset);
        Assert.Equal(["RET"], loaded.OperatingSystemSelection.AllowedLicenseChannels);
        Assert.Equal("RET", loaded.OperatingSystemSelection.DefaultLicenseChannel);
        Assert.Equal(["Pro"], loaded.OperatingSystemSelection.AllowedEditions);
        Assert.Equal("Pro", loaded.OperatingSystemSelection.DefaultEdition);
        Assert.Equal(MachineNamingMode.Composed, loaded.Customization.MachineNaming.Mode);
        Assert.True(loaded.Customization.MachineNaming.UploadComputerNameToAutopilot);
        Assert.Collection(
            loaded.Customization.MachineNaming.Components,
            component =>
            {
                Assert.Equal(MachineNameComponentType.StaticText, component.Type);
                Assert.Equal("FD", component.StaticText);
            },
            component =>
            {
                Assert.Equal(MachineNameComponentType.SerialNumber, component.Type);
                Assert.Equal(12, component.MaximumLength);
                Assert.Equal(MachineNameTruncation.KeepRight, component.Truncation);
            });
        Assert.Equal(MachineNameSeparator.Hyphen, loaded.Customization.MachineNaming.Separator);
        Assert.Equal(MachineNameCasing.Uppercase, loaded.Customization.MachineNaming.Casing);
        Assert.False(loaded.Customization.MachineNaming.AllowEditingDuringDeployment);
        Assert.True(loaded.Customization.Oobe.IsEnabled);
        Assert.True(loaded.Customization.Oobe.EnableAdministratorAccount);
        Assert.True(loaded.Customization.Oobe.UseAdministratorPassword);
        Assert.True(loaded.Customization.Oobe.SkipLicenseTerms);
        Assert.Equal(OobeDiagnosticDataLevel.Off, loaded.Customization.Oobe.DiagnosticDataLevel);
        Assert.True(loaded.Customization.Oobe.HidePrivacySetup);
        Assert.False(loaded.Customization.Oobe.AllowTailoredExperiences);
        Assert.False(loaded.Customization.Oobe.AllowAdvertisingId);
        Assert.False(loaded.Customization.Oobe.AllowOnlineSpeechRecognition);
        Assert.False(loaded.Customization.Oobe.AllowInkingAndTypingDiagnostics);
        Assert.Equal(OobeLocationAccessMode.ForceOff, loaded.Customization.Oobe.LocationAccess);
        Assert.Collection(
            loaded.Customization.Oobe.AdditionalAccounts,
            account =>
            {
                Assert.Equal("account-1", account.Id);
                Assert.Equal("Technician", account.UserName);
                Assert.Equal(OobeAccountType.Administrator, account.Type);
                Assert.True(account.UsePassword);
            });
        Assert.DoesNotContain("\"password\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmation", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(loaded.Customization.AppxRemoval.IsEnabled);
        Assert.Equal(["Microsoft.BingWeather", "Microsoft.GamingApp"], loaded.Customization.AppxRemoval.PackageNames);
        Assert.True(loaded.Customization.WindowsOptionalFeatures.IsEnabled);
        Assert.Equal(["wf:netfx3", "wf:microsoft-windows-subsystem-linux"], loaded.Customization.WindowsOptionalFeatures.EnabledFeatureIds);
        Assert.Equal(["wf:smb1protocol-server"], loaded.Customization.WindowsOptionalFeatures.DisabledFeatureIds);
        Assert.True(loaded.Customization.AiComponentRemoval.IsEnabled);
        Assert.True(loaded.Customization.AiComponentRemoval.RemoveCopilot);
        Assert.True(loaded.Customization.AiComponentRemoval.RemoveAiHub);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableRecall);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableClickToDo);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableAiServiceAutoStart);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableEdgeAi);
        Assert.True(loaded.Customization.AiComponentRemoval.DisablePaintAi);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableNotepadAi);
        Assert.False(loaded.Telemetry.IsEnabled);
        Assert.True(loaded.Telemetry.IsRemoteDiagnosticsEnabled);
        Assert.Equal("install-id", loaded.Telemetry.InstallId);
        Assert.Equal("project-token", loaded.Telemetry.ProjectToken);
    }

    [Fact]
    public void Deserialize_WhenRemoteDiagnosticsConsentIsMissing_DefaultsToEnabled()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument loaded = service.Deserialize("""
            {
              "schemaVersion": 13,
              "telemetry": {
                "isEnabled": true
              }
            }
            """);

        Assert.True(loaded.Telemetry.IsRemoteDiagnosticsEnabled);
    }

    [Fact]
    public void Deserialize_WhenLegacyGeneratedMachineNameIsConfigured_MigratesToComposition()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument loaded = service.Deserialize("""
            {
              "schemaVersion": 13,
              "customization": {
                "machineNaming": {
                  "isEnabled": true,
                  "prefix": "FD-",
                  "autoGenerateName": true,
                  "allowManualSuffixEdit": false
                }
              }
            }
            """);

        MachineNamingSettings naming = loaded.Customization.MachineNaming;
        Assert.Equal(MachineNamingMode.Composed, naming.Mode);
        Assert.False(naming.UploadComputerNameToAutopilot);
        Assert.Collection(
            naming.Components,
            component =>
            {
                Assert.Equal(MachineNameComponentType.StaticText, component.Type);
                Assert.Equal("FD-", component.StaticText);
            },
            component =>
            {
                Assert.Equal(MachineNameComponentType.Random, component.Type);
                Assert.Equal(6, component.MaximumLength);
                Assert.Null(component.Truncation);
            });
        Assert.Equal(MachineNameSeparator.None, naming.Separator);
        Assert.Equal(MachineNameCasing.Preserve, naming.Casing);
        Assert.False(naming.AllowEditingDuringDeployment);
        Assert.Equal(FoundryConfigurationDocument.CurrentSchemaVersion, loaded.SchemaVersion);

        string serialized = service.Serialize(loaded);
        Assert.DoesNotContain("prefix", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("autoGenerateName", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allowManualSuffixEdit", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_WhenLegacyManualMachineNameIsConfigured_MigratesInitialValue()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument loaded = service.Deserialize("""
            {
              "schemaVersion": 13,
              "customization": {
                "machineNaming": {
                  "isEnabled": true,
                  "prefix": "LAB-",
                  "autoGenerateName": false,
                  "allowManualSuffixEdit": false
                }
              }
            }
            """);

        MachineNamingSettings naming = loaded.Customization.MachineNaming;
        Assert.Equal(MachineNamingMode.Manual, naming.Mode);
        Assert.Equal("LAB-", naming.ManualInitialValue);
        Assert.Empty(naming.Components);
        Assert.True(naming.AllowEditingDuringDeployment);
    }

    [Fact]
    public void Deserialize_WhenLegacyMachineNamingIsDisabled_PreservesDisabledState()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument loaded = service.Deserialize("""
            {
              "schemaVersion": 13,
              "customization": {
                "machineNaming": {
                  "isEnabled": false,
                  "prefix": "IGNORED",
                  "autoGenerateName": true
                }
              }
            }
            """);

        Assert.False(loaded.Customization.MachineNaming.IsEnabled);
        Assert.Empty(loaded.Customization.MachineNaming.Components);
        Assert.Null(loaded.Customization.MachineNaming.ManualInitialValue);
    }

    [Fact]
    public void Serialize_ThenDeserialize_WhenNetworkProfileRoamingIsSplit_PreservesIndependentSettings()
    {
        var service = new FoundryConfigurationService();
        var document = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                RoamWiredDot1xProfileToWindows = true,
                RoamWiredDot1xPrivateKeyMaterialToWindows = true,
                RoamWifiProfileToWindows = false,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "Foundry WiFi",
                    SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal,
                    Passphrase = "ValidPassphrase123"
                }
            }
        };

        string json = service.Serialize(document);
        FoundryConfigurationDocument loaded = service.Deserialize(json);

        Assert.True(loaded.Network.RoamWiredDot1xProfileToWindows);
        Assert.True(loaded.Network.RoamWiredDot1xPrivateKeyMaterialToWindows);
        Assert.False(loaded.Network.RoamWifiProfileToWindows);
        Assert.False(loaded.Network.RoamWifiPrivateKeyMaterialToWindows);
    }

    [Fact]
    public void Deserialize_WhenLegacySharedRoamingIsEnabled_MigratesBothTransports()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument loaded = service.Deserialize("""
            {
              "schemaVersion": 13,
              "network": {
                "roamWifiProfilesToWindows": true,
                "roamPrivateKeyMaterialToWindows": true
              }
            }
            """);

        Assert.True(loaded.Network.RoamWiredDot1xProfileToWindows);
        Assert.True(loaded.Network.RoamWiredDot1xPrivateKeyMaterialToWindows);
        Assert.True(loaded.Network.RoamWifiProfileToWindows);
        Assert.True(loaded.Network.RoamWifiPrivateKeyMaterialToWindows);
        Assert.Equal(FoundryConfigurationDocument.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void Deserialize_WhenSplitRoamingFieldsAreExplicit_DoesNotLetLegacyFallbackOverrideThem()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument loaded = service.Deserialize("""
            {
              "schemaVersion": 14,
              "network": {
                "roamWiredDot1xProfileToWindows": false,
                "roamWiredDot1xPrivateKeyMaterialToWindows": false,
                "roamWifiProfileToWindows": false,
                "roamWifiPrivateKeyMaterialToWindows": false,
                "roamWifiProfilesToWindows": true,
                "roamPrivateKeyMaterialToWindows": true
              }
            }
            """);

        Assert.False(loaded.Network.RoamWiredDot1xProfileToWindows);
        Assert.False(loaded.Network.RoamWiredDot1xPrivateKeyMaterialToWindows);
        Assert.False(loaded.Network.RoamWifiProfileToWindows);
        Assert.False(loaded.Network.RoamWifiPrivateKeyMaterialToWindows);
    }

    [Fact]
    public void Deserialize_WhenJsonIsNullLiteral_ReturnsDefaultDocument()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument document = service.Deserialize("null");

        Assert.Equal(FoundryConfigurationDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.False(document.Network.WifiProvisioned);
        Assert.False(document.Autopilot.IsEnabled);
    }

    [Fact]
    public void Deserialize_WhenAutopilotProvisioningModeIsMissing_DefaultsToJsonProfile()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument document = service.Deserialize("""
            {
              "schemaVersion": 7,
              "autopilot": {
                "isEnabled": true
              }
            }
            """);

        Assert.Equal(AutopilotProvisioningMode.JsonProfile, document.Autopilot.ProvisioningMode);
    }

    [Fact]
    public void Serialize_ThenDeserialize_WhenInteractiveHardwareHashModeIsSelected_PreservesReadableMode()
    {
        var service = new FoundryConfigurationService();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.InteractiveHardwareHashUpload
            }
        };

        string json = service.Serialize(document);
        FoundryConfigurationDocument loaded = service.Deserialize(json);

        Assert.Contains("\"provisioningMode\": \"interactiveHardwareHashUpload\"", json, StringComparison.Ordinal);
        Assert.Equal(AutopilotProvisioningMode.InteractiveHardwareHashUpload, loaded.Autopilot.ProvisioningMode);
    }

    [Fact]
    public void Serialize_WhenHardwareHashSettingsArePersisted_DoesNotWritePrivateMaterial()
    {
        var service = new FoundryConfigurationService();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = new AutopilotHardwareHashUploadSettings
                {
                    Tenant = new AutopilotTenantRegistrationSettings
                    {
                        TenantId = "tenant-id",
                        ApplicationObjectId = "application-object-id",
                        ClientId = "client-id",
                        ServicePrincipalObjectId = "service-principal-object-id"
                    },
                    ActiveCertificate = new AutopilotCertificateMetadata
                    {
                        KeyId = "certificate-key-id",
                        Thumbprint = "ABCDEF123456",
                        DisplayName = "Foundry OSD Autopilot Registration",
                        ExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(6)
                    },
                    BootMediaCertificate = new AutopilotBootMediaCertificateSettings
                    {
                        PfxPath = @"E:\Secrets\foundry-osd-autopilot-registration.pfx",
                        PfxPassword = "PfxPassword-DoNotLeak",
                        ValidatedThumbprint = "ABCDEF123456",
                        ValidatedExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(6)
                    }
                }
            }
        };

        string json = service.Serialize(document);

        Assert.Contains("\"provisioningMode\": \"hardwareHashUpload\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("pfx", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"pfxPassword\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PfxPassword-DoNotLeak", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"E:\Secrets", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyLegacyGeneralSettings_WhenAuthoringConfigHasNoGeneralSection_CopiesMediaDefaults()
    {
        var document = new FoundryConfigurationDocument();
        var legacyGeneral = new GeneralSettings
        {
            IsoOutputPath = @"E:\Foundry.iso",
            Architecture = Core.Services.WinPe.WinPeArchitecture.Arm64,
            WinPeLanguage = "fr-FR",
            UseCa2023 = true,
            UsbPartitionStyle = Core.Services.WinPe.UsbPartitionStyle.Mbr,
            UsbFormatMode = Core.Services.WinPe.UsbFormatMode.Complete,
            IncludeDellDrivers = true,
            IncludeHpDrivers = true,
            CustomDriverDirectoryPath = @"D:\Drivers"
        };

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplyLegacyGeneralSettings(
            document,
            legacyGeneral);

        Assert.Equal(legacyGeneral, migrated.General);
    }

    [Fact]
    public void ApplyLegacyGeneralSettings_WhenLegacyGeneralSettingsAreMissing_PreservesDocument()
    {
        var existingGeneral = new GeneralSettings
        {
            IsoOutputPath = @"C:\Existing.iso",
            Architecture = Core.Services.WinPe.WinPeArchitecture.X64,
            WinPeLanguage = "en-US",
            UseCa2023 = false
        };
        var document = new FoundryConfigurationDocument
        {
            General = existingGeneral
        };

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplyLegacyGeneralSettings(
            document,
            legacyGeneralSettings: null);

        Assert.Equal(existingGeneral, migrated.General);
    }
}
