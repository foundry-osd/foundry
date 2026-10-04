// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.ViewModels;

/// <summary>Displays a portable destination and its decoded hierarchy in an authoring or import preview row.</summary>
public sealed partial class DomainJoinOrganizationalUnitEntryViewModel : ObservableObject
{
    public DomainJoinOrganizationalUnitEntryViewModel(DomainJoinOrganizationalUnitSettings settings)
    {
        Settings = settings;
        Hierarchy = DistinguishedNameRules.TryParse(settings.DistinguishedName, out ParsedDistinguishedName parsed)
            ? string.Join(" / ", parsed.Rdns.Reverse().SelectMany(rdn => rdn.Attributes)
                .Where(attribute => !string.Equals(attribute.Type, "DC", StringComparison.OrdinalIgnoreCase)).Select(attribute => attribute.Value))
            : settings.DistinguishedName;
    }

    public DomainJoinOrganizationalUnitSettings Settings { get; }
    public string DisplayName => Settings.DisplayName;
    public string DistinguishedName => Settings.DistinguishedName;
    public string Hierarchy { get; }

    /// <summary>Marks a discovered row for an explicit import; selection alone never persists it.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
