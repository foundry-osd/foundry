// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Reports progress of a media dependency download or cache verification as data, so consumers format sizes
/// and item names in the user's language.
/// </summary>
public sealed record WinPeDownloadProgress
{
    /// <summary>Gets the transfer percentage, or <see langword="null"/> when the total size is unknown.</summary>
    public int? Percent { get; init; }

    /// <summary>Gets one of the <see cref="WinPeProgressStatuses"/> values.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the downloaded item, such as a driver package file name or a runtime application name.</summary>
    public string ItemName { get; init; } = string.Empty;

    /// <summary>Gets the 1-based position of the item when several items are downloaded in sequence.</summary>
    public int? ItemNumber { get; init; }

    /// <summary>Gets the number of items downloaded in sequence.</summary>
    public int? ItemCount { get; init; }

    /// <summary>Gets the bytes transferred or verified so far.</summary>
    public long? BytesTransferred { get; init; }

    /// <summary>Gets the expected size in bytes, when known.</summary>
    public long? TotalBytes { get; init; }
}
