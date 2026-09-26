// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Images;

public sealed partial class CustomImageLibraryService
{
    /// <summary>Refreshes cached metadata without copying or hashing image bytes. Media builds still require a verified source lease.</summary>
    public async Task<CustomImageReference> RefreshMetadataAsync(CustomImageReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!CustomImageSettingsValidator.IsValidReference(reference))
            throw new InvalidDataException("The custom image reference is invalid.");
        cancellationToken.ThrowIfCancellationRequested();
        using FileStream libraryLock = AcquireLibraryLock();
        IReadOnlyList<CustomImageReference> entries = await ListAsync(cancellationToken).ConfigureAwait(false);
        CustomImageReference existing = entries.FirstOrDefault(image => image.Length == reference.Length &&
            image.ContentHash.Equals(reference.ContentHash, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The custom image reference is not present in the local library.");
        string imagePath = OwnedPath($"content/{reference.ContentHash.ToLowerInvariant()}/image.wim");
        await using FileStream imageLease = OpenRead(imagePath);
        if (imageLease.Length != reference.Length)
            throw new InvalidDataException("The custom image source size has changed.");
        IReadOnlyList<CustomImageIndex> indexes = await metadataReader.ReadAsync(imagePath, cancellationToken).ConfigureAwait(false);
        CustomImageReference refreshed = reference with { Indexes = indexes };
        if (!CustomImageSettingsValidator.IsValidReference(refreshed) ||
            !reference.Indexes.Select(index => index.Index).SequenceEqual(indexes.Select(index => index.Index)) ||
            !existing.Indexes.Select(index => index.Index).SequenceEqual(indexes.Select(index => index.Index)))
            throw new InvalidDataException("The image indexes do not match their imported metadata.");
        cancellationToken.ThrowIfCancellationRequested();
        await WriteIndexAsync(entries.Select(entry => entry.Id == existing.Id ? entry with { Indexes = indexes } : entry).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return refreshed;
    }
}
