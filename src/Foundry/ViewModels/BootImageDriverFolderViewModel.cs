// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.ViewModels;

/// <summary>
/// Represents a driver folder in the Boot Image page: a source folder plus a checkbox that controls whether it
/// is processed during customization (a disabled folder is kept but skipped).
/// </summary>
public sealed partial class BootImageDriverFolderViewModel : ObservableObject
{
    private readonly Action _onIsEnabledChanged;

    public BootImageDriverFolderViewModel(string path, bool isEnabled, string removeLabel, Action onIsEnabledChanged)
    {
        Path = path;
        RemoveLabel = removeLabel;
        _onIsEnabledChanged = onIsEnabledChanged;
        IsEnabled = isEnabled;
    }

    /// <summary>
    /// Gets the folder that contains drivers.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the localized label for the remove action.
    /// </summary>
    public string RemoveLabel { get; }

    /// <summary>
    /// Gets or sets whether the folder is injected during customization.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>
    /// Gets the underlying model persisted in configuration.
    /// </summary>
    public WinPeDriverFolder ToModel() => new() { Path = Path, IsEnabled = IsEnabled };

    partial void OnIsEnabledChanged(bool value) => _onIsEnabledChanged();
}
