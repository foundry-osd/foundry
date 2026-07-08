// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.ViewModels;

/// <summary>
/// Represents a single additional root folder overlay shown in the boot image page.
/// </summary>
public sealed partial class BootImageRootFolderViewModel : ObservableObject
{
    public BootImageRootFolderViewModel(string path, string removeLabel)
    {
        Path = path;
        RemoveLabel = removeLabel;
    }

    /// <summary>
    /// Gets the folder path copied into the boot image root.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the localized label for the remove action.
    /// </summary>
    public string RemoveLabel { get; }
}
