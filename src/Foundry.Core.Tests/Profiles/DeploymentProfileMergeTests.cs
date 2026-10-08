// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed class DeploymentProfileMergeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MergeKeepsAnOmittedSecretForEachAccountStillStored(bool includeSecrets)
    {
        DeploymentProfileDocument baseline = TwoAccountProfile();
        baseline = baseline with { Secrets = new() { Entries = [Secret(baseline, "CORP\\join", ProfileValueState.Omitted), Secret(baseline, "EMEA\\join", ProfileValueState.Omitted)] } };
        var captured = baseline with { Secrets = new() { Entries = [baseline.Secrets.Entries[0] with { State = ProfileValueState.Unavailable }] } };

        var result = DeploymentProfileMerge.PreserveOmittedSourceMetadata(captured, baseline, baseline.Configuration, includeSecrets);

        Assert.Equal(2, result.Secrets.Entries.Count);
        Assert.All(result.Secrets.Entries, secret =>
        {
            Assert.Equal(ProfileValueState.Omitted, secret.State);
            Assert.Null(secret.Value);
        });
        Assert.Equal(baseline.Secrets.Entries.Select(secret => secret.Identity).Order(), result.Secrets.Entries.Select(secret => secret.Identity).Order());
    }

    [Fact]
    public void MergeDropsTheSecretOfAnAccountNoLongerStored()
    {
        DeploymentProfileDocument baseline = TwoAccountProfile();
        baseline = baseline with { Secrets = new() { Entries = [Secret(baseline, "CORP\\join", ProfileValueState.Omitted), Secret(baseline, "EMEA\\join", ProfileValueState.Omitted)] } };
        DomainJoinSettings settings = baseline.Configuration.DomainJoin;
        // The second domain falls back to the shared account, so EMEA\join is referenced by nothing.
        var captured = baseline with
        {
            Secrets = new(),
            Configuration = baseline.Configuration with { DomainJoin = settings with { Domains = [settings.Domains[0], settings.Domains[1] with { AccountName = null }] } }
        };

        var result = DeploymentProfileMerge.PreserveOmittedSourceMetadata(captured, baseline, baseline.Configuration, includeSecrets: false);

        DeploymentProfileSecret kept = Assert.Single(result.Secrets.Entries);
        Assert.Equal(baseline.Secrets.Entries[0].Identity, kept.Identity);
    }

    [Fact]
    public void MergeKeepsOmittedSecretsWhileTheAccountsAreStoredInInteractiveMode()
    {
        DeploymentProfileDocument baseline = TwoAccountProfile();
        baseline = baseline with
        {
            Configuration = baseline.Configuration with { DomainJoin = baseline.Configuration.DomainJoin with { Mode = DomainJoinMode.Interactive } }
        };
        baseline = baseline with { Secrets = new() { Entries = [Secret(baseline, "CORP\\join", ProfileValueState.Omitted), Secret(baseline, "EMEA\\join", ProfileValueState.Omitted)] } };

        var result = DeploymentProfileMerge.PreserveOmittedSourceMetadata(baseline with { Secrets = new() }, baseline, baseline.Configuration, includeSecrets: false);

        Assert.Equal(2, result.Secrets.Entries.Count);
    }

    [Fact]
    public void LocalValueIsPreservedOnlyForTheSameAccount()
    {
        DeploymentProfileDocument local = TwoAccountProfile();
        local = local with
        {
            Secrets = new()
            {
                Entries = [Secret(local, "CORP\\join", ProfileValueState.Present, [112, 97, 115, 115]), Secret(local, "EMEA\\join", ProfileValueState.Present, [101, 109, 101, 97])]
            }
        };
        DeploymentProfileDocument incoming = local with { Secrets = new() { Entries = [local.Secrets.Entries[0] with { State = ProfileValueState.Omitted, Value = null }] } };

        var retained = DeploymentProfileMerge.PreserveOmittedLocalValues(incoming, local);

        DeploymentProfileSecret secret = Assert.Single(retained.Secrets.Entries);
        Assert.Equal(new byte[] { 112, 97, 115, 115 }, secret.Value);
        Assert.NotSame(local.Secrets.Entries[0].Value, secret.Value);
        // Switching Domain Join off or to Interactive keeps the account, so its local password stays with it.
        var unused = incoming with { Configuration = incoming.Configuration with { DomainJoin = incoming.Configuration.DomainJoin with { IsEnabled = false, Mode = DomainJoinMode.Interactive } } };
        Assert.Equal(new byte[] { 112, 97, 115, 115 }, DeploymentProfileMerge.PreserveOmittedLocalValues(unused, local).Secrets.Entries[0].Value);
        // Even a stale imported identity must not carry a password to an account the incoming profile no longer uses.
        var changed = incoming with { Configuration = incoming.Configuration with { DomainJoin = incoming.Configuration.DomainJoin with { SharedAccountName = "CORP\\other" } } };
        Assert.Null(DeploymentProfileMerge.PreserveOmittedLocalValues(changed, local).Secrets.Entries[0].Value);
    }

    [Fact]
    public void RenamingADomainKeepsItsAccountSecret()
    {
        DeploymentProfileDocument profile = TwoAccountProfile();
        DomainJoinSettings settings = profile.Configuration.DomainJoin;
        DeploymentProfileDocument renamed = profile with
        {
            Configuration = profile.Configuration with { DomainJoin = settings with { Domains = [settings.Domains[0] with { DomainName = "renamed.test" }, settings.Domains[1]] } }
        };
        Assert.Equal(DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.DomainJoinPassword, profile, "CORP\\join"),
            DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.DomainJoinPassword, renamed, "corp\\JOIN"));
    }

    private static DeploymentProfileDocument TwoAccountProfile() => new()
    {
        ProfileId = Guid.NewGuid(),
        Configuration = new()
        {
            DomainJoin = new()
            {
                IsEnabled = true,
                Mode = DomainJoinMode.Automatic,
                SharedAccountName = "CORP\\join",
                DefaultDomainId = "corp",
                Domains = [new() { Id = "corp", DomainName = "corp.test" }, new() { Id = "emea", DomainName = "emea.test", AccountName = "EMEA\\join" }]
            }
        }
    };

    private static DeploymentProfileSecret Secret(DeploymentProfileDocument profile, string account, ProfileValueState state, byte[]? value = null) => new()
    {
        Purpose = ProfileSecretPurpose.DomainJoinPassword,
        Identity = DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.DomainJoinPassword, profile, account),
        State = state,
        Value = value
    };

    [Fact]
    public void Merge_SettingsOnlyRoundTripRetainsAnotherPcsCertificate()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Foundry.ProfileMerge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        DeploymentProfileDocument? local = null;
        DeploymentProfileDocument? result = null;
        try
        {
            string certificate = Path.Combine(directory, "certificate.pfx");
            File.WriteAllBytes(certificate, [1, 2, 3]);
            FoundryConfigurationDocument configuration = new()
            {
                Network = new() { Dot1x = new() { IsEnabled = true, RequiresCertificate = true, CertificatePath = certificate } }
            };
            local = new()
            {
                ProfileId = Guid.NewGuid(),
                Configuration = configuration,
                Assets = DeploymentProfileAssetService.Capture(configuration, true)
            };
            local = local with
            {
                Secrets = new()
                {
                    Entries = [new()
                {
                    Purpose = ProfileSecretPurpose.WiredCertificatePassword,
                    Identity = DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.WiredCertificatePassword, local),
                    State = ProfileValueState.Present,
                    Value = [4, 5, 6]
                }]
                }
            };
            DeploymentProfileDocument shared = local with
            {
                Assets = DeploymentProfileAssetService.Capture(configuration, false),
                Secrets = new() { Entries = [local.Secrets.Entries[0] with { State = ProfileValueState.Omitted, Value = null }] }
            };
            FoundryConfigurationDocument joined = DeploymentProfileAssetService.Materialize(shared, Path.Combine(directory, "joined"));
            DeploymentProfileDocument recaptured = shared with
            {
                Configuration = joined,
                Assets = DeploymentProfileAssetService.Capture(joined, false),
                Secrets = new()
            };
            recaptured = DeploymentProfileMerge.PreserveOmittedSourceMetadata(recaptured, shared, joined, includeSecrets: false);

            result = DeploymentProfileMerge.PreserveOmittedLocalValues(recaptured, local);

            DeploymentProfileAsset retained = Assert.Single(result.Assets, asset => asset.Kind == ProfileAssetKind.WiredCertificate);
            Assert.Equal(ProfileValueState.Present, retained.State);
            Assert.Equal(new byte[] { 1, 2, 3 }, retained.Content);
            Assert.Equal(new byte[] { 4, 5, 6 }, Assert.Single(result.Secrets.Entries).Value);
        }
        finally
        {
            if (local is not null) DeploymentProfileSecretBinding.Clear(local);
            if (result is not null) DeploymentProfileSecretBinding.Clear(result);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_MissingSourceMetadataNeverRestoresConfidentialBytes(bool includeSecrets)
    {
        DeploymentProfileDocument baseline = MissingCertificateBaseline();
        DeploymentProfileDocument captured = baseline with { Assets = [], Secrets = new() };

        DeploymentProfileDocument result = DeploymentProfileMerge.PreserveOmittedSourceMetadata(captured, baseline,
            baseline.Configuration, includeSecrets);

        DeploymentProfileAsset asset = Assert.Single(result.Assets);
        Assert.Equal(baseline.Assets[0].Sha256, asset.Sha256);
        Assert.Null(asset.Content);
        Assert.Equal(includeSecrets ? ProfileValueState.Unavailable : ProfileValueState.Omitted, asset.State);
        Assert.Null(Assert.Single(result.Secrets.Entries).Value);
        Assert.Equal(asset.State, result.Secrets.Entries[0].State);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("certificate-removed")]
    [InlineData("certificate-replaced")]
    [InlineData("profile-replaced")]
    public void Capture_ChangedSourceSelectionDoesNotRevivePreviousMetadata(string change)
    {
        DeploymentProfileDocument baseline = MissingCertificateBaseline();
        Dot1xSettings settings = change switch
        {
            "disabled" => baseline.Configuration.Network.Dot1x with { IsEnabled = false },
            "certificate-removed" => baseline.Configuration.Network.Dot1x with { RequiresCertificate = false },
            "certificate-replaced" => baseline.Configuration.Network.Dot1x with { CertificatePath = "new.pfx" },
            _ => baseline.Configuration.Network.Dot1x with { ProfileTemplatePath = "new.xml" }
        };
        DeploymentProfileDocument captured = baseline with
        {
            Configuration = baseline.Configuration with { Network = baseline.Configuration.Network with { Dot1x = settings } },
            Assets = [],
            Secrets = new()
        };

        DeploymentProfileDocument result = DeploymentProfileMerge.PreserveOmittedSourceMetadata(captured, baseline,
            baseline.Configuration, includeSecrets: false);

        Assert.Empty(result.Assets);
        Assert.Empty(result.Secrets.Entries);
    }

    [Fact]
    public void Capture_UnrelatedSettingsChangeAndCurrentSourceDigestArePreserved()
    {
        DeploymentProfileDocument baseline = MissingCertificateBaseline();
        DeploymentProfileDocument captured = baseline with
        {
            Configuration = baseline.Configuration with
            {
                Network = baseline.Configuration.Network with
                {
                    Dot1x = baseline.Configuration.Network.Dot1x with { AllowRuntimeCredentials = true }
                }
            },
            Assets = [],
            Secrets = new()
        };
        DeploymentProfileDocument restored = DeploymentProfileMerge.PreserveOmittedSourceMetadata(captured, baseline,
            baseline.Configuration, includeSecrets: false);
        Assert.Equal(baseline.Assets[0].Sha256, Assert.Single(restored.Assets).Sha256);

        captured = captured with { Assets = [baseline.Assets[0] with { Sha256 = new string('B', 64) }] };
        DeploymentProfileDocument current = DeploymentProfileMerge.PreserveOmittedSourceMetadata(captured, baseline,
            baseline.Configuration, includeSecrets: false);
        Assert.Equal(new string('B', 64), Assert.Single(current.Assets).Sha256);
    }

    private static DeploymentProfileDocument MissingCertificateBaseline()
    {
        DeploymentProfileDocument profile = new()
        {
            ProfileId = Guid.NewGuid(),
            Configuration = new() { Network = new() { Dot1x = new() { IsEnabled = true, RequiresCertificate = true } } },
            Assets = [new()
            {
                Id = "wired-certificate",
                Kind = ProfileAssetKind.WiredCertificate,
                RelativePath = "assets/certificate.pfx",
                State = ProfileValueState.Omitted,
                Sha256 = new string('A', 64)
            }]
        };
        return profile with
        {
            Secrets = new()
            {
                Entries = [new()
            {
                Purpose = ProfileSecretPurpose.WiredCertificatePassword,
                Identity = DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.WiredCertificatePassword, profile),
                State = ProfileValueState.Omitted
            }]
            }
        };
    }

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
