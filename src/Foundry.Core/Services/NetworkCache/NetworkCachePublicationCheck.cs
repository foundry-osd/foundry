// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// Checks that a share still holds what this PC published there, as Foundry Deploy checks it at startup: the index signed by
/// this PC at the recorded sequence or later, and every manifest and file it references. Only metadata is read. Foundry OSD
/// runs it when it measures a share, so content deleted on the share shows as not published.
/// </summary>
public static class NetworkCachePublicationCheck
{
    /// <summary>Returns whether the publication of <paramref name="publicKey"/> is complete under <paramref name="contentRoot"/>.</summary>
    /// <param name="contentRoot">The <c>&lt;share&gt;\Foundry</c> folder.</param>
    /// <param name="publicKey">The public key this PC signs the index with.</param>
    /// <param name="minimumSequence">The sequence this PC last published to the share; an older index means a lost publication.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="IOException">The share could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The share refused to list or read a file.</exception>
    public static async Task<bool> IsIntactAsync(string contentRoot, byte[] publicKey, long minimumSequence, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(publicKey);
        byte[]? file = await NetworkCacheIndexStore.ReadAsync(contentRoot, PublicationSigner.KeyId(publicKey), cancellationToken).ConfigureAwait(false);
        if (file is null ||
            NetworkCacheContentIndexCodec.Read(file, publicKey, minimumSequence, out NetworkCacheContentIndex? index) != NetworkCacheIndexReadStatus.Valid)
            return false;
        IReadOnlyList<string> missing = await NetworkCacheContentPresence.FindMissingAsync(contentRoot, index!, cancellationToken).ConfigureAwait(false);
        return missing.Count == 0;
    }
}
