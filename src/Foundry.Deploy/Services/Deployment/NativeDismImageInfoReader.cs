// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Images;
using NativeMetadata = Foundry.Utilities.Imaging.WindowsImageMetadata;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Adapts process-owned native metadata to the catalog contract without narrowing image sizes.</summary>
internal sealed class NativeDismImageInfoReader : IWindowsImageInfoReader
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<NativeMetadata>>> _read;

    public NativeDismImageInfoReader() : this(NativeCustomImageMetadataReader.ReadNativeAsync)
    {
    }

    internal NativeDismImageInfoReader(Func<string, CancellationToken, Task<IReadOnlyList<NativeMetadata>>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _read = read;
    }

    public async Task<IReadOnlyList<WindowsImageInfo>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        IReadOnlyList<NativeMetadata> images = await _read(imagePath, cancellationToken).ConfigureAwait(false);
        return images.Select(image => new WindowsImageInfo(image.Index, image.Name, image.EditionId,
            image.ImageSize, image.Architecture, image.Version)).ToArray();
    }
}
