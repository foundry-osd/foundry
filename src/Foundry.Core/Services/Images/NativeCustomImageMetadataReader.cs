// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Utilities.Imaging;

namespace Foundry.Core.Services.Images;

/// <summary>Maps native technical metadata into portable custom-image references without catalog filtering.</summary>
public sealed class NativeCustomImageMetadataReader : ICustomImageMetadataReader
{
    private static readonly NativeDismImageInfoReader Reader = CreateReader();

    private static NativeDismImageInfoReader CreateReader()
    {
        var reader = new NativeDismImageInfoReader();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { reader.Dispose(); }
            catch
            {
                // Process shutdown must not replace the application's original outcome.
            }
        };
        return reader;
    }

    /// <summary>Reads native metadata through the shared process owner without exposing its disposal lifetime.</summary>
    public static Task<IReadOnlyList<WindowsImageMetadata>> ReadNativeAsync(string imagePath, CancellationToken cancellationToken = default)
        => Reader.ReadAsync(imagePath, cancellationToken);

    public async Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WindowsImageMetadata> images = await ReadNativeAsync(imagePath, cancellationToken).ConfigureAwait(false);
        return images.Select(image => new CustomImageIndex
        {
            Index = image.Index,
            Name = image.Name,
            Description = image.Description,
            Architecture = image.Architecture,
            EditionId = image.EditionId,
            ProductType = image.ProductType,
            Build = image.Version.Build == 0 ? null : image.Version.Build,
            Version = image.Version == new Version(0, 0, 0) ? null : image.Version.ToString(),
            ExpandedSizeBytes = checked((long)image.ImageSize),
            Languages = image.Languages,
            DefaultLanguage = image.DefaultLanguageIndex >= 0 && image.DefaultLanguageIndex < image.Languages.Count
                ? image.Languages[image.DefaultLanguageIndex] : null
        }).ToArray();
    }
}
