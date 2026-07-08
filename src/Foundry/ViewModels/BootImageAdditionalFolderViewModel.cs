// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.ViewModels;

/// <summary>
/// Represents an additional folder overlay: a source folder plus the relative destination inside the boot image
/// its contents are copied to (<c>\</c> is the image root, <c>\Windows</c> the image Windows folder, and so on).
/// </summary>
public sealed partial class BootImageAdditionalFolderViewModel : ObservableObject
{
    private readonly Action _onDestinationChanged;

    public BootImageAdditionalFolderViewModel(
        string sourcePath,
        string destinationRelativePath,
        string removeLabel,
        Action onDestinationChanged)
    {
        SourcePath = sourcePath;
        RemoveLabel = removeLabel;
        _onDestinationChanged = onDestinationChanged;
        DestinationRelativePath = string.IsNullOrWhiteSpace(destinationRelativePath) ? @"\" : destinationRelativePath;
    }

    /// <summary>
    /// Gets the source folder whose contents are copied.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Gets the localized label for the remove action.
    /// </summary>
    public string RemoveLabel { get; }

    /// <summary>
    /// Gets or sets the destination path relative to the boot image root.
    /// </summary>
    [ObservableProperty]
    public partial string DestinationRelativePath { get; set; }

    /// <summary>
    /// Gets the underlying model persisted in configuration.
    /// </summary>
    public WinPeAdditionalRootFolder ToModel() => new()
    {
        SourcePath = SourcePath,
        DestinationRelativePath = string.IsNullOrWhiteSpace(DestinationRelativePath) ? @"\" : DestinationRelativePath.Trim()
    };

    partial void OnDestinationRelativePathChanged(string value) => _onDestinationChanged();
}
