// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Profiles;
using Foundry.Core.Tests.TestUtilities;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Configuration.Legacy;

/// <summary>
/// Writes the committed legacy profile fixtures with the current profile code. Run it once, explicitly, before that code
/// is deleted; regular test runs only read the committed files.
/// </summary>
public sealed class LegacyFixtureGenerator
{
    private static readonly LegacyFixtureValues Values = new()
    {
        ExportPassphrase = "fixture-export-passphrase-not-a-secret",
        ConnectionPassphrase = "fixture-connection-passphrase-not-a-secret",
        WifiSsid = "FoundryFixtureWifi",
        WifiPassphrase = "fake-wifi-passphrase",
        DeploymentPassword = "fake-deployment-password",
        AdministratorPassword = "fake-administrator-password",
        OobeAccountId = "fixture-account-1",
        OobeAccountPassword = "fake-account-password",
        WiredCertificatePassword = "fake-wired-certificate-password",
        WiredCertificateBase64 = Convert.ToBase64String(Encoding.ASCII.GetBytes("FOUNDRY-FIXTURE-FAKE-PFX-NOT-A-CERTIFICATE")),
        DomainAccount = DomainJoinCredentialContext.CanonicalizeAccountName("join-svc@corp.fixture.test"),
        DomainPassword = "fake-domain-join-password",
        UnattendId = "0f1e2d3c4b5a69788796a5b4c3d2e1f0",
        DriverSetId = "a1b2c3d4e5f60718293a4b5c6d7e8f90",
        ConnectionKeyBase64 = Convert.ToBase64String(Enumerable.Repeat((byte)0x5A, 32).ToArray()),
        ActiveLocalId = new Guid("11111111-1111-4111-8111-111111111111"),
        ActiveDisplayName = "Fixture active profile",
        SharedLocalId = new Guid("22222222-2222-4222-8222-222222222222"),
        SharedDisplayName = "Fixture shared profile"
    };

