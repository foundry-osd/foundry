// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeWorkspacePreparationResult
{
    public required WinPeBuildArtifact Artifact { get; init; }
    public required WinPeToolPaths Tools { get; init; }
    public bool UseBootEx { get; init; }

    /// <summary>
    /// Gets the image the boot media was built from; <see cref="WinPeBootImageSource.WinPe"/> means the x64 ADK fallback.
    /// </summary>
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinRe;

    /// <summary>
    /// Gets why WinRE could not be used when the media fell back to the ADK Windows PE image.
    /// </summary>
    public WinPeDiagnostic? BootImageFallbackCause { get; init; }
}
