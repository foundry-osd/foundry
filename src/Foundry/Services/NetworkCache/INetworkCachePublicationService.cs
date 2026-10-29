// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Media;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Telemetry;
using Foundry.Core.Services.WinPe;

namespace Foundry.Services.NetworkCache;

/// <summary>The prepared imported content a network cache publication copies to each share; the caller owns the leases.</summary>
/// <param name="CustomImages">The prepared custom images, or <see langword="null"/> when the section is disabled.</param>
/// <param name="PostInstallation">The prepared post-installation packages.</param>
/// <param name="CustomDrivers">The prepared custom driver sets, or <see langword="null"/> when the section is disabled.</param>
public sealed record NetworkCacheContentLeases(
    WinPeCustomImageMediaLease? CustomImages,
    WinPeContentPackageMediaLease PostInstallation,
    WinPeContentPackageMediaLease? CustomDrivers);

/// <summary>The outcome of one publication to the network cache shares.</summary>
/// <param name="Results">One result per share, in share order; empty when the network cache is not active.</param>
public sealed record NetworkCachePublicationOutcome(IReadOnlyList<NetworkCacheContentPublishResult> Results)
{
    /// <summary>Gets the outcome of a publication that was not attempted.</summary>
    public static NetworkCachePublicationOutcome None { get; } = new([]);

    /// <summary>Gets the paths of the shares that did not receive the content and the index.</summary>
    public IReadOnlyList<string> SkippedSharePaths =>
        Results.Where(result => result.Status != NetworkCacheContentPublishStatus.Published).Select(result => result.Share.Path).ToArray();

    /// <summary>Gets the <c>network_cache_publish_result</c> telemetry value.</summary>
    public string TelemetryResult => NetworkCachePublishTelemetry.Result(Results);
}

/// <summary>
/// Publishes the imported content of the current configuration and its signed index to the network cache shares, for media
/// creation (after its output) and for Publish on the Network cache page (without building media).
/// </summary>
/// <remarks>Members run on the UI thread.</remarks>
public interface INetworkCachePublicationService
{
    /// <summary>
    /// Returns the signed index binding of media about to be built, or <see langword="null"/> when the network cache is not active.
    /// Its minimum sequence is the highest sequence of the trusted key's index found on the shares now, so the media refuse an
    /// older index replayed later; an unreachable share or a missing or unverifiable index counts as 0. Without a trusted key it
    /// returns <see langword="null"/> and generation refuses the active cache, which readiness already reported.
    /// </summary>
    Task<NetworkCacheIndexBinding?> ResolveIndexBindingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Publishes <paramref name="content"/> to every share of the snapshot's active network cache; a reference or standalone PC
    /// then signs and writes the index of <paramref name="sections"/> on each share whose content succeeded, a follower publishes
    /// the content only. Every attempt replaces the publication record of each share. A share that cannot be reached or written
    /// is a result, never an exception; only cancellation propagates.
    /// </summary>
    /// <param name="snapshot">The frozen configuration and share passwords.</param>
    /// <param name="content">The prepared imported content.</param>
    /// <param name="sections">The content sections bound to <paramref name="content"/>, written into the index.</param>
    /// <param name="minimumSequence">The minimum index sequence written into media being built, or 0 without media.</param>
    /// <param name="report">
    /// Receives the progress across all shares (0-100) and the detail line naming the share, on the calling (UI) thread, in
    /// report order; nothing is reported once the returned task completes.
    /// </param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    Task<NetworkCachePublicationOutcome> PublishContentAsync(
        BuildSnapshot snapshot,
        NetworkCacheContentLeases content,
        NetworkCacheContentSections sections,
        long minimumSequence,
        Action<int, string> report,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs Publish: captures the configuration, prepares only the imported content (no WinPE workspace), binds it and publishes
    /// it with the index, in the operation dialog, cancellable and blocking navigation like media creation. Does nothing when
    /// another operation runs, the network cache is not active or this PC does not publish the index. Never throws.
    /// </summary>
    Task PublishAsync();
}
