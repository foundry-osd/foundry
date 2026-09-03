// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.DependencyInjection;
using Foundry.Services.Autopilot;
using Foundry.Services.Configuration;

namespace Foundry.Tests.Configuration;

public sealed class FoundryConfigurationStateServiceTests
{
    [Fact]
    public void IsDeployConfigurationReady_WhenOobePasswordExistsWithoutDeploymentProtection_ReturnsFalse()
    {
        var oobeSecretStateService = new TestOobeAccountSecretStateService();
        oobeSecretStateService.SetAdministratorPassword("AdminPassword123!");
        oobeSecretStateService.SetAdministratorConfirmation("AdminPassword123!");

        IFoundryConfigurationStateService stateService = CreateStateService(
            new FoundryConfigurationDocument
            {
                Customization = new CustomizationSettings
                {
                    Oobe = new OobeSettings
                    {
                        IsEnabled = true,
                        EnableAdministratorAccount = true
                    }
                }
            },
            oobeAccountSecretStateService: oobeSecretStateService);

        Assert.False(stateService.IsDeployConfigurationReady);
    }

    [Fact]
    public void IsDeployConfigurationReady_WhenProtectedOobePasswordExists_ReturnsTrue()
    {
        var oobeSecretStateService = new TestOobeAccountSecretStateService();
        oobeSecretStateService.SetAdministratorPassword("AdminPassword123!");
        oobeSecretStateService.SetAdministratorConfirmation("AdminPassword123!");

        IFoundryConfigurationStateService stateService = CreateStateService(
            new FoundryConfigurationDocument
            {
                General = new GeneralSettings
                {
                    DeploymentProtection = new DeploymentProtectionSettings
                    {
                        IsEnabled = true
                    }
                },
                Customization = new CustomizationSettings
                {
                    Oobe = new OobeSettings
                    {
                        IsEnabled = true,
                        EnableAdministratorAccount = true
                    }
                }
            },
            deploymentProtectionSecretStateService: new TestDeploymentProtectionSecretStateService
            {
                IsValid = true
            },
            oobeAccountSecretStateService: oobeSecretStateService);

        Assert.True(stateService.IsDeployConfigurationReady);
    }

    [Fact]
    public void GenerateDeployConfigurationJson_WhenAdministratorPasswordIsUnconfirmed_ThrowsInvalidOperationException()
    {
        using var mediaProtection = DeploymentMediaProtectionService.CreateProtected("MediaPassword123".AsSpan());
        var oobeSecretStateService = new TestOobeAccountSecretStateService();
        oobeSecretStateService.SetAdministratorPassword("AdminPassword123!");

        IFoundryConfigurationStateService stateService = CreateStateService(
            new FoundryConfigurationDocument
            {
                General = new GeneralSettings
                {
                    DeploymentProtection = new DeploymentProtectionSettings
                    {
                        IsEnabled = true
                    }
                },
                Customization = new CustomizationSettings
                {
                    Oobe = new OobeSettings
                    {
                        IsEnabled = true,
                        EnableAdministratorAccount = true
                    }
                }
            },
            deploymentProtectionSecretStateService: new TestDeploymentProtectionSecretStateService
            {
                IsValid = true
            },
            oobeAccountSecretStateService: oobeSecretStateService);

        Assert.Throws<InvalidOperationException>(() => stateService.GenerateDeployConfigurationJson(
            deploymentSecretsKey: mediaProtection.DeploymentKey,
            protectionSettings: mediaProtection.Settings));
    }

