// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Where the preflight found the <c>Foundry.AutopilotInteractive</c> archive that the Autopilot step installs.
/// </summary>
/// <param name="Source">HTTPS release download address, or the absolute path of the debug archive read from the boot image at <see cref="Foundry.Core.Services.Runtime.WinPeWorkspaceLayout.AutopilotInteractiveArchivePath"/>.</param>
/// <param name="Sha256">Trusted SHA256 of the archive: the release digest, or the hash of the debug archive, pinned when it is resolved.</param>
/// <param name="IsLocalArchive">Whether <paramref name="Source"/> is a media archive copied locally instead of downloaded.</param>
public sealed record AutopilotInteractiveRuntimeOrigin(string Source, string Sha256, bool IsLocalArchive);
