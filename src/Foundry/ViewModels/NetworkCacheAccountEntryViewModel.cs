// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>One row of the network cache account table.</summary>
public sealed partial class NetworkCacheAccountEntryViewModel(NetworkCacheAccount settings) : ObservableObject
{
    public string Id => Settings.Id;
    public string UserName => Settings.UserName;

    [ObservableProperty]
    public partial NetworkCacheAccount Settings { get; set; } = settings;

    /// <summary>Gets or sets whether the session holds the account password, as shown in the table.</summary>
    [ObservableProperty]
    public partial string PasswordText { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of shares that open with this account.</summary>
    [ObservableProperty]
    public partial int UsedByCount { get; set; }

    partial void OnSettingsChanged(NetworkCacheAccount value) => OnPropertyChanged(nameof(UserName));
}