    [Fact]
    public void GenerateDeployConfigurationJson_WhenAdditionalAccountPasswordConfirmationMismatches_ThrowsInvalidOperationException()
    {
        using var mediaProtection = DeploymentMediaProtectionService.CreateProtected("MediaPassword123".AsSpan());
        var oobeSecretStateService = new TestOobeAccountSecretStateService();
        oobeSecretStateService.SetAdditionalAccountPassword("account-1", "TechPassword123!");
        oobeSecretStateService.SetAdditionalAccountConfirmation("account-1", "DifferentPassword123!");

        IFoundryConfigurationStateService stateService = CreateStateService(
            new FoundryConfigurationDocument
            {
                General = new GeneralSettings
                {
                    DeploymentProtection = new DeploymentProtectionSettings
                    {
                        IsEnabled = true
                    }
                },
                Customization = new CustomizationSettings
                {
                    Oobe = new OobeSettings
                    {
                        IsEnabled = true,
                        AdditionalAccounts = new[]
                        {
                            new OobeAdditionalAccountSettings
                            {
                                Id = "account-1",
                                UserName = "Technician",
                                Type = OobeAccountType.Standard
                            }
                        }
                    }
                }
            },
            deploymentProtectionSecretStateService: new TestDeploymentProtectionSecretStateService
            {
                IsValid = true
            },
            oobeAccountSecretStateService: oobeSecretStateService);

        Assert.Throws<InvalidOperationException>(() => stateService.GenerateDeployConfigurationJson(
            deploymentSecretsKey: mediaProtection.DeploymentKey,
            protectionSettings: mediaProtection.Settings));
    }

    [Fact]
    public void GenerateDeployConfigurationJson_WhenOobePasswordExists_MergesTransientSecretsWithoutPersistingPlaintext()
    {
        using var mediaProtection = DeploymentMediaProtectionService.CreateProtected("MediaPassword123".AsSpan());
        var oobeSecretStateService = new TestOobeAccountSecretStateService();
        oobeSecretStateService.SetAdministratorPassword("AdminPassword123!");
        oobeSecretStateService.SetAdministratorConfirmation("AdminPassword123!");
        oobeSecretStateService.SetAdditionalAccountPassword("account-1", "TechPassword123!");
        oobeSecretStateService.SetAdditionalAccountConfirmation("account-1", "TechPassword123!");

        FoundryConfigurationDocument current = new()
        {
            General = new GeneralSettings
            {
                DeploymentProtection = new DeploymentProtectionSettings
                {
                    IsEnabled = true
                }
            },
            Customization = new CustomizationSettings
            {
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    EnableAdministratorAccount = true,
                    AdditionalAccounts = new[]
                    {
                        new OobeAdditionalAccountSettings
                        {
                            Id = "account-1",
                            UserName = "Technician",
                            Type = OobeAccountType.Standard
                        }
                    }
                }
            }
        };
        IFoundryConfigurationStateService stateService = CreateStateService(
            current,
            deploymentProtectionSecretStateService: new TestDeploymentProtectionSecretStateService
            {
                IsValid = true
            },
            oobeAccountSecretStateService: oobeSecretStateService);

