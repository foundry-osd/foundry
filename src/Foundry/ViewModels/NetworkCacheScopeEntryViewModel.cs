// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>One row of the network cache IP scope table.</summary>
public sealed partial class NetworkCacheScopeEntryViewModel(NetworkCacheScope settings) : ObservableObject
{
    public string Id => Settings.Id;
    public string Name => Settings.Name;
    public string Value => Settings.Value;
    public Style StatusStyle => (Style)Application.Current.Resources[IsReady ? "FoundrySuccessTextBlockStyle" : "FoundryCriticalTextBlockStyle"];

    [ObservableProperty]
    public partial NetworkCacheScope Settings { get; set; } = settings;

    /// <summary>Gets or sets the localized type of <see cref="Value"/>.</summary>
    [ObservableProperty]
    public partial string KindText { get; set; } = string.Empty;

    /// <summary>Gets or sets the share paths in the order devices try them.</summary>
    [ObservableProperty]
    public partial string SharesText { get; set; } = string.Empty;

    /// <summary>Gets or sets what this scope still needs before media can be created, or that it is ready.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    partial void OnSettingsChanged(NetworkCacheScope value)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Value));
    }

    partial void OnIsReadyChanged(bool value) => OnPropertyChanged(nameof(StatusStyle));
}
