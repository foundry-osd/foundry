// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.ViewModels;

/// <summary>
/// Represents a single PowerShell module selected for integration into the boot image.
/// </summary>
public sealed partial class BootImageModuleViewModel : ObservableObject
{
    public BootImageModuleViewModel(PowerShellModuleSelection selection, string removeLabel)
    {
        Selection = selection;
        RemoveLabel = removeLabel;
    }

    /// <summary>
    /// Gets the underlying module selection persisted in configuration.
    /// </summary>
    public PowerShellModuleSelection Selection { get; }

    /// <summary>
    /// Gets the localized label for the remove action.
    /// </summary>
    public string RemoveLabel { get; }

    /// <summary>
    /// Gets the module display name.
    /// </summary>
    public string Name => Selection.Name;

    /// <summary>
    /// Gets the secondary line describing the module source and version or local path.
    /// </summary>
    public string Detail => Selection.Source == PowerShellModuleSource.Gallery
        ? $"PowerShell Gallery · {Selection.Version}"
        : $"Local · {Selection.LocalPath}";
}
