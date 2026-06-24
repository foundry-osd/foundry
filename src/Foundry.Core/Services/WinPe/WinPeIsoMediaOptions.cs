// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeIsoMediaOptions
{
    public WinPeWorkspacePreparationResult? PreparedWorkspace { get; init; }
    public string OutputIsoPath { get; init; } = string.Empty;
    public string IsoTempDirectoryPath { get; init; } = string.Empty;
    public bool ForceOverwriteOutput { get; init; } = true;
    public IProgress<WinPeMediaProgress>? Progress { get; init; }
}
