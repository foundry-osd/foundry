// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.ViewModels;

/// <summary>One share in the scope dialog's ordered list: whether the scope uses it, and whether it can move.</summary>
public sealed partial class NetworkCacheScopeShareRowViewModel(string shareId, string path, bool isIncluded, string moveUpLabel, string moveDownLabel)
    : ObservableObject
{
    public string ShareId { get; } = shareId;
    public string Path { get; } = path;
    public string MoveUpLabel { get; } = moveUpLabel;
    public string MoveDownLabel { get; } = moveDownLabel;

    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = isIncluded;

    [ObservableProperty]
    public partial bool CanMoveUp { get; set; }

    [ObservableProperty]
    public partial bool CanMoveDown { get; set; }
}
