// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>Displays a saved or discovered OU in a table row.</summary>
public sealed partial class DomainJoinOrganizationalUnitEntryViewModel(DomainJoinOrganizationalUnitSettings settings) : ObservableObject
{
    public DomainJoinOrganizationalUnitSettings Settings { get; } = settings;
    public string DisplayName => Settings.DisplayName;
    public string DistinguishedName => Settings.DistinguishedName;

    /// <summary>Gets or sets the marker shown when this OU is its domain's default.</summary>
    [ObservableProperty]
    public partial string DefaultText { get; set; } = string.Empty;
}