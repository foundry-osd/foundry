// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.WinPe;
using Foundry.Services.NetworkCache;
using Foundry.Telemetry;

namespace Foundry.ViewModels;

public sealed partial class StartMediaViewModel
{
    private readonly INetworkCachePublicationService networkCachePublication;
    // Shares that did not receive the imported content of the running media operation; shown after its success message.
    private string? networkCachePublicationNotice;
    // Minimum index sequence written into the media of the running operation; the published index never goes below it.
    private long networkCacheMinimumSequence;
    // network_cache_publish_result of the running media operation; none until the publication runs.
    private string networkCachePublishResult = TelemetryNetworkCachePublishResults.None;

    /// <summary>Resets the network cache publication state of a media operation that is starting.</summary>
    private void ResetNetworkCachePublication()
    {
        networkCachePublicationNotice = null;
        networkCacheMinimumSequence = 0;
        networkCachePublishResult = TelemetryNetworkCachePublishResults.None;
    }

    /// <summary>
    /// Returns the signed index binding of the media about to be built (<see cref="INetworkCachePublicationService.ResolveIndexBindingAsync"/>)
    /// and remembers its minimum sequence for the publication that follows the media output.
    /// </summary>
    private async Task<NetworkCacheIndexBinding?> ResolveNetworkCacheIndexBindingAsync(CancellationToken cancellationToken)
    {
        NetworkCacheIndexBinding? binding = await networkCachePublication.ResolveIndexBindingAsync(cancellationToken);
        networkCacheMinimumSequence = binding?.MinimumSequence ?? 0;
        return binding;
    }

    /// <summary>
    /// Publishes the imported content held by the prepared leases, and the index on a reference or standalone PC, to every
    /// network cache share, so devices started without this media, PXE included, find it on their share. It runs after the media
    /// output succeeded and before the leases are released. A share that cannot be reached or written is a notice on the result,
    /// never a media failure; only cancellation of the media operation propagates.
    /// </summary>
    private async Task PublishImportedContentToNetworkCacheAsync(BuildSnapshot snapshot, PreparedMediaWorkspace workspace, CancellationToken cancellationToken)
    {
        NetworkCacheSettings settings = snapshot.Configuration.NetworkCache;
        if (!NetworkCacheContentPublisher.IsActive(settings)) return;

        // The plan has its own publication step when the cache was active as the build started, Finishing otherwise.
        MediaCreationPhase phase = progressPlan?.Includes(MediaCreationPhase.PublishingToNetworkCache) == true
            ? MediaCreationPhase.PublishingToNetworkCache
            : MediaCreationPhase.Finishing;
        ReportPhase(phase, 0, localizationService.GetString("StartMedia.Operation.PublishingToNetworkCache"));
        NetworkCachePublicationOutcome outcome = await networkCachePublication.PublishContentAsync(
            snapshot,
            new NetworkCacheContentLeases(workspace.CustomImages, workspace.PostInstallation, workspace.CustomDrivers),
            workspace.NetworkCacheSections,
            networkCacheMinimumSequence,
            (percent, detail) => ReportPhase(phase, percent, detail, percent),
            cancellationToken);
        networkCachePublishResult = outcome.TelemetryResult;
        IReadOnlyList<string> skipped = outcome.SkippedSharePaths;
        if (skipped.Count > 0)
        {
            networkCachePublicationNotice = localizationService.FormatString(
                "StartMedia.Operation.NetworkCachePublicationFailedFormat", string.Join(", ", skipped));
        }
    }
}
