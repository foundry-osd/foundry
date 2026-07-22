// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Microsoft.UI.Xaml;

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
    /// Gets the secondary line describing a Gallery module's source and version.
    /// </summary>
    public string Detail => Selection.Source == PowerShellModuleSource.Gallery
        ? $"PowerShell Gallery · {Selection.Version}"
        : $"Local · {Selection.LocalPath}";

    /// <summary>
    /// Gets the local module folder path (clickable for local modules).
    /// </summary>
    public string LocalPath => Selection.LocalPath;

    /// <summary>
    /// Gets the PowerShell Gallery version page URL for a Gallery module, or <see langword="null"/> otherwise.
    /// </summary>
    public Uri? GalleryUri => Selection.Source == PowerShellModuleSource.Gallery
        && !string.IsNullOrWhiteSpace(Selection.Name)
        && !string.IsNullOrWhiteSpace(Selection.Version)
        ? new Uri($"https://www.powershellgallery.com/packages/{Uri.EscapeDataString(Selection.Name)}/{Uri.EscapeDataString(Selection.Version)}")
        : null;

    /// <summary>
    /// Gets whether the module name is shown as a clickable link to its Gallery version page.
    /// </summary>
    public Visibility GalleryLinkVisibility => GalleryUri is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Gets whether the module name is shown as plain text (non-Gallery modules).
    /// </summary>
    public Visibility NameTextVisibility => GalleryUri is null ? Visibility.Visible : Visibility.Collapsed;
}
