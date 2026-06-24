// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes Windows AI component removal settings consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployAiComponentRemovalSettings
{
    /// <summary>
    /// Gets whether AI component removal should run before OOBE.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets whether the Microsoft Copilot AppX package and policies should be removed.
    /// </summary>
    public bool RemoveCopilot { get; init; }

    /// <summary>
    /// Gets whether the Copilot+ AI Hub AppX package should be removed.
    /// </summary>
    public bool RemoveAiHub { get; init; }

    /// <summary>
    /// Gets whether Windows Recall should be disabled through policy.
    /// </summary>
    public bool DisableRecall { get; init; }

    /// <summary>
    /// Gets whether Click to Do should be disabled through policy.
    /// </summary>
    public bool DisableClickToDo { get; init; }

    /// <summary>
    /// Gets whether the Windows AI service should be prevented from autostarting.
    /// </summary>
    public bool DisableAiServiceAutoStart { get; init; }

    /// <summary>
    /// Gets whether Microsoft Edge AI features should be disabled through policy.
    /// </summary>
    public bool DisableEdgeAi { get; init; }

    /// <summary>
    /// Gets whether Paint AI features should be disabled through policy.
    /// </summary>
    public bool DisablePaintAi { get; init; }

    /// <summary>
    /// Gets whether Notepad AI features should be disabled through policy.
    /// </summary>
    public bool DisableNotepadAi { get; init; }
}
