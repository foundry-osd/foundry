// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Discovers the WinPE optional components available in the installed ADK for a given architecture.
/// </summary>
public interface IWinPeOptionalComponentCatalogService
{
    /// <summary>
    /// Scans the ADK WinPE_OCs folder and returns the available neutral optional components,
    /// flagging Foundry's recommended defaults.
    /// </summary>
    /// <param name="kitsRootPath">The resolved ADK KitsRoot10 path.</param>
    /// <param name="architecture">The target WinPE architecture.</param>
    /// <returns>The available components sorted by name, or a diagnostic when the folder is missing.</returns>
    WinPeResult<IReadOnlyList<WinPeOptionalComponent>> GetAvailableComponents(
        string kitsRootPath,
        WinPeArchitecture architecture);
}
