// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed class DeploymentProfileMergeTests
{
    [Theory]
    [InlineData(ProfileValueState.Omitted, true)]
    [InlineData(ProfileValueState.Unavailable, false)]
    [InlineData(ProfileValueState.Deleted, false)]
    [InlineData(ProfileValueState.Blank, false)]
    public void Merge_OnlyOmittedMatchingContextRetainsLocalPassword(ProfileValueState state, bool retains)
    {
        Guid id = Guid.NewGuid();
        var local = new DeploymentProfileDocument
        {
            ProfileId = id,
            Secrets = new() { Entries = [new() { Purpose = ProfileSecretPurpose.WifiPassphrase, Identity = "network-context", State = ProfileValueState.Present, Value = [1, 2, 3] }] }
        };
        var incoming = new DeploymentProfileDocument
        {
            ProfileId = id,
            Secrets = new() { Entries = [local.Secrets.Entries[0] with { State = state, Value = null }] }
        };
        DeploymentProfileDocument result = DeploymentProfileMerge.PreserveOmittedLocalValues(incoming, local);
        Assert.Equal(retains ? ProfileValueState.Present : state, result.Secrets.Entries[0].State);
        if (retains)
        {
            Assert.Equal(local.Secrets.Entries[0].Value, result.Secrets.Entries[0].Value);
            DeploymentProfileSecretBinding.Clear(result);
            Assert.Equal(new byte[] { 1, 2, 3 }, local.Secrets.Entries[0].Value);
        }
        else Assert.Null(result.Secrets.Entries[0].Value);
    }

    [Fact]
    public void Merge_ChangedIdentityAndUnverifiedAssetsDoNotReuseLocalMaterial()
    {
        Guid id = Guid.NewGuid();
        var local = new DeploymentProfileDocument
        {
            ProfileId = id,
            Secrets = new() { Entries = [new() { Purpose = ProfileSecretPurpose.WifiPassphrase, Identity = "old-network", State = ProfileValueState.Present, Value = [1] }] },
            Assets = [new() { Id = "certificate", Kind = ProfileAssetKind.WiredCertificate, State = ProfileValueState.Present, Sha256 = new string('A', 64), Content = [1] }]
        };
        var incoming = new DeploymentProfileDocument
        {
            ProfileId = id,
            Secrets = new() { Entries = [local.Secrets.Entries[0] with { Identity = "new-network", State = ProfileValueState.Omitted, Value = null }] },
            Assets = [local.Assets[0] with { Sha256 = null, State = ProfileValueState.Omitted, Content = null }]
        };
        DeploymentProfileDocument result = DeploymentProfileMerge.PreserveOmittedLocalValues(incoming, local);
        Assert.Null(result.Secrets.Entries[0].Value);
        Assert.Null(result.Assets[0].Content);
    }
}
