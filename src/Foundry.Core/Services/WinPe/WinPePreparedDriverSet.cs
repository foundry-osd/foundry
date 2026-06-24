// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPePreparedDriverSet
{
    public IReadOnlyList<string> ExtractionDirectories { get; init; } = [];
    public IReadOnlyList<string> DownloadedPackagePaths { get; init; } = [];
}
