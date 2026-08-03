// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes a source folder whose contents are recursively copied into a relative destination inside the
/// mounted boot image. The destination is relative to the image root; <c>\</c> is the image root and, for
/// example, <c>\Windows</c> copies the source contents beneath the image's Windows folder.
/// </summary>
public sealed record WinPeAdditionalRootFolder
{
    /// <summary>
    /// Gets the source folder whose contents are copied.
    /// </summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the destination path relative to the boot image root. Defaults to the image root.
    /// </summary>
    public string DestinationRelativePath { get; init; } = @"\";
}
