// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed class DeploymentBuildSnapshotTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundrySnapshotTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CaptureAsync_FreezesMutableCollectionsBeforeReturningTask()
    {
        var packages = new List<string> { "Original.Package" };
        var document = new FoundryConfigurationDocument { Customization = new() { AppxRemoval = new() { IsEnabled = true, PackageNames = packages } } };
        using var secrets = new OobeAccountSecretState();
        Task<DeploymentBuildSnapshot> capture = DeploymentBuildSnapshot.CaptureAsync(document, secrets, [], root, TestContext.Current.CancellationToken);
        packages[0] = "Changed.Package";
        using DeploymentBuildSnapshot snapshot = await capture;
        Assert.Equal("Original.Package", Assert.Single(snapshot.Configuration.Customization.AppxRemoval.PackageNames));
        IList<string> exposed = (IList<string>)snapshot.Configuration.Customization.AppxRemoval.PackageNames;
        exposed[0] = "External.Change";
        Assert.Equal("Original.Package", Assert.Single(snapshot.Configuration.Customization.AppxRemoval.PackageNames));
    }

    [Fact]
    public async Task CaptureAsync_FreezesSecretsAcrossSourceChanges()
    {
        using var secrets = new OobeAccountSecretState();
        secrets.SetAdministratorPassword("AdminPassword123!");
        secrets.SetAdministratorConfirmation("AdminPassword123!");
        char[] mediaPassword = "MediaPassword123!".ToCharArray();
        var document = new FoundryConfigurationDocument
        {
            General = new() { DeploymentProtection = new() { IsEnabled = true } },
            Network = new() { WifiProvisioned = true, Wifi = new() { IsEnabled = true, Ssid = "Office", SecurityType = "WPA2/WPA3-Personal", Passphrase = "OriginalWifiPassword" } },
            Customization = new() { Oobe = new() { IsEnabled = true, EnableAdministratorAccount = true, UseAdministratorPassword = true } }
        };
        Task<DeploymentBuildSnapshot> capture = DeploymentBuildSnapshot.CaptureAsync(document, secrets, mediaPassword, root, TestContext.Current.CancellationToken);
        secrets.Clear();
        Array.Clear(mediaPassword);
        using DeploymentBuildSnapshot snapshot = await capture;
        using var protection = snapshot.CreateDeploymentProtectionMaterial();
        FoundryConnectProvisioningBundle connect = snapshot.CreateConnectProvisioningBundle(Path.Combine(root, "connect"));
        try
        {
            Assert.Equal("OriginalWifiPassword", MediaSecretEnvelopeProtector.DecryptString(connect.Configuration.Wifi.PassphraseSecret!, connect.MediaSecretsKey!));
            string json = snapshot.GenerateDeployConfigurationJson(deploymentSecretsKey: protection.DeploymentKey, protectionSettings: protection.Settings);
            FoundryDeployConfigurationDocument deploy = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal("AdminPassword123!", MediaSecretEnvelopeProtector.DecryptString(deploy.Customization.Oobe.AdministratorPasswordSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
            Assert.Null(snapshot.Configuration.Network.Wifi.Passphrase);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(connect.MediaSecretsKey!);
        }
    }

    [Fact]
    public async Task CaptureAsync_PinsUnattendAndDriverBytesThenRemovesPrivateFiles()
    {
        Directory.CreateDirectory(root);
        string answer = Path.Combine(root, "answer.xml");
        byte[] content = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\r\n<unattend />\r\n");
        File.WriteAllBytes(answer, content);
        string drivers = Path.Combine(root, "drivers");
        Directory.CreateDirectory(drivers);
        File.WriteAllText(Path.Combine(drivers, "original.inf"), "driver-before");
        var document = new FoundryConfigurationDocument
        {
            General = new() { CustomDriverDirectoryPath = drivers },
            Unattend = new() { IsEnabled = true, Files = [new() { Id = Guid.NewGuid().ToString("N"), DisplayName = "Answer", SourcePath = answer, ContentHash = Convert.ToHexString(SHA256.HashData(content)) }] }
        };
        using var secrets = new OobeAccountSecretState();
        DeploymentBuildSnapshot snapshot = await DeploymentBuildSnapshot.CaptureAsync(document, secrets, [], root, TestContext.Current.CancellationToken);
        string capturedAnswer = Assert.Single(snapshot.Configuration.Unattend.Files).SourcePath;
        string capturedDrivers = snapshot.Configuration.General.CustomDriverDirectoryPath!;
        File.WriteAllText(answer, "changed");
        File.WriteAllText(Path.Combine(drivers, "original.inf"), "driver-after");
        Assert.Equal(content, File.ReadAllBytes(capturedAnswer));
        Assert.Equal("driver-before", File.ReadAllText(Path.Combine(capturedDrivers, "original.inf")));
        snapshot.Dispose();
        Assert.False(File.Exists(capturedAnswer));
        Assert.False(Directory.Exists(capturedDrivers));
        Assert.Throws<ObjectDisposedException>(() => snapshot.CreateDeploymentProtectionMaterial());
    }

    [Fact]
    public async Task CaptureAsync_IgnoresDisabledStaleDependenciesAndPreservesTelemetry()
    {
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument
        {
            Network = new() { Dot1x = new() { IsEnabled = false, ProfileTemplatePath = "Z:/missing.xml" } },
            Telemetry = new() { IsEnabled = true, InstallId = "captured-install" },
            General = new() { IsoOutputPath = "C:/local-output.iso" }
        };
        using DeploymentBuildSnapshot snapshot = await DeploymentBuildSnapshot.CaptureAsync(document, secrets, [], root, TestContext.Current.CancellationToken);
        Assert.Equal("captured-install", snapshot.Configuration.Telemetry.InstallId);
        Assert.True(snapshot.Configuration.Telemetry.IsEnabled);
        Assert.Equal("C:/local-output.iso", snapshot.Configuration.General.IsoOutputPath);
    }

    [Fact]
    public async Task CaptureAsync_RejectsChangedUnattendAndCleansFailedCapture()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "answer.xml");
        File.WriteAllText(path, "changed");
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument { Unattend = new() { IsEnabled = true, Files = [new() { Id = Guid.NewGuid().ToString("N"), SourcePath = path, ContentHash = new string('0', 64) }] } };
        await Assert.ThrowsAsync<InvalidDataException>(() => DeploymentBuildSnapshot.CaptureAsync(document, secrets, [], root, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateDirectories(root));
    }

    [Fact]
    public async Task CaptureAsync_CancellationCleansPreparedState()
    {
        using var secrets = new OobeAccountSecretState();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeploymentBuildSnapshot.CaptureAsync(new(), secrets, [], root, new CancellationToken(true)));
        Assert.False(Directory.Exists(root) && Directory.EnumerateDirectories(root).Any());
    }

    [Fact]
    public async Task CaptureAsync_PreservesAutopilotSessionMetadataPasswordAndExactPfxBytes()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "certificate.pfx");
        byte[] original = [1, 2, 3, 4, 5];
        File.WriteAllBytes(source, original);
        var expiry = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new()
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = new()
                {
                    Tenant = new() { TenantId = "tenant", ApplicationObjectId = "application", ClientId = "client", ServicePrincipalObjectId = "principal" },
                    ActiveCertificate = new() { KeyId = "key", Thumbprint = "ABCDEF", ExpiresOnUtc = expiry },
                    BootMediaCertificate = new() { PfxPath = source, PfxPassword = "OriginalCertificatePassword", ValidatedThumbprint = "ABCDEF", ValidatedExpiresOnUtc = expiry }
                }
            }
        };
        using DeploymentBuildSnapshot snapshot = await DeploymentBuildSnapshot.CaptureAsync(document, secrets, [], root, TestContext.Current.CancellationToken);
        File.WriteAllBytes(source, [9, 9, 9]);
        using var protection = snapshot.CreateDeploymentProtectionMaterial();
        string json = snapshot.GenerateDeployConfigurationJson(deploymentSecretsKey: protection.DeploymentKey, protectionSettings: protection.Settings);
        FoundryDeployConfigurationDocument deploy = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(original, MediaSecretEnvelopeProtector.DecryptBytes(deploy.Autopilot.HardwareHashUpload.CertificatePfxSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Equal("OriginalCertificatePassword", MediaSecretEnvelopeProtector.DecryptString(deploy.Autopilot.HardwareHashUpload.CertificatePfxPasswordSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Null(snapshot.Configuration.Autopilot.HardwareHashUpload.BootMediaCertificate.PfxPassword);
        Assert.Equal("ABCDEF", snapshot.Configuration.Autopilot.HardwareHashUpload.BootMediaCertificate.ValidatedThumbprint);
    }

    [Fact]
    public async Task CaptureAsync_PreservesAdditionalAccountPasswordAndIntentionalBlank()
    {
        using var secrets = new OobeAccountSecretState();
        secrets.SetAdditionalAccountPassword("account", "AccountPassword123!");
        secrets.SetAdditionalAccountConfirmation("account", "AccountPassword123!");
        var document = new FoundryConfigurationDocument
        {
            General = new() { DeploymentProtection = new() { IsEnabled = true } },
            Customization = new() { Oobe = new() { IsEnabled = true, AdditionalAccounts = [new() { Id = "account", UserName = "Technician", UsePassword = true }, new() { Id = "blank", UserName = "BlankAccount", UsePassword = false }] } }
        };
        Task<DeploymentBuildSnapshot> capture = DeploymentBuildSnapshot.CaptureAsync(document, secrets, "MediaPassword123!", root, TestContext.Current.CancellationToken);
        secrets.Clear();
        using DeploymentBuildSnapshot snapshot = await capture;
        using var protection = snapshot.CreateDeploymentProtectionMaterial();
        string json = snapshot.GenerateDeployConfigurationJson(deploymentSecretsKey: protection.DeploymentKey, protectionSettings: protection.Settings);
        FoundryDeployConfigurationDocument deploy = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("AccountPassword123!", MediaSecretEnvelopeProtector.DecryptString(deploy.Customization.Oobe.AdditionalAccounts[0].PasswordSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.True(deploy.Customization.Oobe.AdditionalAccounts[1].PasswordIsBlank);
        Assert.Null(deploy.Customization.Oobe.AdditionalAccounts[1].PasswordSecret);
    }
    [Fact]
    public async Task CaptureAsync_RejectsUnavailableActiveDependencies()
    {
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument
        {
            Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplatePath = Path.Combine(root, "missing.xml") } }
        };
        await Assert.ThrowsAnyAsync<IOException>(() => DeploymentBuildSnapshot.CaptureAsync(document, secrets, [], root, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateDirectories(root));
    }
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
