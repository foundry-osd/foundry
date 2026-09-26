// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Images;

public sealed partial class CustomImageLibraryService
{
    /// <summary>Inspects the selected container without copying, denying writes and deletion until inspection and mount cleanup complete.</summary>
    public async Task<CustomImageImportPreview> PreviewAsync(string sourcePath, string? isoImagePath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using CustomImageImportSource source = await CustomImageImportSource.OpenAsync(sourcePath, cancellationToken, isoImagePath).ConfigureAwait(false);
        await using FileStream image = OpenRead(source.ImagePath);
        IReadOnlyList<CustomImageIndex> indexes = await metadataReader.ReadAsync(source.ImagePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Length <= 0 || indexes.Count is 0 or > CustomImageSettingsValidator.MaximumIndexes ||
            indexes.Any(index => index is null || index.Index <= 0) ||
            indexes.Select(index => index.Index).Distinct().Count() != indexes.Count)
            throw new InvalidDataException("The source image does not contain usable image indexes.");
        return new(image.Length, indexes.ToArray());
    }
}
