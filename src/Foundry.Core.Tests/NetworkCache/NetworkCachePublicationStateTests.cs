// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCachePublicationStateTests
{
    [Fact]
    public void Fingerprint_IsStableAcrossCalls()
    {
        string fingerprint = NetworkCachePublicationState.Fingerprint(Document());

        Assert.Equal(fingerprint, NetworkCachePublicationState.Fingerprint(Document()));
        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }

    [Fact]
    public void Fingerprint_ChangesWhenAnImageIsAdded()
    {
        FoundryConfigurationDocument document = Document();
        CustomImageReference extra = new() { Id = "img2", ContentHash = "bb", DisplayName = "Second" };

        FoundryConfigurationDocument changed = document with
        {
            CustomImages = document.CustomImages with { Images = [.. document.CustomImages.Images, extra] }
        };

        Assert.NotEqual(NetworkCachePublicationState.Fingerprint(document), NetworkCachePublicationState.Fingerprint(changed));
    }

    [Fact]
    public void Fingerprint_ChangesWhenADriverSetChanges()
    {
        FoundryConfigurationDocument document = Document();
        CustomDriverSetReference set = document.CustomDrivers.Sets[0];

        FoundryConfigurationDocument changed = document with
        {
            CustomDrivers = document.CustomDrivers with { Sets = [set with { Package = set.Package with { ContentHash = "dd" } }] }
        };

        Assert.NotEqual(NetworkCachePublicationState.Fingerprint(document), NetworkCachePublicationState.Fingerprint(changed));
    }

    [Fact]
    public void Fingerprint_ChangesWhenAPostInstallationActionChanges()
    {
        FoundryConfigurationDocument document = Document();

        FoundryConfigurationDocument changed = document with
        {
            PreOobe = document.PreOobe with { Actions = [document.PreOobe.Actions[0] with { Arguments = "/quiet" }] }
        };

        Assert.NotEqual(NetworkCachePublicationState.Fingerprint(document), NetworkCachePublicationState.Fingerprint(changed));
    }

    [Fact]
    public void Fingerprint_ChangesWhenADefaultChanges()
    {
        FoundryConfigurationDocument document = Document();

        FoundryConfigurationDocument changed = document with
        {
            CustomDrivers = document.CustomDrivers with { DefaultSetId = null }
        };

        Assert.NotEqual(NetworkCachePublicationState.Fingerprint(document), NetworkCachePublicationState.Fingerprint(changed));
    }

    [Fact]
    public void Fingerprint_IgnoresNetworkAndShareSettings()
    {
        FoundryConfigurationDocument document = Document();

        FoundryConfigurationDocument changed = document with
        {
            Network = document.Network with { WifiProvisioned = !document.Network.WifiProvisioned },
            NetworkCache = document.NetworkCache with
            {
                Shares = [new NetworkCacheShare { Id = "s9", Path = @"\\other\cache" }]
            }
        };

        Assert.Equal(NetworkCachePublicationState.Fingerprint(document), NetworkCachePublicationState.Fingerprint(changed));
    }

    [Fact]
    public void SharesNeedingPublication_ReturnsAShareWithoutARecord()
    {
        IReadOnlyList<NetworkCacheShare> due = NetworkCachePublicationState.SharesNeedingPublication(Document(), []);

        Assert.Equal(["s1", "s2"], due.Select(share => share.Id));
    }

    [Fact]
    public void SharesNeedingPublication_ReturnsOnlyStaleOrFailedShares()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        IReadOnlyList<NetworkCacheShare> stale = NetworkCachePublicationState.SharesNeedingPublication(
            document, [Record("s1", fingerprint), Record("s2", "other")]);
        IReadOnlyList<NetworkCacheShare> failed = NetworkCachePublicationState.SharesNeedingPublication(
            document, [Record("s1", fingerprint), Record("s2", fingerprint, succeeded: false)]);

        Assert.Equal(["s2"], stale.Select(share => share.Id));
        Assert.Equal(["s2"], failed.Select(share => share.Id));
    }

    [Fact]
    public void SharesNeedingPublication_ReturnsNothingWhenEveryShareMatches()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        IReadOnlyList<NetworkCacheShare> due = NetworkCachePublicationState.SharesNeedingPublication(
            document, [Record("s1", fingerprint), Record("s2", fingerprint)]);

        Assert.Empty(due);
    }

    [Fact]
    public void SharesNeedingPublication_MatchesARecordWhosePathDiffersByCaseOrTrailingBackslash()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        IReadOnlyList<NetworkCacheShare> due = NetworkCachePublicationState.SharesNeedingPublication(
            document,
            [Record("s1", fingerprint, @"\\SERVER\One\"), Record("s2", fingerprint, @"\\server\two")]);

        Assert.Empty(due);
    }

    [Fact]
    public void SharesNeedingPublication_ReturnsAShareWhoseRecordPointsToAnotherLocation()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        IReadOnlyList<NetworkCacheShare> due = NetworkCachePublicationState.SharesNeedingPublication(
            document, [Record("s1", fingerprint, @"\\elsewhere\one"), Record("s2", fingerprint)]);

        Assert.Equal(["s1"], due.Select(share => share.Id));
    }

    [Fact]
    public void SharesNeedingPublication_UsesTheLatestRecordOfAShare()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        IReadOnlyList<NetworkCacheShare> due = NetworkCachePublicationState.SharesNeedingPublication(
            document,
            [Record("s1", "old", sequence: 1), Record("s1", fingerprint, sequence: 2), Record("s2", fingerprint)]);

        Assert.Empty(due);
    }

    [Fact]
    public void SharesNeedingPublication_ReturnsNothingWhenTheCacheIsInactive()
    {
        FoundryConfigurationDocument document = Document();
        FoundryConfigurationDocument disabled = document with { NetworkCache = document.NetworkCache with { IsEnabled = false } };
        FoundryConfigurationDocument noShares = document with { NetworkCache = document.NetworkCache with { Shares = [] } };

        Assert.Empty(NetworkCachePublicationState.SharesNeedingPublication(disabled, []));
        Assert.Empty(NetworkCachePublicationState.SharesNeedingPublication(noShares, []));
    }

    [Fact]
    public void Evaluate_IsNotPublishedBeforeTheFirstPublication()
    {
        NetworkCachePublicationStatus status = NetworkCachePublicationState.Evaluate(Document(), [], canPublishIndex: true);

        Assert.False(status.IsUpToDate);
        Assert.Equal(NetworkCachePublicationLevel.NotPublished, status.Level);
        Assert.Empty(status.FailedShares);
    }

    [Fact]
    public void Evaluate_NamesTheSharesWhoseLastPublicationFailed()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        NetworkCachePublicationStatus status = NetworkCachePublicationState.Evaluate(
            document, [Record("s1", fingerprint), Record("s2", fingerprint, succeeded: false)], canPublishIndex: true);

        Assert.False(status.IsUpToDate);
        Assert.Equal(["s2"], status.FailedShares.Select(share => share.Id));
        Assert.Equal(["s2"], status.DueShares.Select(share => share.Id));
    }

    [Fact]
    public void Evaluate_TellsAnOlderPublicationFromNoPublication()
    {
        FoundryConfigurationDocument document = Document();

        NetworkCachePublicationStatus status = NetworkCachePublicationState.Evaluate(
            document, [Record("s1", "older", sequence: 4), Record("s2", "older", succeeded: false, sequence: 0)], canPublishIndex: true);

        Assert.Equal(NetworkCachePublicationLevel.NotPublished, status.Level);
        Assert.Equal(NetworkCachePublicationLevel.Outdated, status.LevelOf("s1"));
        Assert.Equal(NetworkCachePublicationLevel.NotPublished, status.LevelOf("s2"));
        Assert.Equal(NetworkCachePublicationLevel.UpToDate, NetworkCachePublicationStatus.UpToDate.Level);
    }

    [Fact]
    public void Evaluate_NamesAPublishedShareThatLostItsContent()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        NetworkCachePublicationStatus status = NetworkCachePublicationState.Evaluate(
            document, [Record("s1", fingerprint), Record("s2", fingerprint)], canPublishIndex: true, sharesMissingContent: ["s2", "removed"]);

        Assert.False(status.IsUpToDate);
        Assert.Empty(status.FailedShares);
        Assert.Equal(["s2"], status.MissingContentShares.Select(share => share.Id));
        Assert.Equal(["s2"], status.DueShares.Select(share => share.Id));
        Assert.Equal(NetworkCachePublicationLevel.NotPublished, status.LevelOf("s2"));
        Assert.Equal(NetworkCachePublicationLevel.UpToDate, status.LevelOf("s1"));
    }

    [Fact]
    public void Evaluate_ListsEveryShareThatMissesTheLatestChanges()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        NetworkCachePublicationStatus stale = NetworkCachePublicationState.Evaluate(document, [Record("s1", "old")], canPublishIndex: true);
        NetworkCachePublicationStatus upToDate = NetworkCachePublicationState.Evaluate(
            document, [Record("s1", fingerprint), Record("s2", fingerprint)], canPublishIndex: true);

        Assert.Equal(document.NetworkCache.Shares.Select(share => share.Id), stale.DueShares.Select(share => share.Id));
        Assert.Empty(upToDate.DueShares);
    }

    [Fact]
    public void Evaluate_DoesNotNameAShareThatOnlyMissesTheLatestChanges()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        NetworkCachePublicationStatus status = NetworkCachePublicationState.Evaluate(
            document, [Record("s1", fingerprint), Record("s2", "older")], canPublishIndex: true);

        Assert.False(status.IsUpToDate);
        Assert.Empty(status.FailedShares);
    }

    [Fact]
    public void Evaluate_IsUpToDateWhenEveryShareHasTheLatestChanges()
    {
        FoundryConfigurationDocument document = Document();
        string fingerprint = NetworkCachePublicationState.Fingerprint(document);

        NetworkCachePublicationStatus status = NetworkCachePublicationState.Evaluate(
            document, [Record("s1", fingerprint), Record("s2", fingerprint)], canPublishIndex: true);

        Assert.True(status.IsUpToDate);
        Assert.Empty(status.FailedShares);
    }

    [Fact]
    public void Evaluate_AFollowerIsAlwaysUpToDate()
    {
        FoundryConfigurationDocument document = Document();

        NetworkCachePublicationStatus status = NetworkCachePublicationState.Evaluate(
            document, [Record("s2", NetworkCachePublicationState.Fingerprint(document), succeeded: false)], canPublishIndex: false);

        Assert.True(status.IsUpToDate);
        Assert.Empty(status.FailedShares);
    }

    private static NetworkCachePublicationRecord Record(string shareId, string fingerprint, string? sharePath = null, bool succeeded = true, long sequence = 1)
    {
        return new()
        {
            ShareId = shareId,
            SharePath = sharePath ?? (shareId == "s1" ? @"\\server\one" : @"\\server\two"),
            Fingerprint = fingerprint,
            Sequence = sequence,
            Succeeded = succeeded,
            PublishedAtUtc = DateTimeOffset.UnixEpoch
        };
    }

    private static FoundryConfigurationDocument Document() => new()
    {
        CustomImages = new()
        {
            IsEnabled = true,
            DefaultImageId = "img1",
            Images = [new CustomImageReference { Id = "img1", ContentHash = "aa", DisplayName = "First" }]
        },
        CustomDrivers = new()
        {
            IsEnabled = true,
            DefaultSetId = "drv1",
            Sets = [new CustomDriverSetReference
            {
                Id = "drv1",
                DisplayName = "Drivers",
                Package = new ContentPackageReference { ContentHash = "cc", DisplayName = "Drivers", Length = 10, FileCount = 2 }
            }]
        },
        PreOobe = new()
        {
            IsEnabled = true,
            Actions = [PreOobeActionSettings.Create(PreOobeActionKind.Application, "Agent") with
            {
                Id = "act1",
                Package = new ContentPackageReference { ContentHash = "ee", DisplayName = "Agent", Length = 5, FileCount = 1 },
                EntryPoint = "setup.exe"
            }]
        },
        NetworkCache = new()
        {
            IsEnabled = true,
            Shares =
            [
                new NetworkCacheShare { Id = "s1", Path = @"\\server\one" },
                new NetworkCacheShare { Id = "s2", Path = @"\\server\two" }
            ]
        }
    };
}
