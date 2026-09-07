// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Download;
using Foundry.Utilities.Networking;

namespace Foundry.Deploy.Tests;

public sealed class ArtifactCatalogRevisionTests
{
    [Theory]
    [InlineData("legacy-revision", true)]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("sha256:short", false)]
    [InlineData("sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("other:value", false)]
    public void ValidateIdentity_AcceptsOnlyCanonicalContentAddressedRevision(string revision, bool valid)
    {
        var identity = new ArtifactIdentity(revision, "source", new("https://example.test/image.wim"), "image.wim", new FileIntegrity(null, 1), "OperatingSystemImage", null);
        if (valid) ArtifactIntegrityPolicy.ValidateIdentity(identity);
        else Assert.Throws<ArgumentException>(() => ArtifactIntegrityPolicy.ValidateIdentity(identity));
        Assert.Throws<ArgumentException>(() => ArtifactIntegrityPolicy.ValidateIdentity(identity with { CatalogRevision = "legacy", Kind = "sha256:" + new string('a', 64) }));
        Assert.Throws<ArgumentException>(() => ArtifactIntegrityPolicy.ValidateIdentity(identity with { CatalogRevision = "legacy", SourceId = "source\ncontrol" }));
    }
}
