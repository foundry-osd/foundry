// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Operations;

/// <summary>
/// Identifies the long-running operation currently reported through the shared operation progress service.
/// </summary>
public enum OperationKind
{
    /// <summary>
    /// No user-visible operation is active.
    /// </summary>
    None,

    /// <summary>
    /// Windows ADK or WinPE add-on installation is running.
    /// </summary>
    AdkInstall,

    /// <summary>
    /// Windows ADK or WinPE add-on upgrade is running.
    /// </summary>
    AdkUpgrade,

    /// <summary>
    /// Boot media creation is running.
    /// </summary>
    MediaCreation
}
