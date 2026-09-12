// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Telemetry;

namespace Foundry.Core.Services.Profiles;

/// <summary>Owns one media build's frozen configuration, session secrets and private source copies.</summary>
public sealed class DeploymentBuildSnapshot : IDisposable
{
    private const long MaximumDriverBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumDriverEntries = 10_000;
    private readonly OobeAccountSecretState accountSecrets = new();
    private readonly char[] deploymentPassword;
    private readonly char[]? wifiPassphrase;
    private readonly char[]? wiredCertificatePassword;
    private readonly char[]? wifiCertificatePassword;
    private readonly char[]? autopilotCertificatePassword;
    private readonly string privateRootDirectory;
    private readonly string privateDirectory;
    private FoundryConfigurationDocument configuration;
    private bool isDisposed;

    private DeploymentBuildSnapshot(FoundryConfigurationDocument source, ReadOnlySpan<char> password, string root)
    {
        configuration = CloneConfiguration(source);
        deploymentPassword = password.ToArray();
        wifiPassphrase = source.Network.Wifi.Passphrase?.ToCharArray();
        wiredCertificatePassword = source.Network.Dot1x.CertificatePfxPassword?.ToCharArray();
        wifiCertificatePassword = source.Network.Wifi.CertificatePfxPassword?.ToCharArray();
        autopilotCertificatePassword = source.Autopilot.HardwareHashUpload.BootMediaCertificate.PfxPassword?.ToCharArray();
        privateRootDirectory = Path.GetFullPath(root);
        privateDirectory = Path.Combine(privateRootDirectory, Guid.NewGuid().ToString("N"));
    }