    private static readonly Guid ExportProfileId = new("33333333-3333-4333-8333-333333333333");
    private static readonly Guid ActiveProfileId = new("44444444-4444-4444-8444-444444444444");
    private static readonly Guid SharedProfileId = new("55555555-5555-4555-8555-555555555555");
    private static readonly Guid RepositoryId = new("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KnownRevisionId = new("77777777-7777-4777-8777-777777777777");
    private const string SharedRootPath = @"\\fixture-server.invalid\FoundryShare";

    [Fact(Explicit = true)]
    public void Generate()
    {
        string fixtures = SourceFixtureDirectory();
        Directory.CreateDirectory(fixtures);
        using var sources = new TemporaryDirectory();
        FoundryConfigurationDocument configuration = CreateConfiguration(sources.Path);
        var packages = new DeploymentProfilePackageService();

        File.WriteAllBytes(Path.Combine(fixtures, LegacyFixtures.ExportWithSecrets),
            packages.Export(Capture(configuration, ExportProfileId, "Fixture export", includeSecrets: true), Values.ExportPassphrase));
        File.WriteAllBytes(Path.Combine(fixtures, LegacyFixtures.ExportWithoutSecrets),
            packages.Export(Capture(configuration, ExportProfileId, "Fixture export", includeSecrets: false), Values.ExportPassphrase));
        File.WriteAllBytes(Path.Combine(fixtures, LegacyFixtures.Connection), packages.Export(CreateConnection(configuration), Values.ConnectionPassphrase));
        WriteLocalStore(fixtures, configuration, packages);
        File.WriteAllBytes(Path.Combine(fixtures, LegacyFixtures.Values), JsonSerializer.SerializeToUtf8Bytes(Values, LegacyFixtures.JsonOptions));
    }

    private static void WriteLocalStore(string fixtures, FoundryConfigurationDocument configuration, DeploymentProfilePackageService packages)
    {
        using var store = new TemporaryDirectory();
        var credentials = new FakeCredentialStore();
        var repository = new LocalDeploymentProfileRepository(store.Path, credentials, packages);
        repository.Save(Values.ActiveLocalId, Capture(configuration, ActiveProfileId, Values.ActiveDisplayName, includeSecrets: true),
            rememberSecrets: true, expectedRevision: null);
        var enrollment = new LocalProfileEnrollment
        {
            RootPath = SharedRootPath,
            RepositoryId = RepositoryId,
            ProfileId = SharedProfileId,
            KeyEpoch = 1,
            KnownRevisionId = KnownRevisionId,
            IsEnabled = true,
            RememberSharedKey = true,
            IncludeSecrets = true
        };
        var shared = new DeploymentProfileDocument
        {
            ProfileId = SharedProfileId,
            DisplayName = Values.SharedDisplayName,
            Configuration = new() { Telemetry = FakeTelemetry }
        };
        repository.Save(Values.SharedLocalId, shared, rememberSecrets: false, expectedRevision: null, enrollment, Convert.FromBase64String(Values.ConnectionKeyBase64));
        repository.SetActive(Values.ActiveLocalId);

        string destination = Path.Combine(fixtures, LegacyFixtures.LocalStore);
        if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        CopyDirectory(Path.Combine(store.Path, "profiles"), Path.Combine(destination, "profiles"));
        File.Copy(Path.Combine(store.Path, "active.json"), Path.Combine(destination, "active.json"));

        string prefix = FakeCredentialStore.TargetPrefix(store.Path);
        var dump = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string target, byte[] key) in credentials.Values)
        {
            Assert.StartsWith(prefix, target, StringComparison.Ordinal);
            dump[target[prefix.Length..]] = Convert.ToBase64String(key);
        }
        File.WriteAllBytes(Path.Combine(fixtures, LegacyFixtures.LocalStoreCredentials), JsonSerializer.SerializeToUtf8Bytes(dump, LegacyFixtures.JsonOptions));
    }

    /// <summary>Mirrors the coordinator's connection payload: a SharedProfileKey secret holding the invitation JSON.</summary>
    private static DeploymentProfileDocument CreateConnection(FoundryConfigurationDocument configuration)
    {
        DeploymentProfileDocument profile = Capture(configuration, SharedProfileId, "Fixture connection", includeSecrets: true);
        var invitation = new SharedInvitation(1, RepositoryId, SharedProfileId, 1, true, Values.ConnectionKeyBase64, SharedRootPath);
        var access = new DeploymentProfileSecret
        {
            Purpose = ProfileSecretPurpose.SharedProfileKey,
            Identity = "shared-enrollment",
            State = ProfileValueState.Present,
            Value = JsonSerializer.SerializeToUtf8Bytes(invitation)
        };
        return profile with { Secrets = new DeploymentProfileSecrets { Entries = profile.Secrets.Entries.Append(access).ToArray() } };
    }

    /// <summary>Mirrors DeploymentProfileSessionService.CaptureAsync for the secrets this configuration enables.</summary>
    private static DeploymentProfileDocument Capture(FoundryConfigurationDocument configuration, Guid profileId, string displayName, bool includeSecrets)
    {
        DeploymentProfileDocument profile = new()
        {
            ProfileId = profileId,
            DisplayName = displayName,
            Configuration = WithFakeHostPaths(configuration),
            Assets = DeploymentProfileAssetService.Capture(configuration, includeSecrets)
        };
        (ProfileSecretPurpose Purpose, string? AccountId, string Value)[] secrets =
        [
            (ProfileSecretPurpose.DeploymentPassword, null, Values.DeploymentPassword),
            (ProfileSecretPurpose.WifiPassphrase, null, Values.WifiPassphrase),
            (ProfileSecretPurpose.DomainJoinPassword, Values.DomainAccount, Values.DomainPassword),
            (ProfileSecretPurpose.AdministratorPassword, null, Values.AdministratorPassword),
            (ProfileSecretPurpose.AdditionalAccountPassword, Values.OobeAccountId, Values.OobeAccountPassword),
            (ProfileSecretPurpose.WiredCertificatePassword, null, Values.WiredCertificatePassword)
        ];
        return profile with
        {
            Secrets = new DeploymentProfileSecrets
            {
                Entries = secrets.Select(secret => new DeploymentProfileSecret
                {
                    Purpose = secret.Purpose,
                    Identity = DeploymentProfileSecretBinding.Identity(secret.Purpose, profile, secret.AccountId),
                    State = includeSecrets ? ProfileValueState.Present : ProfileValueState.Omitted,
                    Value = includeSecrets ? Encoding.UTF8.GetBytes(secret.Value) : null
                }).ToArray()
            }
        };
    }

    private static FoundryConfigurationDocument CreateConfiguration(string sourceDirectory)
    {
        string wiredProfile = Path.Combine(sourceDirectory, "wired-profile.xml");
        string wiredCertificate = Path.Combine(sourceDirectory, "wired-certificate.pfx");
        string unattend = Path.Combine(sourceDirectory, "unattend.xml");
        File.WriteAllText(wiredProfile, "<LANProfile xmlns=\"http://www.microsoft.com/networking/LAN/profile/v1\"><!-- fixture --></LANProfile>");
        File.WriteAllBytes(wiredCertificate, Convert.FromBase64String(Values.WiredCertificateBase64));
        File.WriteAllText(unattend, "<?xml version=\"1.0\" encoding=\"utf-8\"?><unattend xmlns=\"urn:schemas-microsoft-com:unattend\"><!-- fixture --></unattend>");

        return new FoundryConfigurationDocument
        {
            General = new() { IsoOutputPath = @"C:\FoundryFixture\Output", DeploymentProtection = new() { IsEnabled = true } },
            Network = new()
            {
                WifiProvisioned = true,
                Wifi = new() { IsEnabled = true, Ssid = Values.WifiSsid, SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal },
                Dot1x = new() { IsEnabled = true, RequiresCertificate = true, ProfileTemplatePath = wiredProfile, CertificatePath = wiredCertificate }
            },
            Customization = new()
            {
                Oobe = new()
                {
                    IsEnabled = true,
                    EnableAdministratorAccount = true,
                    UseAdministratorPassword = true,
                    AdditionalAccounts = [new() { Id = Values.OobeAccountId, UserName = "fixtureuser", Type = OobeAccountType.Standard, UsePassword = true }]
                }
            },
            DomainJoin = new()
            {
                IsEnabled = true,
                Mode = DomainJoinMode.Automatic,
                SharedAccountName = "join-svc@corp.fixture.test",
                Domains = [new() { Id = "fixture-domain", DomainName = "corp.fixture.test" }],
                DefaultDomainId = "fixture-domain"
            },
            Unattend = new()
            {
                IsEnabled = true,
                DefaultFileId = Values.UnattendId,
                Files = [new() { Id = Values.UnattendId, DisplayName = "Fixture answer file", SourcePath = unattend, ContentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(unattend))) }]
            },
            CustomDrivers = new()
            {
                IsEnabled = true,
                Sets = [new() { Id = Values.DriverSetId, DisplayName = "Fixture driver set", Package = new() { ContentHash = new string('a', 64), DisplayName = "Fixture drivers", Length = 1024, FileCount = 2 } }],
                DefaultSetId = Values.DriverSetId
            },
            Telemetry = FakeTelemetry
        };
    }

    /// <summary>Local revisions keep host paths; the committed fixtures carry fake ones instead of the generation machine's.</summary>
    private static FoundryConfigurationDocument WithFakeHostPaths(FoundryConfigurationDocument configuration) => configuration with
    {
        Network = configuration.Network with
        {
            Dot1x = configuration.Network.Dot1x with
            {
                ProfileTemplatePath = @"C:\FoundryFixture\wired-profile.xml",
                CertificatePath = @"C:\FoundryFixture\wired-certificate.pfx"
            }
        },
        Unattend = configuration.Unattend with
        {
            Files = configuration.Unattend.Files.Select(file => file with { SourcePath = @"C:\FoundryFixture\unattend.xml" }).ToArray()
        }
    };

    private static TelemetrySettings FakeTelemetry => new()
    {
        IsEnabled = false,
        IsRemoteDiagnosticsEnabled = false,
        InstallId = "fixture-install-id",
        HostUrl = "https://telemetry.fixture.invalid",
        ProjectToken = "fake-project-token"
    };

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string SourceFixtureDirectory([CallerFilePath] string sourceFile = "") => Path.Combine(Path.GetDirectoryName(sourceFile)!, "Fixtures");

    /// <summary>Test-only copy of the WinUI coordinator's private invitation record; member names are verbatim.</summary>
    private sealed record SharedInvitation(int FormatVersion, Guid RepositoryId, Guid ProfileId, int KeyEpoch, bool IncludeSecrets, string Key, string? RootPath = null);
}
