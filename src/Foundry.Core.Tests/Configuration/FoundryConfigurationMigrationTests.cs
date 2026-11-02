// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class FoundryConfigurationMigrationTests
{
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
    public void ApplySchemaMigrations_NullTrustedRootList_ReadsAsEmpty()
    {
        const string json = """{"schemaVersion":19,"network":{"dot1x":{"isEnabled":true,"trustedRootCertificates":null},"wifi":{"trustedRootCertificates":null}}}""";

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplySchemaMigrations(
            JsonSerializer.Deserialize<FoundryConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions)!);

        Assert.Empty(migrated.Network.Dot1x.TrustedRootCertificates);
        Assert.Empty(migrated.Network.Wifi.TrustedRootCertificates);
    }

    [Fact]
    public void ApplySchemaMigrations_NullTrustedRootItems_AreDropped()
    {
        AuthoringFileReference root = TestFileReferences.Detached("root.cer");
        string item = JsonSerializer.Serialize(root, ConfigurationJsonDefaults.SerializerOptions);
        string json = $$"""{"schemaVersion":19,"network":{"dot1x":{"isEnabled":true,"trustedRootCertificates":[null,{{item}},null]} } }""";

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplySchemaMigrations(
            JsonSerializer.Deserialize<FoundryConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions)!);

        Assert.Equal([root], migrated.Network.Dot1x.TrustedRootCertificates);
    }

    [Fact]
    public void WithCertificate_DoesNotDuplicateARoot()
    {
        AuthoringFileReference root = TestFileReferences.Detached("r.cer");
        Dot1xSettings settings = NetworkCertificateShapes.WithCertificate(new Dot1xSettings { TrustedRootCertificates = [root] }, root);

        Assert.Single(settings.TrustedRootCertificates);
    }
}