        string persistedConfigurationJson = new FoundryConfigurationService().Serialize(stateService.Current);
        string deployConfigurationJson = stateService.GenerateDeployConfigurationJson(
            deploymentSecretsKey: mediaProtection.DeploymentKey,
            protectionSettings: mediaProtection.Settings);
        FoundryDeployConfigurationDocument deployConfiguration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(
            deployConfigurationJson,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;

        Assert.DoesNotContain("AdminPassword123!", persistedConfigurationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("TechPassword123!", persistedConfigurationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("AdminPassword123!", deployConfigurationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("TechPassword123!", deployConfigurationJson, StringComparison.Ordinal);
        Assert.NotNull(deployConfiguration.Customization.Oobe.AdministratorPasswordSecret);
        Assert.Equal(
            "AdminPassword123!",
            MediaSecretEnvelopeProtector.DecryptString(
                deployConfiguration.Customization.Oobe.AdministratorPasswordSecret!,
                mediaProtection.DeploymentKey,
                MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Collection(
            deployConfiguration.Customization.Oobe.AdditionalAccounts,
            account =>
            {
                Assert.NotNull(account.PasswordSecret);
                Assert.Equal(
                    "TechPassword123!",
                    MediaSecretEnvelopeProtector.DecryptString(
                        account.PasswordSecret!,
                        mediaProtection.DeploymentKey,
                        MediaSecretEnvelopeProtector.DeploymentKeyId));
            });
    }

    private static IFoundryConfigurationStateService CreateStateService(
        FoundryConfigurationDocument current,
        IDeployConfigurationGenerator? deployConfigurationGenerator = null,
        IDeploymentProtectionSecretStateService? deploymentProtectionSecretStateService = null,
        IOobeAccountSecretStateService? oobeAccountSecretStateService = null,
        IAutopilotHardwareHashSessionState? autopilotHardwareHashSessionState = null)
    {
        Type type = typeof(ServiceCollectionExtensions).Assembly.GetType(
            "Foundry.Services.Configuration.FoundryConfigurationStateService",
            throwOnError: true)!;
        object instance = RuntimeHelpers.GetUninitializedObject(type);

        SetField(type, instance, "<Current>k__BackingField", current);
        SetField(type, instance, "deployConfigurationGenerator", deployConfigurationGenerator ?? new DeployConfigurationGenerator());
        SetField(type, instance, "deploymentProtectionSecretStateService", deploymentProtectionSecretStateService ?? new TestDeploymentProtectionSecretStateService());
        SetField(type, instance, "oobeAccountSecretStateService", oobeAccountSecretStateService ?? new TestOobeAccountSecretStateService());
        SetField(type, instance, "autopilotHardwareHashSessionState", autopilotHardwareHashSessionState ?? new TestAutopilotHardwareHashSessionState());

        return (IFoundryConfigurationStateService)instance;
    }

    private static void SetField(Type type, object instance, string fieldName, object value)
    {
        type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);
    }

    private sealed class TestDeploymentProtectionSecretStateService : IDeploymentProtectionSecretStateService
    {
        public event EventHandler? Changed;

        public bool HasPassword { get; private set; }

        public bool HasConfirmation { get; private set; }

        public bool IsValid { get; set; }

        public bool ShouldRecommendStrongerPassword => false;

        public void SetPassword(string? value)
        {
            HasPassword = !string.IsNullOrEmpty(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetConfirmation(string? value)
        {
            HasConfirmation = !string.IsNullOrEmpty(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public char[] GetPasswordCopy() => [];

        public char[] GetConfirmationCopy() => [];

        public char[] GetConfirmedPasswordCopy() => [];

        public void Clear()
        {
            HasPassword = false;
            HasConfirmation = false;
            IsValid = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestOobeAccountSecretStateService : IOobeAccountSecretStateService
    {
        private readonly OobeAccountSecretState state = new();

        public event EventHandler? Changed;

        public void SetAdministratorPassword(string? value)
        {
            state.SetAdministratorPassword(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetAdministratorConfirmation(string? value)
        {
            state.SetAdministratorConfirmation(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public char[] GetAdministratorPasswordCopy() => state.GetAdministratorPasswordCopy();

        public char[] GetAdministratorConfirmationCopy() => state.GetAdministratorConfirmationCopy();

        public void SetAdditionalAccountPassword(string accountId, string? value)
        {
            state.SetAdditionalAccountPassword(accountId, value);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetAdditionalAccountConfirmation(string accountId, string? value)
        {
            state.SetAdditionalAccountConfirmation(accountId, value);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public char[] GetAdditionalAccountPasswordCopy(string accountId) => state.GetAdditionalAccountPasswordCopy(accountId);

        public char[] GetAdditionalAccountConfirmationCopy(string accountId) => state.GetAdditionalAccountConfirmationCopy(accountId);

        public OobeAccountConfigurationValidationResult Validate(OobeSettings settings) => state.Validate(settings);

        public void Update(OobeSettings settings)
        {
            state.Update(settings);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            state.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestAutopilotHardwareHashSessionState : IAutopilotHardwareHashSessionState
    {
        public bool HasConnectedTenant { get; set; }

        public AutopilotTenantOnboardingStatus? TenantOnboardingStatus { get; set; }

        public AutopilotBootMediaCertificateSettings BootMediaCertificate { get; set; } = new();

        public IReadOnlyList<AutopilotGraphKeyCredential> Certificates { get; set; } = [];

        public void ClearTenantConnection()
        {
            HasConnectedTenant = false;
            TenantOnboardingStatus = null;
            BootMediaCertificate = new AutopilotBootMediaCertificateSettings();
            Certificates = [];
        }
    }
}
