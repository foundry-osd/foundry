// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Utilities.Storage;

/// <summary>
/// Describes the observable facts for a file-system volume.
/// </summary>
public sealed record VolumeInfo(
    string RootPath,
    string VolumeLabel,
    DriveType DriveType,
    bool IsReady,
    long AvailableFreeSpace);
