// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

/// <summary>
/// Identifies the user-facing ADK setup action that resolves a detected installation state.
/// </summary>
public enum AdkSetupAction
{
    /// <summary>No automated setup action applies; the state is ready or needs manual repair.</summary>
    None,

    /// <summary>Installs missing components without removing registered ADK bundles.</summary>
    Install,

    /// <summary>Removes registered ADK bundles before installing the supported release.</summary>
    Upgrade
}
