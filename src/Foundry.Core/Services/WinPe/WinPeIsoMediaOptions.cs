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
    /// <summary>Publishes installation images outside boot.wim through an output-specific ISO staging tree.</summary>
    public WinPeCustomImageMediaLease? CustomImages { get; init; }
    /// <summary>Binds the exact custom-image package to the configuration provisioned into boot.wim.</summary>
    public string? DeployConfigurationJson { get; init; }
    public IProgress<WinPeMediaProgress>? Progress { get; init; }
}
