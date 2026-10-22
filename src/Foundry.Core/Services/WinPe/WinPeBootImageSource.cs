// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Identifies the image a boot media was actually built from.
/// </summary>
public enum WinPeBootImageSource
{
    /// <summary>The Windows Recovery Environment of the newest supported Windows source; the normal case.</summary>
    WinRe = 0,

    /// <summary>The ADK Windows PE image, used for x64 only when no WinRE could be obtained; it has no Wi-Fi support.</summary>
    WinPe = 1
}
