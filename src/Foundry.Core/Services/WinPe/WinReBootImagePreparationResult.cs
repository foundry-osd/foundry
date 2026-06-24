// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinReBootImagePreparationResult
{
    public required IReadOnlyList<WinReDependencyFile> DependencyFiles { get; init; }
}

public sealed record WinReDependencyFile
{
    public required string FileName { get; init; }
    public required string StagedPath { get; init; }
}
