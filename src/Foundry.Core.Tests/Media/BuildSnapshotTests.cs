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
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Media;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Media;

public sealed class BuildSnapshotTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundrySnapshotTests", Guid.NewGuid().ToString("N"));
    private readonly string libraryRoot;
    private readonly AuthoringFileLibrary library;

    public BuildSnapshotTests()
    {
        // The library lives beside the snapshot root, so assertions about private snapshot directories ignore it.
        libraryRoot = root + "-library";
        library = new AuthoringFileLibrary(libraryRoot);
    }

    [Fact]
    public async Task SnapshotCopiesThePasswordOfEveryReferencedAccount()
    {
        using var accounts = new OobeAccountSecretState();
        using var domain = new DomainJoinSecretState();
        var context = new DomainJoinCredentialContext("example.com", "EXAMPLE\\joiner");
        var dedicated = new DomainJoinCredentialContext("emea.example.com", "EMEA\\joiner");
        domain.SetPassword(context.AccountName, " frozen password ");
        domain.SetPassword(dedicated.AccountName, "second");
        var document = new FoundryConfigurationDocument
        {
            General = new() { DeploymentProtection = new() { IsEnabled = true } },
            DomainJoin = new()
            {
                IsEnabled = true,
                Mode = DomainJoinMode.Automatic,
                SharedAccountName = context.AccountName,
                DefaultDomainId = "a",
                Domains = [new() { Id = "a", DomainName = context.DomainName }, new() { Id = "b", DomainName = dedicated.DomainName, AccountName = dedicated.AccountName }]
            }
        };
        Task<BuildSnapshot> capturing = BuildSnapshot.CaptureAsync(document, accounts, domain, "MediaPassword123!", root, null, TestContext.Current.CancellationToken);
        domain.Clear();
        using BuildSnapshot snapshot = await capturing;
        using var protection = snapshot.CreateDeploymentProtectionMaterial();
        string json = snapshot.GenerateDeployConfigurationJson(deploymentSecretsKey: protection.DeploymentKey, protectionSettings: protection.Settings);
        var deploy = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        byte[] payload = MediaSecretEnvelopeProtector.DecryptBytes(deploy.DomainJoin.Domains[0].EncryptedCredentials!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId);
        byte[] second = MediaSecretEnvelopeProtector.DecryptBytes(deploy.DomainJoin.Domains[1].EncryptedCredentials!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId);
        try
        {
            using var decoded = DomainJoinCredentialPayloadCodec.Decode(payload, context);
            using var decodedSecond = DomainJoinCredentialPayloadCodec.Decode(second, dedicated);
            Assert.Equal(" frozen password ", new string(decoded.Password.Span));
            Assert.Equal("second", new string(decodedSecond.Password.Span));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(second);
        }
    }

    [Fact]
    public async Task SnapshotCarriesNetworkCachePasswordsIntoDeployConfiguration()
    {
        using var accounts = new OobeAccountSecretState();
        var passwords = new Dictionary<string, string> { ["a1"] = "share password" };
        var document = new FoundryConfigurationDocument
        {
            General = new() { DeploymentProtection = new() { IsEnabled = true } },
            NetworkCache = new()
            {
                IsEnabled = true,
                Accounts = [new() { Id = "a1", UserName = "CONTOSO\\svc" }],
                Shares = [new() { Id = "s1", Path = @"\\fs01\cache", AccountId = "a1" }],
                Scopes = [new() { Id = "c1", Name = "All", Kind = NetworkCacheScopeKind.Subnet, Value = "0.0.0.0/0", ShareIds = ["s1"] }]
            }
        };
        Task<BuildSnapshot> capturing = BuildSnapshot.CaptureAsync(
            document, new BuildSnapshotSources(library) { NetworkCachePasswords = passwords }, accounts, null, "MediaPassword123!", root, null, TestContext.Current.CancellationToken);
        passwords.Clear();
        using BuildSnapshot snapshot = await capturing;
        using var protection = snapshot.CreateDeploymentProtectionMaterial();

        string json = snapshot.GenerateDeployConfigurationJson(deploymentSecretsKey: protection.DeploymentKey, protectionSettings: protection.Settings);

        var deploy = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        byte[] payload = MediaSecretEnvelopeProtector.DecryptBytes(deploy.NetworkCache.Shares[0].EncryptedCredentials!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId);
        try
        {
            using var decoded = Foundry.Core.Services.NetworkCache.NetworkShareCredentialPayloadCodec.Decode(payload, new(@"\\fs01\cache", "CONTOSO\\svc"));
            Assert.Equal("share password", new string(decoded.Password.Span));
            Assert.DoesNotContain("share password", json, StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    [Fact]
    public async Task CaptureAsync_PreservesOneAuthoringVersionAcrossBothRuntimeOutputs()
    {
        using var secrets = new OobeAccountSecretState();
        using BuildSnapshot snapshot = await BuildSnapshot.CaptureAsync(
            new(), secrets, [], root, "26.10.3.1", TestContext.Current.CancellationToken);
        FoundryConnectProvisioningBundle connect = snapshot.CreateConnectProvisioningBundle(Path.Combine(root, "connect"));
        using JsonDocument connectJson = JsonDocument.Parse(connect.ConfigurationJson);
        using JsonDocument deployJson = JsonDocument.Parse(snapshot.GenerateDeployConfigurationJson());
        using JsonDocument authoringJson = JsonDocument.Parse(new FoundryConfigurationService().Serialize(snapshot.Configuration));

        Assert.Equal("26.10.3.1", connect.Configuration.AuthoringVersion);
        Assert.Equal("26.10.3.1", connectJson.RootElement.GetProperty("authoringVersion").GetString());
        Assert.Equal("26.10.3.1", deployJson.RootElement.GetProperty("authoringVersion").GetString());
        Assert.False(authoringJson.RootElement.TryGetProperty("authoringVersion", out _));
    }

    [Fact]
    public async Task CaptureAsync_FreezesMutableCollectionsBeforeReturningTask()
    {
        var packages = new List<string> { "Original.Package" };
        var document = new FoundryConfigurationDocument { Customization = new() { AppxRemoval = new() { IsEnabled = true, PackageNames = packages } } };
        using var secrets = new OobeAccountSecretState();
        Task<BuildSnapshot> capture = Capture(document, secrets);
        packages[0] = "Changed.Package";
        using BuildSnapshot snapshot = await capture;
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
            Network = new() { WifiProvisioned = true, Wifi = new() { IsEnabled = true, Ssid = "Office", SecurityType = "WPA2/WPA3-Personal" } },
            Customization = new() { Oobe = new() { IsEnabled = true, EnableAdministratorAccount = true, UseAdministratorPassword = true } }
        };
        var network = new NetworkSecrets { WifiPassphrase = "OriginalWifiPassword" };
        Task<BuildSnapshot> capture = BuildSnapshot.CaptureAsync(
            document, Sources(network), secrets, null, mediaPassword, root, null, TestContext.Current.CancellationToken);
        secrets.Clear();
        Array.Clear(mediaPassword);
        using BuildSnapshot snapshot = await capture;
        using var protection = snapshot.CreateDeploymentProtectionMaterial();
        FoundryConnectProvisioningBundle connect = snapshot.CreateConnectProvisioningBundle(Path.Combine(root, "connect"));
        try
        {
            Assert.Equal("OriginalWifiPassword", MediaSecretEnvelopeProtector.DecryptString(connect.Configuration.Wifi.PassphraseSecret!, connect.MediaSecretsKey!));
            string json = snapshot.GenerateDeployConfigurationJson(deploymentSecretsKey: protection.DeploymentKey, protectionSettings: protection.Settings);
            FoundryDeployConfigurationDocument deploy = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal("AdminPassword123!", MediaSecretEnvelopeProtector.DecryptString(deploy.Customization.Oobe.AdministratorPasswordSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
            Assert.DoesNotContain("OriginalWifiPassword", new FoundryConfigurationService().Serialize(snapshot.Configuration), StringComparison.Ordinal);
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
            Unattend = new() { IsEnabled = true, Files = [new() { Id = Guid.NewGuid().ToString("N"), DisplayName = "Answer", ContentHash = Convert.ToHexString(SHA256.HashData(content)), File = Reference(answer) }] }
        };
        using var secrets = new OobeAccountSecretState();
        BuildSnapshot snapshot = await Capture(document, secrets);
        string capturedAnswer = Assert.Single(snapshot.UnattendSourcePaths).Value;
        string capturedDrivers = snapshot.Configuration.General.CustomDriverDirectoryPath!;
        File.WriteAllText(library.GetAvailablePath(document.Unattend.Files[0].File!)!, "changed");
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
            Network = new() { Dot1x = new() { IsEnabled = false, ProfileTemplate = TestFileReferences.Detached("missing.xml") } },
            Telemetry = new() { IsEnabled = true, InstallId = "captured-install" },
            General = new() { IsoOutputPath = "C:/local-output.iso" }
        };
        using BuildSnapshot snapshot = await Capture(document, secrets);
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
        AuthoringFileReference reference = Reference(path);
        var document = new FoundryConfigurationDocument { Unattend = new() { IsEnabled = true, Files = [new() { Id = Guid.NewGuid().ToString("N"), ContentHash = new string('0', 64), File = reference }] } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Capture(document, secrets));
        Assert.Empty(Directory.EnumerateDirectories(root));
    }

    [Fact]
    public async Task CaptureAsync_CanceledDuringCopy_RemovesPrivateDirectory()
    {
        string drivers = Path.Combine(root, "drivers");
        string snapshots = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(drivers);
        foreach (string name in new[] { "first.inf", "second.inf", "third.inf" }) File.WriteAllText(Path.Combine(drivers, name), name);
        using var secrets = new OobeAccountSecretState();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var document = new FoundryConfigurationDocument { General = new() { CustomDriverDirectoryPath = drivers } };
        List<int> reports = [];
        string? privateDirectory = null;
        var progress = new CallbackProgress(copied =>
        {
            reports.Add(copied);
            privateDirectory = Assert.Single(Directory.EnumerateDirectories(snapshots));
            cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildSnapshot.CaptureAsync(document, null, secrets, null, [], snapshots, null, progress, cancellation.Token));

        Assert.Equal([1], reports);
        Assert.NotNull(privateDirectory);
        Assert.False(Directory.Exists(privateDirectory));
        Assert.Empty(Directory.EnumerateDirectories(snapshots));
    }

    [Fact]
    public async Task CaptureAsync_RejectsAggregateDriverSizeBeforeOpeningAnySource()
    {
        string drivers = Path.Combine(root, "drivers");
        string snapshots = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(drivers);
        using FileStream first = new(Path.Combine(drivers, "first.sys"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using FileStream second = new(Path.Combine(drivers, "second.sys"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        first.SetLength(1_073_741_824);
        second.SetLength(1_073_741_825);
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument { General = new() { CustomDriverDirectoryPath = drivers } };

        CustomDriverSizeLimitException exception = await Assert.ThrowsAsync<CustomDriverSizeLimitException>(() => BuildSnapshot.CaptureAsync(document, secrets, [], snapshots, null, TestContext.Current.CancellationToken));

        Assert.Equal(2_147_483_649L, exception.ActualBytes);
        Assert.Equal(2_147_483_648L, exception.LimitBytes);
        Assert.DoesNotContain(drivers, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(snapshots) && Directory.EnumerateDirectories(snapshots).Any());
        Assert.Equal(1_073_741_824L, first.Length);
        Assert.Equal(1_073_741_825L, second.Length);
    }

    [Fact]
    public async Task CaptureAsync_DriverSizeAtLimitReachesSourceRead()
    {
        string drivers = Path.Combine(root, "drivers");
        string snapshots = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(drivers);
        using FileStream source = new(Path.Combine(drivers, "locked.sys"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.SetLength(2_147_483_648);
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument { General = new() { CustomDriverDirectoryPath = drivers } };

        IOException exception = await Assert.ThrowsAsync<IOException>(() => BuildSnapshot.CaptureAsync(document, secrets, [], snapshots, null, TestContext.Current.CancellationToken));

        Assert.Equal(32, exception.HResult & 0xffff);
        Assert.Empty(Directory.EnumerateDirectories(snapshots));
    }

    [Fact]
    public async Task Dispose_WhenSourceIsLocked_ClearsSecretsAndRetriesFileCleanup()
    {
        using var secrets = new OobeAccountSecretState();
        BuildSnapshot snapshot = await BuildSnapshot.CaptureAsync(new(), secrets, [], root, null, TestContext.Current.CancellationToken);
        string directory = Assert.Single(Directory.EnumerateDirectories(root));
        string path = Path.Combine(directory, "confidential.xml");
        File.WriteAllText(path, "private-source");
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(snapshot.Dispose);
            Assert.Throws<ObjectDisposedException>(() => snapshot.CreateDeploymentProtectionMaterial());
        }

        snapshot.Dispose();

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task CaptureAsync_RemovesAbandonedSourcesAndPreservesLiveSnapshots()
    {
        using var secrets = new OobeAccountSecretState();
        using BuildSnapshot active = await BuildSnapshot.CaptureAsync(new(), secrets, [], root, null, TestContext.Current.CancellationToken);
        string activeDirectory = Assert.Single(Directory.EnumerateDirectories(root));
        string activeSource = Path.Combine(activeDirectory, "active.xml");
        File.WriteAllText(activeSource, "active-source");
        string abandonedDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandonedDirectory);
        File.WriteAllText(Path.Combine(abandonedDirectory, "confidential.xml"), "abandoned-source");
        string unrelatedDirectory = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(unrelatedDirectory);
        File.WriteAllText(Path.Combine(unrelatedDirectory, "keep.xml"), "keep-source");

        using BuildSnapshot next = await BuildSnapshot.CaptureAsync(new(), secrets, [], root, null, TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(abandonedDirectory));
        Assert.Equal("active-source", File.ReadAllText(activeSource));
        Assert.True(Directory.Exists(unrelatedDirectory));
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
                    BootMediaCertificate = new() { Pfx = Reference(source), ValidatedThumbprint = "ABCDEF", ValidatedExpiresOnUtc = expiry }
                }
            }
        };
        using BuildSnapshot snapshot = await Capture(document, secrets, autopilotPfxPassword: "OriginalCertificatePassword");
        File.WriteAllBytes(library.GetAvailablePath(document.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx!)!, [9, 9, 9, 9, 9]);
        using var protection = snapshot.CreateDeploymentProtectionMaterial();
        string json = snapshot.GenerateDeployConfigurationJson(deploymentSecretsKey: protection.DeploymentKey, protectionSettings: protection.Settings);
        FoundryDeployConfigurationDocument deploy = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(original, MediaSecretEnvelopeProtector.DecryptBytes(deploy.Autopilot.HardwareHashUpload.CertificatePfxSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Equal("OriginalCertificatePassword", MediaSecretEnvelopeProtector.DecryptString(deploy.Autopilot.HardwareHashUpload.CertificatePfxPasswordSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.DoesNotContain("OriginalCertificatePassword", new FoundryConfigurationService().Serialize(snapshot.Configuration), StringComparison.Ordinal);
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
        Task<BuildSnapshot> capture = BuildSnapshot.CaptureAsync(document, secrets, "MediaPassword123!", root, null, TestContext.Current.CancellationToken);
        secrets.Clear();
        using BuildSnapshot snapshot = await capture;
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
            Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplate = TestFileReferences.Detached("missing.xml") } }
        };
        await Assert.ThrowsAnyAsync<IOException>(() => Capture(document, secrets));
        Assert.Empty(Directory.EnumerateDirectories(root));
    }

    [Theory]
    [InlineData(MaximumSourceBytes, true)]
    [InlineData(MaximumSourceBytes + 1, false)]
    public async Task CaptureAsync_BoundsEachSourceFile(long length, bool accepted)
    {
        string template = CreateSource("wired.xml", length);
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument { Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplate = Reference(template) } } };

        if (accepted)
        {
            using BuildSnapshot snapshot = await Capture(document, secrets);
            Assert.Equal(length, new FileInfo(snapshot.ResolveCapturedFile(document.Network.Dot1x.ProfileTemplate!)!).Length);
            return;
        }

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => Capture(document, secrets));
        Assert.Equal("Media source files exceed the supported size.", exception.Message);
        AssertNoPrivateDirectoryLeft();
    }

    [Fact]
    public async Task CaptureAsync_RejectsSourcesAboveTheTotalBound()
    {
        const long length = 3 * 1024 * 1024;
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument
        {
            Network = new()
            {
                Dot1x = new() { IsEnabled = true, ProfileTemplate = Reference(CreateSource("wired.xml", length)), Certificate = Reference(CreateSource("wired.pfx", length, fill: 2)), RequiresCertificate = true },
                WifiProvisioned = true,
                Wifi = new() { IsEnabled = true, Certificate = Reference(CreateSource("wifi.pfx", length, fill: 3)), RequiresCertificate = true }
            }
        };

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => Capture(document, secrets));
        Assert.Equal("Media source files exceed the supported size.", exception.Message);
        AssertNoPrivateDirectoryLeft();
    }

    [Fact]
    public async Task CaptureAsync_OptionalCertificateIsCopiedWhenSelectedAndNotRequiredOtherwise()
    {
        string template = CreateSource("wired.xml", 3);
        string certificate = CreateSource("wired.pfx", 3, fill: 2);
        using var secrets = new OobeAccountSecretState();
        var selected = new FoundryConfigurationDocument
        {
            Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplate = Reference(template), Certificate = Reference(certificate), RequiresCertificate = false } }
        };

        using (BuildSnapshot snapshot = await Capture(selected, secrets))
            Assert.Equal(File.ReadAllBytes(certificate), File.ReadAllBytes(snapshot.ResolveCapturedFile(selected.Network.Dot1x.Certificate!)!));

        FoundryConfigurationDocument unselected = selected with { Network = selected.Network with { Dot1x = selected.Network.Dot1x with { Certificate = null } } };
        using BuildSnapshot withoutCertificate = await Capture(unselected, secrets);
        Assert.Null(withoutCertificate.ResolveCapturedFile(selected.Network.Dot1x.Certificate!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing.xml")]
    public async Task CaptureAsync_ActiveEnterpriseWifiRequiresItsSelectedTemplate(string? templateName)
    {
        Directory.CreateDirectory(SourcesDirectory);
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument
        {
            Network = new()
            {
                WifiProvisioned = true,
                Wifi = new()
                {
                    IsEnabled = true,
                    HasEnterpriseProfile = true,
                    EnterpriseProfileTemplate = templateName is null ? null : TestFileReferences.Detached(templateName)
                }
            }
        };

        await Assert.ThrowsAsync<FileNotFoundException>(() => Capture(document, secrets));
    }

    [Theory]
    [InlineData("certificate.pfx", ".pfx")]
    [InlineData("certificate.P12", ".p12")]
    [InlineData("certificate.cer", ".cer")]
    [InlineData("certificate.crt", ".crt")]
    [InlineData("certificate.exe", ".bin")]
    [InlineData("certificate", ".bin")]
    public async Task CaptureAsync_NamesPrivateCopiesWithGeneratedNamesAndWhitelistedExtensions(string sourceName, string expectedExtension)
    {
        string certificate = CreateSource(sourceName, 3);
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument
        {
            Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplate = Reference(CreateSource("wired.xml", 3, fill: 2)), Certificate = Reference(certificate), RequiresCertificate = true } }
        };

        using BuildSnapshot snapshot = await Capture(document, secrets);
        string captured = snapshot.ResolveCapturedFile(document.Network.Dot1x.Certificate!)!;
        Assert.Equal(expectedExtension, Path.GetExtension(captured));
        Assert.True(Guid.TryParseExact(Path.GetFileNameWithoutExtension(captured), "N", out _));
        Assert.NotEqual(SourcesDirectory, Path.GetDirectoryName(captured));
        Assert.StartsWith(root + Path.DirectorySeparatorChar, captured);
        Assert.Equal(".xml", Path.GetExtension(snapshot.ResolveCapturedFile(document.Network.Dot1x.ProfileTemplate!)));
    }

    [Fact]
    public async Task CaptureAsync_ResolvesFileReferencesToVerifiedCopies()
    {
        AuthoringFileReference template = library.Import("<LANProfile />"u8, "wired.xml");
        AuthoringFileReference certificate = library.Import([1, 2, 3], "wired.pfx");
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument
        {
            Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplate = template, RequiresCertificate = true, Certificate = certificate } }
        };

        using BuildSnapshot snapshot = await BuildSnapshot.CaptureAsync(
            document, Sources(new NetworkSecrets { WiredCertificatePfxPassword = "pfx-password" }),
            secrets, null, [], Path.Combine(root, "snapshots"), null, TestContext.Current.CancellationToken);
        string capturedTemplate = snapshot.ResolveCapturedFile(template)!;
        string capturedCertificate = snapshot.ResolveCapturedFile(certificate)!;
        File.WriteAllBytes(library.GetAvailablePath(template)!, "<changed! />"u8.ToArray());
        FoundryConnectProvisioningBundle connect = snapshot.CreateConnectProvisioningBundle(Path.Combine(root, "connect"));

        Assert.StartsWith(Path.Combine(root, "snapshots") + Path.DirectorySeparatorChar, capturedTemplate);
        Assert.Equal("<LANProfile />"u8.ToArray(), File.ReadAllBytes(capturedTemplate));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(capturedCertificate));
        Assert.Equal("<LANProfile />"u8.ToArray(), File.ReadAllBytes(connect.AssetFiles.Single(asset => asset.RelativeDestinationPath.Contains(@"\Wired\Profiles\", StringComparison.Ordinal)).SourcePath));
        Assert.NotNull(connect.Configuration.Dot1x.CertificatePfxPasswordSecret);
        CryptographicOperations.ZeroMemory(connect.MediaSecretsKey!);
    }

    [Fact]
    public async Task CaptureAsync_ReleasesSourceFileLeaseOnceFilesAreCopied()
    {
        AuthoringFileReference template = library.Import("<LANProfile />"u8, "wired.xml");
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument { Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplate = template } } };

        using BuildSnapshot snapshot = await Capture(document, secrets);

        Assert.Equal(1, library.DeleteUnreferenced(new HashSet<string>(), library.ImportSequence).Removed);
        Assert.Equal("<LANProfile />"u8.ToArray(), File.ReadAllBytes(snapshot.ResolveCapturedFile(template)!));
    }

    [Fact]
    public async Task CaptureAsync_MissingReferencedFile_FailsWithDependencyUnavailable()
    {
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument
        {
            Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplate = TestFileReferences.Detached("wired.xml") } }
        };

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(() => BuildSnapshot.CaptureAsync(
            document, Sources(), secrets, null, [], root, null, TestContext.Current.CancellationToken));

        Assert.Equal(BuildSnapshot.DependencyUnavailableMessage, exception.Message);
        AssertNoPrivateDirectoryLeft();
    }

    [Fact]
    public async Task CaptureAsync_LibraryFileChangedOutsideFoundry_FailsVerification()
    {
        AuthoringFileReference template = library.Import("<LANProfile />"u8, "wired.xml");
        File.WriteAllBytes(library.GetAvailablePath(template)!, "<LANProfil! />"u8.ToArray());
        using var secrets = new OobeAccountSecretState();
        var document = new FoundryConfigurationDocument { Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplate = template } } };

        await Assert.ThrowsAsync<InvalidDataException>(() => BuildSnapshot.CaptureAsync(
            document, Sources(), secrets, null, [], root, null, TestContext.Current.CancellationToken));

        AssertNoPrivateDirectoryLeft();
    }

    private const int MaximumSourceBytes = 4 * 1024 * 1024;

    private string SourcesDirectory => Path.Combine(root, "sources");

    /// <summary>Imports a source into the authoring file library, as a page does, and returns its reference.</summary>
    /// <remarks>A source above the library limit is written straight into the library layout, as a file changed outside Foundry would be.</remarks>
    private AuthoringFileReference Reference(string path)
    {
        byte[] content = File.ReadAllBytes(path);
        if (content.Length <= AuthoringFileLibrary.MaximumFileBytes) return library.Import(content, Path.GetFileName(path));

        AuthoringFileReference reference = TestFileReferences.For(path);
        string stored = Path.Combine(libraryRoot, "content", reference.ContentHash, reference.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
        File.Copy(path, stored);
        return reference;
    }

    private BuildSnapshotSources Sources(NetworkSecrets? network = null, string? autopilotPfxPassword = null) =>
        new(library) { Network = network ?? new NetworkSecrets(), AutopilotPfxPassword = autopilotPfxPassword };

    private Task<BuildSnapshot> Capture(FoundryConfigurationDocument document, OobeAccountSecretState secrets, string? autopilotPfxPassword = null) =>
        BuildSnapshot.CaptureAsync(document, Sources(autopilotPfxPassword: autopilotPfxPassword), secrets, null, [], root, null, TestContext.Current.CancellationToken);

    private void AssertNoPrivateDirectoryLeft() =>
        Assert.DoesNotContain(Directory.EnumerateDirectories(root), directory => Guid.TryParseExact(Path.GetFileName(directory), "N", out _));

    // The fill byte keeps sources of equal length distinct, because a snapshot copies identical content once.
    private string CreateSource(string name, long length, byte fill = 1)
    {
        Directory.CreateDirectory(SourcesDirectory);
        string path = Path.Combine(SourcesDirectory, name);
        using FileStream stream = File.Create(path);
        stream.SetLength(length);
        if (length > 0) stream.WriteByte(fill);
        return path;
    }

    public void Dispose()
    {
        foreach (string directory in new[] { root, libraryRoot })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class CallbackProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
