// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Files;

namespace Foundry.Core.Services.Media;

/// <summary>
/// What a media build captures beside the configuration: the library holding the referenced authoring files and the
/// session passwords that never enter the document.
/// </summary>
/// <param name="Files">The authoring file library the referenced files are copied from, with hash verification.</param>
public sealed record BuildSnapshotSources(AuthoringFileLibrary Files)
{
    /// <summary>Gets the network passwords for the captured network settings.</summary>
    public NetworkSecrets Network { get; init; } = new();

    /// <summary>Gets the password of the Autopilot boot media PFX.</summary>
    public string? AutopilotPfxPassword { get; init; }
}
