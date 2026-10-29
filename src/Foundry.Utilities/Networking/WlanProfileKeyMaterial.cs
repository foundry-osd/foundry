// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Networking;

/// <summary>
/// Whether a WLAN profile embeds key material, as detected by <see cref="WlanProfileReader.DetectKeyMaterial"/>.
/// </summary>
public enum WlanProfileKeyMaterial
{
    /// <summary>The profile was read completely and contains no <c>keyMaterial</c> element.</summary>
    Absent,

    /// <summary>The profile contains a <c>keyMaterial</c> element.</summary>
    Present,

    /// <summary>
    /// The profile could not be read (invalid XML, a prohibited DTD, an undecodable encoding), so its content is unknown and
    /// callers that protect secrets must treat it as containing key material.
    /// </summary>
    Unreadable
}
