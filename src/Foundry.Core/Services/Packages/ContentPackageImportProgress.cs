// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Packages;

/// <summary>
/// Reports one content package import as a single copy-and-verify completion; it carries no file names.
/// </summary>
/// <param name="CompletedBytes">
/// Average of the bytes copied and the bytes verified, so the copy pass and the verification pass weigh equally;
/// it reaches <paramref name="TotalBytes"/> when both passes have finished.
/// </param>
/// <param name="TotalBytes">Package size measured from file metadata during the source inventory.</param>
/// <param name="CompletedFiles">Files copied and verified.</param>
/// <param name="TotalFiles">Files found by the source inventory.</param>
public sealed record ContentPackageImportProgress(long CompletedBytes, long TotalBytes, int CompletedFiles, int TotalFiles);
