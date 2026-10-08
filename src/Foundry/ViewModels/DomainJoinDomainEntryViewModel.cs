// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>
/// Displays one joinable domain in the domain table. The row outlives saves of its domain so the table keeps its
/// selection; the page view model refreshes the texts that depend on the whole configuration.
/// </summary>
public sealed partial class DomainJoinDomainEntryViewModel(DomainJoinDomainSettings settings) : ObservableObject
{
    public string Id => Settings.Id;
    public string DomainName => Settings.DomainName;
    public int OrganizationalUnitCount => Settings.OrganizationalUnits.Count;
    public Style StatusStyle => (Style)Application.Current.Resources[IsReady ? "FoundrySuccessTextBlockStyle" : "FoundryCriticalTextBlockStyle"];

    [ObservableProperty]
    public partial DomainJoinDomainSettings Settings { get; set; } = settings;

    /// <summary>Gets or sets the dedicated account, or the word that stands for the shared account.</summary>
    [ObservableProperty]
    public partial string AccountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DefaultText { get; set; } = string.Empty;

    /// <summary>Gets or sets what this domain still needs before media can be created, or that it is ready.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    partial void OnSettingsChanged(DomainJoinDomainSettings value)
    {
        OnPropertyChanged(nameof(DomainName));
        OnPropertyChanged(nameof(OrganizationalUnitCount));
    }

    partial void OnIsReadyChanged(bool value) => OnPropertyChanged(nameof(StatusStyle));
}
