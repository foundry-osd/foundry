// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;
using System.Text.Json;

namespace Foundry.Core.Tests.Configuration;

public sealed class DeployConfigurationGeneratorTests
{
    [Fact]
    public void Generate_WhenMachineNamingIsDisabled_ClearsPrefixAndKeepsManualSuffixEditable()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = false,
                    Prefix = "FD-",
                    AutoGenerateName = true,
                    AllowManualSuffixEdit = false
                }
            }
        };

        var result = generator.Generate(document);

        Assert.False(result.Customization.MachineNaming.IsEnabled);
        Assert.Null(result.Customization.MachineNaming.Prefix);
        Assert.False(result.Customization.MachineNaming.AutoGenerateName);
        Assert.True(result.Customization.MachineNaming.AllowManualSuffixEdit);
    }

    [Fact]
    public void Generate_WhenNetworkProfileRoamingIsDisabled_DisablesDeployRoaming()
    {
        var generator = new DeployConfigurationGenerator();

        FoundryDeployConfigurationDocument result = generator.Generate(new FoundryConfigurationDocument());

        Assert.False(result.Network.ProfileRoaming.IsEnabled);
        Assert.False(result.Network.ProfileRoaming.IncludePrivateKeyMaterial);
    }

    [Fact]
    public void Generate_WhenNetworkProfileRoamingIsEnabled_PropagatesDeployRoaming()
    {
        var generator = new DeployConfigurationGenerator();

        FoundryDeployConfigurationDocument result = generator.Generate(new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                RoamWifiProfilesToWindows = true,
                RoamPrivateKeyMaterialToWindows = true
            }
        });

        Assert.True(result.Network.ProfileRoaming.IsEnabled);
        Assert.True(result.Network.ProfileRoaming.IncludePrivateKeyMaterial);
    }

    [Fact]
    public void Generate_PropagatesTelemetrySettings()
    {
        var generator = new DeployConfigurationGenerator();
        var telemetry = new TelemetrySettings
        {
            IsEnabled = false,
            InstallId = "install-id",
            HostUrl = TelemetryDefaults.PostHogEuHost,
            ProjectToken = "project-token",
            RuntimePayloadSource = TelemetryRuntimePayloadSources.Release
        };

        var result = generator.Generate(new FoundryConfigurationDocument { Telemetry = telemetry });

        Assert.Same(telemetry, result.Telemetry);
    }

    [Fact]
    public void Generate_WhenOobeCustomizationIsEnabled_PropagatesOobeSettings()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
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
                }
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.Customization.Oobe.IsEnabled);
        Assert.True(result.Customization.Oobe.SkipLicenseTerms);
        Assert.Equal(DeployOobeDiagnosticDataLevel.Off, result.Customization.Oobe.DiagnosticDataLevel);
        Assert.True(result.Customization.Oobe.HidePrivacySetup);
        Assert.False(result.Customization.Oobe.AllowTailoredExperiences);
        Assert.False(result.Customization.Oobe.AllowAdvertisingId);
        Assert.False(result.Customization.Oobe.AllowOnlineSpeechRecognition);
        Assert.False(result.Customization.Oobe.AllowInkingAndTypingDiagnostics);
        Assert.Equal(DeployOobeLocationAccessMode.ForceOff, result.Customization.Oobe.LocationAccess);
    }

    [Fact]
    public void Generate_WhenOobeCustomizationIsDisabled_DoesNotEnableRuntimeOobeSettings()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                Oobe = new OobeSettings
                {
                    IsEnabled = false,
                    DiagnosticDataLevel = OobeDiagnosticDataLevel.Optional,
                    LocationAccess = OobeLocationAccessMode.ForceOff
                }
            }
        };

        var result = generator.Generate(document);

        Assert.False(result.Customization.Oobe.IsEnabled);
        Assert.Equal(DeployOobeDiagnosticDataLevel.Required, result.Customization.Oobe.DiagnosticDataLevel);
        Assert.Equal(DeployOobeLocationAccessMode.UserControlled, result.Customization.Oobe.LocationAccess);
    }

    [Fact]
    public void Generate_WhenAppxRemovalIsEnabled_PropagatesDistinctPackageNames()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                AppxRemoval = new AppxRemovalSettings
                {
                    IsEnabled = true,
                    PackageNames =
                    [
                        "Microsoft.BingNews",
                        " ",
                        "Microsoft.BingWeather",
                        "Microsoft.BingNews"
                    ]
                }
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.Customization.AppxRemoval.IsEnabled);
        Assert.Equal(["Microsoft.BingNews", "Microsoft.BingWeather"], result.Customization.AppxRemoval.PackageNames);
    }

    [Fact]
    public void Generate_WhenAppxRemovalIsDisabled_DoesNotPropagatePackageNames()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                AppxRemoval = new AppxRemovalSettings
                {
                    IsEnabled = false,
                    PackageNames = ["Microsoft.BingNews"]
                }
            }
        };

        var result = generator.Generate(document);

        Assert.False(result.Customization.AppxRemoval.IsEnabled);
        Assert.Empty(result.Customization.AppxRemoval.PackageNames);
    }

    [Fact]
    public void Generate_WhenAiComponentRemovalIsEnabled_PropagatesSelectedOptions()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
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
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.Customization.AiComponentRemoval.IsEnabled);
        Assert.True(result.Customization.AiComponentRemoval.RemoveCopilot);
        Assert.True(result.Customization.AiComponentRemoval.RemoveAiHub);
        Assert.True(result.Customization.AiComponentRemoval.DisableRecall);
        Assert.True(result.Customization.AiComponentRemoval.DisableClickToDo);
        Assert.True(result.Customization.AiComponentRemoval.DisableAiServiceAutoStart);
        Assert.True(result.Customization.AiComponentRemoval.DisableEdgeAi);
        Assert.True(result.Customization.AiComponentRemoval.DisablePaintAi);
        Assert.True(result.Customization.AiComponentRemoval.DisableNotepadAi);
    }

    [Fact]
    public void Generate_WhenAiComponentRemovalIsDisabled_DoesNotPropagateOptions()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                AiComponentRemoval = new AiComponentRemovalSettings
                {
                    IsEnabled = false,
                    RemoveCopilot = true,
                    RemoveAiHub = true,
                    DisableRecall = true,
                    DisableClickToDo = true,
                    DisableAiServiceAutoStart = true,
                    DisableEdgeAi = true,
                    DisablePaintAi = true,
                    DisableNotepadAi = true
                }
            }
        };

        var result = generator.Generate(document);

        Assert.False(result.Customization.AiComponentRemoval.IsEnabled);
        Assert.False(result.Customization.AiComponentRemoval.RemoveCopilot);
        Assert.False(result.Customization.AiComponentRemoval.RemoveAiHub);
        Assert.False(result.Customization.AiComponentRemoval.DisableRecall);
        Assert.False(result.Customization.AiComponentRemoval.DisableClickToDo);
        Assert.False(result.Customization.AiComponentRemoval.DisableAiServiceAutoStart);
        Assert.False(result.Customization.AiComponentRemoval.DisableEdgeAi);
        Assert.False(result.Customization.AiComponentRemoval.DisablePaintAi);
        Assert.False(result.Customization.AiComponentRemoval.DisableNotepadAi);
    }

    [Fact]
    public void Generate_WhenLegacyAiPackagesAreSelectedForAppxRemoval_MigratesThemToAiComponentRemoval()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                AppxRemoval = new AppxRemovalSettings
                {
                    IsEnabled = true,
                    PackageNames =
                    [
                        "Microsoft.Copilot",
                        "Microsoft.Windows.AIHub",
                        "Microsoft.BingWeather"
                    ]
                }
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.Customization.AppxRemoval.IsEnabled);
        Assert.Equal(["Microsoft.BingWeather"], result.Customization.AppxRemoval.PackageNames);
        Assert.True(result.Customization.AiComponentRemoval.IsEnabled);
        Assert.True(result.Customization.AiComponentRemoval.RemoveCopilot);
        Assert.True(result.Customization.AiComponentRemoval.RemoveAiHub);
        Assert.False(result.Customization.AiComponentRemoval.DisableRecall);
        Assert.False(result.Customization.AiComponentRemoval.DisableClickToDo);
        Assert.False(result.Customization.AiComponentRemoval.DisableAiServiceAutoStart);
        Assert.False(result.Customization.AiComponentRemoval.DisableEdgeAi);
        Assert.False(result.Customization.AiComponentRemoval.DisablePaintAi);
        Assert.False(result.Customization.AiComponentRemoval.DisableNotepadAi);
    }

    [Fact]
    public void Generate_CanonicalizesOperatingSystemSelectionAndDropsDefaultsOutsideAllowedValues()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            OperatingSystemSelection = new OperatingSystemSelectionSettings
            {
                IsEnabled = true,
                AllowedLanguageCodes = [" fr_fr ", "EN-us", "fr-FR", ""],
                DefaultLanguageCode = "de-DE",
                AllowedReleaseIds = ["25h2", "24H2", "25H2", ""],
                DefaultReleaseId = "23H2",
                AllowedLicenseChannels = ["retail", "VOL", "ret", ""],
                DefaultLicenseChannel = "volume",
                AllowedEditions = [" enterprise ", "Pro", "Enterprise", ""],
                DefaultEdition = "Education"
            },
            Localization = new LocalizationSettings
            {
                DefaultTimeZoneId = "Romance Standard Time"
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.OperatingSystemSelection.IsEnabled);
        Assert.Equal(["fr-FR", "en-US"], result.OperatingSystemSelection.AllowedLanguageCodes);
        Assert.Null(result.OperatingSystemSelection.DefaultLanguageCode);
        Assert.Equal(["25H2", "24H2"], result.OperatingSystemSelection.AllowedReleaseIds);
        Assert.Null(result.OperatingSystemSelection.DefaultReleaseId);
        Assert.Equal(["RET", "VOL"], result.OperatingSystemSelection.AllowedLicenseChannels);
        Assert.Equal("VOL", result.OperatingSystemSelection.DefaultLicenseChannel);
        Assert.Equal(["Enterprise", "Pro"], result.OperatingSystemSelection.AllowedEditions);
        Assert.Null(result.OperatingSystemSelection.DefaultEdition);
        Assert.Equal("Romance Standard Time", result.Localization.DefaultTimeZoneId);
    }

    [Fact]
    public void Generate_WhenOperatingSystemSelectionAllowedListHasOneValue_ForcesDefaultToThatValue()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            OperatingSystemSelection = new OperatingSystemSelectionSettings
            {
                IsEnabled = true,
                AllowedLanguageCodes = ["fr-FR"],
                AllowedReleaseIds = ["24H2"],
                AllowedLicenseChannels = ["VOL"],
                AllowedEditions = ["Enterprise"]
            }
        };

        var result = generator.Generate(document);

        Assert.Equal("fr-FR", result.OperatingSystemSelection.DefaultLanguageCode);
        Assert.Equal("24H2", result.OperatingSystemSelection.DefaultReleaseId);
        Assert.Equal("VOL", result.OperatingSystemSelection.DefaultLicenseChannel);
        Assert.Equal("Enterprise", result.OperatingSystemSelection.DefaultEdition);
    }

    [Fact]
    public void Generate_WhenOperatingSystemSelectionIsDisabled_DoesNotPropagatePolicy()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            OperatingSystemSelection = new OperatingSystemSelectionSettings
            {
                IsEnabled = false,
                AllowedLanguageCodes = ["fr-FR"],
                DefaultLanguageCode = "fr-FR",
                AllowedReleaseIds = ["25H2"],
                DefaultReleaseId = "25H2",
                AllowedLicenseChannels = ["VOL"],
                DefaultLicenseChannel = "VOL",
                AllowedEditions = ["Enterprise"],
                DefaultEdition = "Enterprise"
            }
        };

        var result = generator.Generate(document);

        Assert.False(result.OperatingSystemSelection.IsEnabled);
        Assert.Empty(result.OperatingSystemSelection.AllowedLanguageCodes);
        Assert.Null(result.OperatingSystemSelection.DefaultLanguageCode);
        Assert.Empty(result.OperatingSystemSelection.AllowedReleaseIds);
        Assert.Null(result.OperatingSystemSelection.DefaultReleaseId);
        Assert.Empty(result.OperatingSystemSelection.AllowedLicenseChannels);
        Assert.Null(result.OperatingSystemSelection.DefaultLicenseChannel);
        Assert.Empty(result.OperatingSystemSelection.AllowedEditions);
        Assert.Null(result.OperatingSystemSelection.DefaultEdition);
    }

    [Fact]
    public void Generate_DoesNotMigrateLegacyLocalizationLanguageSelection()
    {
        const string json = """
            {
              "schemaVersion": 8,
              "localization": {
                "visibleLanguageCodes": ["fr-FR"],
                "defaultLanguageCodeOverride": "fr-FR",
                "forceSingleVisibleLanguage": true
              }
            }
            """;
        var generator = new DeployConfigurationGenerator();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        FoundryConfigurationDocument document = JsonSerializer.Deserialize<FoundryConfigurationDocument>(json, options)!;

        var result = generator.Generate(document);

        Assert.Empty(result.OperatingSystemSelection.AllowedLanguageCodes);
        Assert.Null(result.OperatingSystemSelection.DefaultLanguageCode);
    }

    [Fact]
    public void Serialize_WhenDefaultTimeZoneIdIsSet_WritesCamelCaseProperty()
    {
        var generator = new DeployConfigurationGenerator();
        var document = generator.Generate(new FoundryConfigurationDocument
        {
            Localization = new LocalizationSettings
            {
                DefaultTimeZoneId = "Romance Standard Time"
            }
        });

        string json = generator.Serialize(document);

        Assert.Contains("\"defaultTimeZoneId\": \"Romance Standard Time\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ResolvesDefaultAutopilotProfileFolder()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                DefaultProfileId = "profile-b",
                Profiles =
                [
                    CreateProfile("profile-a", "profile-a-folder"),
                    CreateProfile("profile-b", "profile-b-folder")
                ]
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.Autopilot.IsEnabled);
        Assert.Equal("profile-b-folder", result.Autopilot.DefaultProfileFolderName);
    }

    [Fact]
    public void Generate_WhenJsonProfileModeHasNoSelectedProfile_ThrowsInvalidOperationException()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.JsonProfile
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(document));
        Assert.Contains("JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WhenHardwareHashModeIsEnabled_DoesNotRequireSelectedProfile()
    {
        var generator = new DeployConfigurationGenerator();
        DateTimeOffset expiration = DateTimeOffset.UtcNow.AddMonths(6);
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = CreateCompleteHardwareHashSettings(expiration)
            }
        };

        var result = generator.Generate(document);

        Assert.True(result.Autopilot.IsEnabled);
        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, result.Autopilot.ProvisioningMode);
        Assert.Null(result.Autopilot.DefaultProfileFolderName);
        Assert.Equal("tenant-id", result.Autopilot.HardwareHashUpload.TenantId);
        Assert.Equal("client-id", result.Autopilot.HardwareHashUpload.ClientId);
        Assert.Equal("certificate-key-id", result.Autopilot.HardwareHashUpload.ActiveCertificateKeyId);
        Assert.Equal("ABCDEF123456", result.Autopilot.HardwareHashUpload.ActiveCertificateThumbprint);
        Assert.Equal(expiration, result.Autopilot.HardwareHashUpload.ActiveCertificateExpiresOnUtc);
        Assert.Equal("Sales", result.Autopilot.HardwareHashUpload.DefaultGroupTag);

        string json = generator.Serialize(result);
        Assert.DoesNotContain("knownGroupTags", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WhenInteractiveHardwareHashModeIsEnabled_DoesNotRequireSelectedProfileOrCertificateMetadata()
    {
        var generator = new DeployConfigurationGenerator();
        DateTimeOffset expiration = DateTimeOffset.UtcNow.AddMonths(6);
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.InteractiveHardwareHashUpload,
                HardwareHashUpload = CreateCompleteHardwareHashSettings(expiration)
            }
        };

        FoundryDeployConfigurationDocument result = generator.Generate(document, mediaSecretsKey: [1, 2, 3]);

        Assert.True(result.Autopilot.IsEnabled);
        Assert.Equal(AutopilotProvisioningMode.InteractiveHardwareHashUpload, result.Autopilot.ProvisioningMode);
        Assert.Null(result.Autopilot.DefaultProfileFolderName);
        Assert.Null(result.Autopilot.HardwareHashUpload.TenantId);
        Assert.Null(result.Autopilot.HardwareHashUpload.ClientId);
        Assert.Null(result.Autopilot.HardwareHashUpload.ActiveCertificateKeyId);
        Assert.Null(result.Autopilot.HardwareHashUpload.ActiveCertificateThumbprint);
        Assert.Null(result.Autopilot.HardwareHashUpload.ActiveCertificateExpiresOnUtc);
        Assert.Null(result.Autopilot.HardwareHashUpload.DefaultGroupTag);
        Assert.Null(result.Autopilot.HardwareHashUpload.CertificatePfxSecret);
        Assert.Null(result.Autopilot.HardwareHashUpload.CertificatePfxPasswordSecret);

        string json = generator.Serialize(result);
        Assert.Contains("\"provisioningMode\": \"interactiveHardwareHashUpload\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("certificatePfxSecret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certificatePfxPasswordSecret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WhenHardwareHashDefaultGroupTagIsNotKnown_PreservesDefaultGroupTag()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = CreateCompleteHardwareHashSettings(DateTimeOffset.UtcNow.AddMonths(6)) with
                {
                    KnownGroupTags = [],
                    DefaultGroupTag = "KIOSK"
                }
            }
        };

        var result = generator.Generate(document);

        Assert.Equal("KIOSK", result.Autopilot.HardwareHashUpload.DefaultGroupTag);
    }

    [Fact]
    public void Generate_WhenHardwareHashModeHasMediaKey_EmbedsEncryptedPfxSecrets()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-deploy-config-{Guid.NewGuid():N}");
        string pfxPath = Path.Combine(root, "certificate.pfx");
        byte[] pfxBytes = [1, 2, 3, 4, 5];
        byte[] mediaKey = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        Directory.CreateDirectory(root);
        File.WriteAllBytes(pfxPath, pfxBytes);

        try
        {
            var generator = new DeployConfigurationGenerator();
            var document = new FoundryConfigurationDocument
            {
                Autopilot = new AutopilotSettings
                {
                    IsEnabled = true,
                    ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                    HardwareHashUpload = CreateCompleteHardwareHashSettings(DateTimeOffset.UtcNow.AddMonths(6)) with
                    {
                        BootMediaCertificate = new AutopilotBootMediaCertificateSettings
                        {
                            PfxPath = pfxPath,
                            PfxPassword = "PfxPassword-DoNotLeak",
                            ValidatedThumbprint = "ABCDEF123456",
                            ValidatedExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(6)
                        }
                    }
                }
            };

            FoundryDeployConfigurationDocument result = generator.Generate(document, mediaKey);

            Assert.NotNull(result.Autopilot.HardwareHashUpload.CertificatePfxSecret);
            Assert.NotNull(result.Autopilot.HardwareHashUpload.CertificatePfxPasswordSecret);
            Assert.Equal(pfxBytes, MediaSecretEnvelopeProtector.DecryptBytes(result.Autopilot.HardwareHashUpload.CertificatePfxSecret!, mediaKey));
            Assert.Equal("PfxPassword-DoNotLeak", MediaSecretEnvelopeProtector.DecryptString(result.Autopilot.HardwareHashUpload.CertificatePfxPasswordSecret!, mediaKey));

            string json = generator.Serialize(result);
            Assert.DoesNotContain(Convert.ToBase64String(pfxBytes), json, StringComparison.Ordinal);
            Assert.DoesNotContain("PfxPassword-DoNotLeak", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generate_WhenHardwareHashModeNeedsSecretsWithInvalidMediaKey_ThrowsInvalidOperationException()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-deploy-config-{Guid.NewGuid():N}");
        string pfxPath = Path.Combine(root, "certificate.pfx");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(pfxPath, [1, 2, 3]);

        try
        {
            var generator = new DeployConfigurationGenerator();
            var document = new FoundryConfigurationDocument
            {
                Autopilot = new AutopilotSettings
                {
                    IsEnabled = true,
                    ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                    HardwareHashUpload = CreateCompleteHardwareHashSettings(DateTimeOffset.UtcNow.AddMonths(6)) with
                    {
                        BootMediaCertificate = new AutopilotBootMediaCertificateSettings
                        {
                            PfxPath = pfxPath,
                            PfxPassword = "password",
                            ValidatedThumbprint = "ABCDEF123456",
                            ValidatedExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(6)
                        }
                    }
                }
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(document, []));
            Assert.Contains("media secret key", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Generate_WhenHardwareHashCertificateIsExpired_ThrowsInvalidOperationException()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = CreateCompleteHardwareHashSettings(DateTimeOffset.UtcNow.AddDays(-1))
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(document));
        Assert.Contains("certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_WhenAutopilotIsDisabledWithNullHardwareHashSettings_DoesNotThrow()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = false,
                HardwareHashUpload = null!
            }
        };

        var result = generator.Generate(document);

        Assert.False(result.Autopilot.IsEnabled);
        Assert.Equal(AutopilotProvisioningMode.JsonProfile, result.Autopilot.ProvisioningMode);
        Assert.NotNull(result.Autopilot.HardwareHashUpload);
    }

    [Fact]
    public void Generate_WhenProvisioningModeIsUnsupported_ThrowsInvalidOperationException()
    {
        var generator = new DeployConfigurationGenerator();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = (AutopilotProvisioningMode)999
            }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(document));
        Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AutopilotProfileSettings CreateProfile(string id, string folderName)
    {
        return new AutopilotProfileSettings
        {
            Id = id,
            DisplayName = id,
            FolderName = folderName,
            Source = "import",
            ImportedAtUtc = DateTimeOffset.UtcNow,
            JsonContent = "{}"
        };
    }

    private static AutopilotHardwareHashUploadSettings CreateCompleteHardwareHashSettings(DateTimeOffset expiration)
    {
        return new AutopilotHardwareHashUploadSettings
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
                ExpiresOnUtc = expiration
            },
            BootMediaCertificate = new AutopilotBootMediaCertificateSettings
            {
                PfxPath = @"E:\Secrets\foundry-osd-autopilot-registration.pfx",
                PfxPassword = "correct-password",
                ValidatedThumbprint = "ABCDEF123456",
                ValidatedExpiresOnUtc = expiration
            },
            KnownGroupTags = ["Sales", "Engineering"],
            DefaultGroupTag = "Sales"
        };
    }
}
