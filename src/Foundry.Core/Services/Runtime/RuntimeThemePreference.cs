// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Runtime;

/// <summary>Theme a WinPE application hands to the runtime it stages, for example Foundry.AutopilotInteractive.</summary>
public enum RuntimeThemePreference
{
    /// <summary>Follows the Windows theme.</summary>
    System,

    /// <summary>Always uses the light theme.</summary>
    Light,

    /// <summary>Always uses the dark theme.</summary>
    Dark
}
