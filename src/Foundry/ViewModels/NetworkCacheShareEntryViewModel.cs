// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>One row of the network cache share table.</summary>
public sealed partial class NetworkCacheShareEntryViewModel(NetworkCacheShare settings) : ObservableObject
{
    public string Id => Settings.Id;
    public string Path => Settings.Path;
    public Style StatusStyle => (Style)Application.Current.Resources[IsReady ? "FoundrySuccessTextBlockStyle" : "FoundryCriticalTextBlockStyle"];

    [ObservableProperty]
    public partial NetworkCacheShare Settings { get; set; } = settings;

    /// <summary>Gets or sets the user name of the share's account, or the text that stands for no account.</summary>
    [ObservableProperty]
    public partial string AccountText { get; set; } = string.Empty;

    /// <summary>Gets or sets what this share still needs before media can be created, or that it is ready.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    partial void OnSettingsChanged(NetworkCacheShare value) => OnPropertyChanged(nameof(Path));

    partial void OnIsReadyChanged(bool value) => OnPropertyChanged(nameof(StatusStyle));
}
