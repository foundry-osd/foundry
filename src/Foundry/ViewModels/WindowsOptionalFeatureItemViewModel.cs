// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

public sealed partial class WindowsOptionalFeatureItemViewModel : ObservableObject
{
    private WindowsOptionalFeatureState state;
    private string unchangedText = string.Empty;
    private string enableText = string.Empty;
    private string disableText = string.Empty;

    public WindowsOptionalFeatureItemViewModel(WindowsOptionalFeatureCatalogEntry catalogEntry)
    {
        CatalogEntry = catalogEntry;
    }

    public WindowsOptionalFeatureCatalogEntry CatalogEntry { get; }
    public string Id => CatalogEntry.Id;
    public string FeatureName => CatalogEntry.FeatureName;
    public int SortOrder => CatalogEntry.SortOrder;
    public string StateText => State switch
    {
        WindowsOptionalFeatureState.Enable => enableText,
        WindowsOptionalFeatureState.Disable => disableText,
        _ => unchangedText
    };
    public string EnableActionText => enableText;
    public string DisableActionText => disableText;
    public string ClearActionText => unchangedText;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailText { get; set; } = string.Empty;

    public WindowsOptionalFeatureState State
    {
        get => state;
        set
        {
            if (state == value)
            {
                return;
            }

            state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
        }
    }

    public void SetState(WindowsOptionalFeatureState value) => State = value;

    public void RefreshStateText(
        string unchangedText,
        string enableText,
        string disableText)
    {
        this.unchangedText = unchangedText;
        this.enableText = enableText;
        this.disableText = disableText;
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(EnableActionText));
        OnPropertyChanged(nameof(DisableActionText));
        OnPropertyChanged(nameof(ClearActionText));
    }
}
