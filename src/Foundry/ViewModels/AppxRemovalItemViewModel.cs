// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.ViewModels;

public sealed partial class AppxRemovalItemViewModel : ObservableObject
{
    public AppxRemovalItemViewModel(string packageName, string displayName)
    {
        PackageName = packageName;
        DisplayName = displayName;
    }

    public string PackageName { get; }
    public string DisplayName { get; }
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