    /// <summary>Returns a detached configuration copy with stable staged paths and no plaintext passwords.</summary>
    public FoundryConfigurationDocument Configuration
    {
        get
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return CloneConfiguration(configuration);
        }
    }

    /// <summary>Copies configuration and secrets synchronously, then captures bounded source files off-thread before returning a usable snapshot.</summary>
    public static Task<DeploymentBuildSnapshot> CaptureAsync(
        FoundryConfigurationDocument configuration,
        OobeAccountSecretState accountSecrets,
        ReadOnlySpan<char> deploymentPassword,
        string privateRootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(accountSecrets);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateRootDirectory);
        var snapshot = new DeploymentBuildSnapshot(configuration, deploymentPassword, privateRootDirectory);
        try
        {
            snapshot.CopyAccountSecrets(accountSecrets);
            return snapshot.PrepareAsync(cancellationToken);
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    /// <summary>Generates Connect only from captured credentials and private dependency copies.</summary>
    public FoundryConnectProvisioningBundle CreateConnectProvisioningBundle(string stagingDirectoryPath, TelemetrySettings? telemetryOverride = null)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return new ConnectConfigurationGenerator().CreateProvisioningBundle(CreateGenerationDocument(telemetryOverride), stagingDirectoryPath);
    }

    /// <summary>Retains existing Deploy protection and account validation rules while using captured session material.</summary>
    public string GenerateDeployConfigurationJson(TelemetrySettings? telemetryOverride = null, byte[]? deploymentSecretsKey = null, DeployProtectionSettings? protectionSettings = null)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (!accountSecrets.Validate(configuration.Customization.Oobe).IsValid)
        {
            throw new InvalidOperationException("OOBE local account password confirmation is invalid.");
        }
        var generator = new DeployConfigurationGenerator();
        return generator.Serialize(generator.Generate(CreateGenerationDocument(telemetryOverride), deploymentSecretsKey, protectionSettings, accountSecrets));
    }

    /// <summary>Creates fresh per-media protection using the confirmed password captured before preparation began.</summary>
    public DeploymentMediaProtectionMaterial CreateDeploymentProtectionMaterial()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return configuration.General.DeploymentProtection.IsEnabled
            ? DeploymentMediaProtectionService.CreateProtected(deploymentPassword)
            : DeploymentMediaProtectionService.CreateUnprotected();
    }

    /// <summary>Clears owned password buffers and removes the private staging directory; cleanup errors are surfaced to the caller.</summary>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }
        isDisposed = true;
        accountSecrets.Dispose();
        Clear(deploymentPassword);
        Clear(wifiPassphrase);
        Clear(wiredCertificatePassword);
        Clear(wifiCertificatePassword);
        Clear(autopilotCertificatePassword);
        if (Directory.Exists(privateDirectory)
            && string.Equals(Path.GetDirectoryName(Path.GetFullPath(privateDirectory)), privateRootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(privateDirectory, recursive: true);
        }
    }

    private async Task<DeploymentBuildSnapshot> PrepareAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() => PrepareFiles(cancellationToken), cancellationToken).ConfigureAwait(false);
            return this;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void PrepareFiles(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CreatePrivateDirectory();
        FoundryConfigurationDocument sources = SelectActiveSources(configuration);
        IReadOnlyList<DeploymentProfileAsset> assets = DeploymentProfileAssetService.Capture(sources, includeContent: true, requireContent: true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            configuration = DeploymentProfileAssetService.Materialize(new DeploymentProfileDocument { Configuration = sources, Assets = assets }, privateDirectory);
        }
        finally
        {
            DeploymentProfileAssetService.Clear(assets);
        }
        if (!string.IsNullOrWhiteSpace(configuration.General.CustomDriverDirectoryPath))
        {
            string destination = Path.Combine(privateDirectory, "Drivers");
            CopyDrivers(configuration.General.CustomDriverDirectoryPath, destination, cancellationToken);
            configuration = configuration with { General = configuration.General with { CustomDriverDirectoryPath = destination } };
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void CreatePrivateDirectory()
    {
        Directory.CreateDirectory(privateRootDirectory);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("The current Windows user is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(privateDirectory).Create(security);
    }

    private static void CopyDrivers(string source, string destination, CancellationToken cancellationToken)
    {
        var pending = new Stack<(DirectoryInfo Source, string Destination)>();
        pending.Push((new DirectoryInfo(source), destination));
        long totalBytes = 0;
        int entries = 0;
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (pending.TryPop(out (DirectoryInfo Source, string Destination) current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((current.Source.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Custom driver snapshots do not follow reparse points.");
                }
                Directory.CreateDirectory(current.Destination);
                foreach (FileSystemInfo entry in current.Source.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > MaximumDriverEntries || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException("Custom driver snapshots exceed the entry limit or contain a reparse point.");
                    }
                    string target = Path.Combine(current.Destination, entry.Name);
                    if (entry is DirectoryInfo directory)
                    {
                        pending.Push((directory, target));
                        continue;
                    }
                    using FileStream input = new(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    totalBytes += input.Length;
                    if (totalBytes > MaximumDriverBytes)
                    {
                        throw new InvalidDataException("Custom driver snapshots exceed the supported size.");
                    }
                    using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    int count;
                    while ((count = input.Read(buffer)) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, count);
                    }
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private void CopyAccountSecrets(OobeAccountSecretState source)
    {
        Copy(source.GetAdministratorPasswordCopy(), accountSecrets.SetAdministratorPassword);
        Copy(source.GetAdministratorConfirmationCopy(), accountSecrets.SetAdministratorConfirmation);
        foreach (OobeAdditionalAccountSettings account in configuration.Customization.Oobe.AdditionalAccounts)
        {
            Copy(source.GetAdditionalAccountPasswordCopy(account.Id), value => accountSecrets.SetAdditionalAccountPassword(account.Id, value));
            Copy(source.GetAdditionalAccountConfirmationCopy(account.Id), value => accountSecrets.SetAdditionalAccountConfirmation(account.Id, value));
        }
    }

    private delegate void SetSecret(ReadOnlySpan<char> value);

    private static void Copy(char[] buffer, SetSecret setter)
    {
        try
        {
            setter(buffer);
        }
        finally
        {
            Clear(buffer);
        }
    }

    private FoundryConfigurationDocument CreateGenerationDocument(TelemetrySettings? telemetryOverride)
    {
        return configuration with
        {
            Network = configuration.Network with
            {
                Wifi = configuration.Network.Wifi with { Passphrase = AsString(wifiPassphrase), CertificatePfxPassword = AsString(wifiCertificatePassword) },
                Dot1x = configuration.Network.Dot1x with { CertificatePfxPassword = AsString(wiredCertificatePassword) }
            },
            Autopilot = configuration.Autopilot with
            {
                HardwareHashUpload = configuration.Autopilot.HardwareHashUpload with
                {
                    BootMediaCertificate = configuration.Autopilot.HardwareHashUpload.BootMediaCertificate with { PfxPassword = AsString(autopilotCertificatePassword) }
                }
            },
            Telemetry = telemetryOverride ?? configuration.Telemetry
        };
    }

    private static FoundryConfigurationDocument CloneConfiguration(FoundryConfigurationDocument source)
    {
        AutopilotBootMediaCertificateSettings bootCertificate = source.Autopilot.HardwareHashUpload.BootMediaCertificate with { PfxPassword = null };
        FoundryConfigurationDocument sanitized = source with
        {
            Network = source.Network with
            {
                Wifi = source.Network.Wifi with { Passphrase = null, PassphraseSecret = null, CertificatePfxPassword = null, CertificatePfxPasswordSecret = null },
                Dot1x = source.Network.Dot1x with { CertificatePfxPassword = null, CertificatePfxPasswordSecret = null }
            },
            Autopilot = source.Autopilot with { HardwareHashUpload = source.Autopilot.HardwareHashUpload with { BootMediaCertificate = bootCertificate } }
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(sanitized, ConfigurationJsonDefaults.SerializerOptions);
        try
        {
            FoundryConfigurationDocument clone = JsonSerializer.Deserialize<FoundryConfigurationDocument>(bytes, ConfigurationJsonDefaults.SerializerOptions)!;
            return clone with { Autopilot = clone.Autopilot with { HardwareHashUpload = clone.Autopilot.HardwareHashUpload with { BootMediaCertificate = bootCertificate } } };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static FoundryConfigurationDocument SelectActiveSources(FoundryConfigurationDocument source)
    {
        bool wifiEnabled = source.Network.WifiProvisioned && source.Network.Wifi.IsEnabled;
        bool hardwareHashEnabled = source.Autopilot.IsEnabled && source.Autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload;
        return source with
        {
            Network = source.Network with
            {
                Dot1x = source.Network.Dot1x.IsEnabled ? source.Network.Dot1x : source.Network.Dot1x with { ProfileTemplatePath = null, CertificatePath = null },
                Wifi = source.Network.Wifi with
                {
                    EnterpriseProfileTemplatePath = wifiEnabled && source.Network.Wifi.HasEnterpriseProfile ? source.Network.Wifi.EnterpriseProfileTemplatePath : null,
                    CertificatePath = wifiEnabled ? source.Network.Wifi.CertificatePath : null
                }
            },
            Unattend = source.Unattend.IsEnabled ? source.Unattend : source.Unattend with { Files = [] },
            Autopilot = source.Autopilot with
            {
                HardwareHashUpload = source.Autopilot.HardwareHashUpload with
                {
                    BootMediaCertificate = hardwareHashEnabled ? source.Autopilot.HardwareHashUpload.BootMediaCertificate : new()
                }
            }
        };
    }

    private static string? AsString(char[]? value) => value is null ? null : new string(value);
    private static void Clear(char[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
        }
    }
}
