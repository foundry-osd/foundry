// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.ViewModels;

/// <summary>
/// Represents a single PowerShell Gallery search result shown in the boot image module picker.
/// </summary>
public sealed partial class BootImageModuleSearchResultViewModel : ObservableObject
{
    public BootImageModuleSearchResultViewModel(PowerShellGalleryModule module, string addLabel)
    {
        Module = module;
        AddLabel = addLabel;
    }

    /// <summary>
    /// Gets the underlying Gallery module.
    /// </summary>
    public PowerShellGalleryModule Module { get; }

    /// <summary>
    /// Gets the localized label for the add action.
    /// </summary>
    public string AddLabel { get; }

    /// <summary>
    /// Gets the module display name.
    /// </summary>
    public string Name => Module.Name;

    /// <summary>
    /// Gets the module version.
    /// </summary>
    public string Version => Module.Version;

    /// <summary>
    /// Gets the module description.
    /// </summary>
    public string Description => Module.Description;
}
