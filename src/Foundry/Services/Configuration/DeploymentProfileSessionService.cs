// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Profiles;
using Foundry.Services.Autopilot;

namespace Foundry.Services.Configuration;

/// <summary>Transfers explicitly selected secrets between profile records and the desktop's volatile authoring state.</summary>
public sealed class DeploymentProfileSessionService(
    IFoundryConfigurationStateService configurationState,
    INetworkSecretStateService networkSecrets,
    IDeploymentProtectionSecretStateService deploymentSecrets,
    IOobeAccountSecretStateService accountSecrets,
    IAutopilotHardwareHashSessionState autopilotSession)
{
    /// <summary>Freezes configuration and owned password buffers before reading source files off the UI thread.</summary>
    public Task<DeploymentProfileDocument> CaptureAsync(Guid profileId, string displayName, bool includeSecrets)
    {
        FoundryConfigurationDocument configuration = configurationState.Current with
        {
            Network = networkSecrets.ApplyRequiredSecrets(configurationState.Current.Network),
            Autopilot = configurationState.Current.Autopilot with
            {
                HardwareHashUpload = configurationState.Current.Autopilot.HardwareHashUpload with { BootMediaCertificate = autopilotSession.BootMediaCertificate }
            }
        };
        DeploymentProfileDocument profile = new()
        {
            ProfileId = profileId,
            DisplayName = displayName,
            Configuration = configuration
        };
        List<(DeploymentProfileSecret Secret, string? AccountId)> entries = [];
        try
        {
            if (configuration.General.DeploymentProtection.IsEnabled)
            {
                AddPair(ProfileSecretPurpose.DeploymentPassword, deploymentSecrets.GetPasswordCopy(), deploymentSecrets.GetConfirmationCopy());
            }

            if (configuration.Network.WifiProvisioned && configuration.Network.Wifi.IsEnabled && !configuration.Network.Wifi.HasEnterpriseProfile &&
                string.Equals(NetworkConfigurationValidator.NormalizeWifiSecurityType(configuration.Network.Wifi), NetworkConfigurationValidator.WifiSecurityPersonal, StringComparison.Ordinal))
            {
                Add(ProfileSecretPurpose.WifiPassphrase, configuration.Network.Wifi.Passphrase);
            }

            OobeSettings oobe = configuration.Customization.Oobe;
            if (oobe.IsEnabled)
            {
                if (oobe.EnableAdministratorAccount && oobe.UseAdministratorPassword)
                {
                    AddPair(ProfileSecretPurpose.AdministratorPassword, accountSecrets.GetAdministratorPasswordCopy(), accountSecrets.GetAdministratorConfirmationCopy());
                }

                foreach (OobeAdditionalAccountSettings account in oobe.AdditionalAccounts.Where(account => account.UsePassword))
                {
                    AddPair(ProfileSecretPurpose.AdditionalAccountPassword, accountSecrets.GetAdditionalAccountPasswordCopy(account.Id), accountSecrets.GetAdditionalAccountConfirmationCopy(account.Id), account.Id);
                }
            }

            if (IsPfx(configuration.Network.Dot1x.CertificatePath))
            {
                Add(ProfileSecretPurpose.WiredCertificatePassword, configuration.Network.Dot1x.CertificatePfxPassword);
            }

            if (IsPfx(configuration.Network.Wifi.CertificatePath))
            {
                Add(ProfileSecretPurpose.WifiCertificatePassword, configuration.Network.Wifi.CertificatePfxPassword);
            }

            if (!string.IsNullOrEmpty(configuration.Autopilot.HardwareHashUpload.BootMediaCertificate.PfxPath) ||
                configuration.Autopilot.IsEnabled && configuration.Autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload)
            {
                Add(ProfileSecretPurpose.AutopilotCertificatePassword, configuration.Autopilot.HardwareHashUpload.BootMediaCertificate.PfxPassword);
            }

            profile = profile with { Configuration = FreezeConfiguration(configuration) };
            configuration = profile.Configuration;
            return CaptureFrozenAsync();
        }
        catch
        {
            ClearCapturedValues();
            throw;
        }

        async Task<DeploymentProfileDocument> CaptureFrozenAsync()
        {
            try
            {
                return await Task.Run(() =>
                {
                    profile = profile with { Assets = DeploymentProfileAssetService.Capture(profile.Configuration, includeSecrets) };
                    return profile with
                    {
                        Secrets = new DeploymentProfileSecrets
                        {
                            Entries = entries.Select(entry => entry.Secret with
                            {
                                Identity = DeploymentProfileSecretBinding.Identity(entry.Secret.Purpose, profile, entry.AccountId)
                            }).ToArray()
                        }
                    };
                }).ConfigureAwait(false);
            }
            catch
            {
                ClearCapturedValues();
                DeploymentProfileAssetService.Clear(profile.Assets);
                throw;
            }
        }

        void ClearCapturedValues()
        {
            foreach (var entry in entries)
            {
                if (entry.Secret.Value is not null) CryptographicOperations.ZeroMemory(entry.Secret.Value);
            }
        }

        static bool IsPfx(string? path) => Path.GetExtension(path)?.ToLowerInvariant() is ".pfx" or ".p12";

        void Add(ProfileSecretPurpose purpose, string? value, string? accountId = null)
        {
            entries.Add((new DeploymentProfileSecret
            {
                Purpose = purpose,
                State = !includeSecrets ? ProfileValueState.Omitted : value is null ? ProfileValueState.Unavailable : value.Length == 0 ? ProfileValueState.Blank : ProfileValueState.Present,
                Value = includeSecrets && !string.IsNullOrEmpty(value) ? Encoding.UTF8.GetBytes(value) : null
            }, accountId));
        }

        void AddPair(ProfileSecretPurpose purpose, char[] password, char[] confirmation, string? accountId = null)
        {
            try
            {
                bool confirmed = password.Length > 0 && password.AsSpan().SequenceEqual(confirmation) &&
                    (purpose != ProfileSecretPurpose.DeploymentPassword || DeploymentProtectionPasswordRules.IsValid(password));
                entries.Add((new DeploymentProfileSecret
                {
                    Purpose = purpose,
                    State = !includeSecrets ? ProfileValueState.Omitted : !confirmed ? ProfileValueState.Unavailable : ProfileValueState.Present,
                    Value = includeSecrets && confirmed && password.Length > 0 ? Encoding.UTF8.GetBytes(password) : null
                }, accountId));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(confirmation.AsSpan()));
            }
        }
    }

    private static FoundryConfigurationDocument FreezeConfiguration(FoundryConfigurationDocument source)
    {
        AutopilotBootMediaCertificateSettings certificate = source.Autopilot.HardwareHashUpload.BootMediaCertificate with { PfxPassword = null };
        FoundryConfigurationDocument sanitized = source with
        {
            Network = source.Network with
            {
                Wifi = source.Network.Wifi with { Passphrase = null, PassphraseSecret = null, CertificatePfxPassword = null, CertificatePfxPasswordSecret = null },
                Dot1x = source.Network.Dot1x with { CertificatePfxPassword = null, CertificatePfxPasswordSecret = null }
            },
            Autopilot = source.Autopilot with { HardwareHashUpload = source.Autopilot.HardwareHashUpload with { BootMediaCertificate = certificate } }
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(sanitized);
        try
        {
            FoundryConfigurationDocument clone = JsonSerializer.Deserialize<FoundryConfigurationDocument>(bytes)!;
            return clone with { Autopilot = clone.Autopilot with { HardwareHashUpload = clone.Autopilot.HardwareHashUpload with { BootMediaCertificate = certificate } } };
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>Clears volatile credentials without reading or writing configuration storage.</summary>
    public void ClearSensitiveState()
    {
        networkSecrets.Update(new NetworkSettings());
        deploymentSecrets.Clear();
        accountSecrets.Update(new OobeSettings());
        autopilotSession.ClearTenantConnection();
        autopilotSession.BootMediaCertificate = new();
    }

    /// <summary>Restores matching secret contexts without establishing an authenticated Graph session.</summary>
    public void Activate(DeploymentProfileDocument profile, FoundryConfigurationDocument materialized)
    {
        string? deploymentPassword = Get(ProfileSecretPurpose.DeploymentPassword);
        string? administratorPassword = Get(ProfileSecretPurpose.AdministratorPassword);
        var additionalPasswords = materialized.Customization.Oobe.AdditionalAccounts
            .Select(account => (account.Id, Password: Get(ProfileSecretPurpose.AdditionalAccountPassword, account.Id))).ToArray();

        materialized = materialized with
        {
            Network = materialized.Network with
            {
                Dot1x = materialized.Network.Dot1x with { CertificatePfxPassword = Get(ProfileSecretPurpose.WiredCertificatePassword) },
                Wifi = materialized.Network.Wifi with
                {
                    Passphrase = Get(ProfileSecretPurpose.WifiPassphrase),
                    CertificatePfxPassword = Get(ProfileSecretPurpose.WifiCertificatePassword)
                }
            }
        };
        AutopilotBootMediaCertificateSettings boot = materialized.Autopilot.HardwareHashUpload.BootMediaCertificate with
        {
            PfxPassword = Get(ProfileSecretPurpose.AutopilotCertificatePassword),
            ValidatedThumbprint = null,
            ValidatedExpiresOnUtc = null
        };
        DeploymentProfileAsset? pfx = profile.Assets.SingleOrDefault(asset => asset.Kind == ProfileAssetKind.AutopilotCertificate);
        if (pfx?.Content is not null && boot.PfxPassword is not null)
        {
            AutopilotPfxValidationResult validation = AutopilotPfxCertificateValidator.Validate(pfx.Content, boot.PfxPassword);
            if (validation.IsValid)
            {
                boot = boot with { ValidatedThumbprint = validation.Thumbprint, ValidatedExpiresOnUtc = validation.ExpiresOnUtc };
            }
        }

        configurationState.Replace(materialized, () =>
        {
            ClearSensitiveState();
            deploymentSecrets.SetPassword(deploymentPassword);
            deploymentSecrets.SetConfirmation(deploymentPassword);
            accountSecrets.SetAdministratorPassword(administratorPassword);
            accountSecrets.SetAdministratorConfirmation(administratorPassword);
            foreach ((string id, string? password) in additionalPasswords)
            {
                accountSecrets.SetAdditionalAccountPassword(id, password.AsSpan());
                accountSecrets.SetAdditionalAccountConfirmation(id, password.AsSpan());
            }
            autopilotSession.BootMediaCertificate = boot;
        });

        string? Get(ProfileSecretPurpose purpose, string? accountId = null)
        {
            string identity = DeploymentProfileSecretBinding.Identity(purpose, profile, accountId);
            DeploymentProfileSecret? secret = profile.Secrets.Entries.SingleOrDefault(secret => secret.Purpose == purpose && secret.Identity == identity);
            return secret?.State switch
            {
                ProfileValueState.Blank => string.Empty,
                ProfileValueState.Present => Encoding.UTF8.GetString(secret.Value!),
                _ => null
            };
        }
    }
}
