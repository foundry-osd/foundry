// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class FoundryConfigurationMigrationTests
{
    [Theory]
    [InlineData("client.pfx", true)]
    [InlineData("client.P12", true)]
    [InlineData("root.cer", false)]
    [InlineData("root.crt", false)]
    [InlineData("chain.p7b", false)]
    public void ApplySchemaMigrations_LegacyCertificate_MovesByShape(string fileName, bool isClient)
    {
        AuthoringFileReference reference = TestFileReferences.Detached(fileName);
        string legacy = JsonSerializer.Serialize(reference, ConfigurationJsonDefaults.SerializerOptions);
        string json = $$"""{"schemaVersion":19,"network":{"dot1x":{"isEnabled":true,"certificate":{{legacy}}},"wifi":{"isEnabled":true,"certificate":{{legacy}} } } }""";

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplySchemaMigrations(
            JsonSerializer.Deserialize<FoundryConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions)!);

        foreach ((AuthoringFileReference? client, IReadOnlyList<AuthoringFileReference> roots, AuthoringFileReference? left) in new[]
        {
            (migrated.Network.Dot1x.ClientCertificate, migrated.Network.Dot1x.TrustedRootCertificates, migrated.Network.Dot1x.LegacyCertificate),
            (migrated.Network.Wifi.ClientCertificate, migrated.Network.Wifi.TrustedRootCertificates, migrated.Network.Wifi.LegacyCertificate)
        })
        {
            Assert.Null(left);
            Assert.Equal(isClient ? reference : null, client);
            Assert.Equal(isClient ? Array.Empty<AuthoringFileReference>() : new[] { reference }, roots);
        }

        string saved = JsonSerializer.Serialize(migrated, ConfigurationJsonDefaults.SerializerOptions);
        Assert.DoesNotContain("\"certificate\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"trustedRootCertificates\"", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplySchemaMigrations_LegacyPfx_KeepsItsVaultPassword()
    {
        AuthoringFileReference pfx = TestFileReferences.Detached("client.pfx");
        string legacy = JsonSerializer.Serialize(pfx, ConfigurationJsonDefaults.SerializerOptions);
        string json = $$"""{"schemaVersion":19,"network":{"dot1x":{"isEnabled":true,"requiresCertificate":true,"certificate":{{legacy}} } } }""";
        var vault = new Dictionary<string, string> { [SecretIds.Dot1xCertificate(pfx.ContentHash)] = "pfx-password" };

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplySchemaMigrations(
            JsonSerializer.Deserialize<FoundryConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions)!);

        Assert.Equal("pfx-password", SecretMap.FromEntries(migrated, vault).Dot1xCertificatePassword);
        Assert.Equal([SecretIds.Dot1xCertificate(pfx.ContentHash)], SecretMap.RequiredIds(migrated));
    }

    [Fact]
    public void ApplySchemaMigrations_KeepsCurrentShapeUnchanged()
    {
        var document = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings { ClientCertificate = TestFileReferences.Detached("a.pfx"), TrustedRootCertificates = [TestFileReferences.Detached("r.cer")] }
            }
        };

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplySchemaMigrations(document);

        Assert.Equal(document.Network.Dot1x.ClientCertificate, migrated.Network.Dot1x.ClientCertificate);
        Assert.Equal(document.Network.Dot1x.TrustedRootCertificates, migrated.Network.Dot1x.TrustedRootCertificates);
    }

    [Fact]
    public void WithCertificate_DoesNotDuplicateARoot()
    {
        AuthoringFileReference root = TestFileReferences.Detached("r.cer");
        Dot1xSettings settings = NetworkCertificateShapes.WithCertificate(new Dot1xSettings { TrustedRootCertificates = [root] }, root);

        Assert.Single(settings.TrustedRootCertificates);
    }
}
