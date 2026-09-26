// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Images;

internal static class CustomImageExportCapacityPolicy
{
    private const long ExportReserveBytes = 1024L * 1024 * 1024;

    internal static void Validate(IReadOnlyList<CustomImageIndex> indexes, long? availableBytes, string format)
    {
        if (indexes.Count == 0 || indexes.Any(index => index.ExpandedSizeBytes <= 0))
            throw new InvalidDataException("The source image expanded size is unavailable for export capacity planning.");
        long expandedBytes = 0;
        foreach (CustomImageIndex index in indexes)
            expandedBytes = checked(expandedBytes + index.ExpandedSizeBytes);
        long requiredBytes = checked(expandedBytes + ExportReserveBytes);
        if (availableBytes is null or < 0 || string.IsNullOrWhiteSpace(format))
            throw new IOException("The image export destination capacity could not be verified.");
        if (format.Equals("FAT32", StringComparison.OrdinalIgnoreCase) && requiredBytes > uint.MaxValue)
            throw new IOException("The estimated exported image exceeds the FAT32 file-size limit.");
        if (availableBytes < requiredBytes)
            throw new IOException("There is insufficient space for the estimated exported image and export reserve.");
    }
}
