// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Deploy.Services.Runtime;

/// <summary>
/// Selects the in-memory Domain Join scenario used by Foundry.Deploy debug safe mode.
/// </summary>
public enum DebugDomainJoinMode
{
    /// <summary>
    /// Uses the media configuration without a debug Domain Join scenario.
    /// </summary>
    None,

    /// <summary>
    /// Shows the wizard step as Interactive media would: domain, account, password and OU choice.
    /// </summary>
    Interactive,

    /// <summary>
    /// Shows the wizard step as Zero-touch media with technician OU choice would: a fixed domain and the OU list.
    /// </summary>
    ZeroTouch
}

/// <summary>
/// Builds sample Domain Join configurations for debug safe mode. They carry no credentials, because a dry run
/// never decrypts or stages any.
/// </summary>
public static class DebugDomainJoinScenarios
{
    private const string Domain = "corp.contoso.com";

    public static DeployDomainJoinSettings Create(DebugDomainJoinMode mode) => mode switch
    {
        DebugDomainJoinMode.Interactive => CreateEnabled(DomainJoinMode.Interactive, accountName: null),
        DebugDomainJoinMode.ZeroTouch => CreateEnabled(DomainJoinMode.Automatic, accountName: @"CORP\svc-domainjoin"),
        _ => new()
    };

    private static DeployDomainJoinSettings CreateEnabled(DomainJoinMode mode, string? accountName) => new()
    {
        IsEnabled = true,
        Mode = mode,
        DomainName = Domain,
        AccountName = accountName,
        OuCatalogDomain = Domain,
        OrganizationalUnits =
        [
            new() { Id = "workstations", DisplayName = "Workstations", DistinguishedName = "OU=Workstations,DC=corp,DC=contoso,DC=com" },
            new() { Id = "laptops", DisplayName = "Laptops", DistinguishedName = "OU=Laptops,OU=Workstations,DC=corp,DC=contoso,DC=com" },
            new() { Id = "kiosks", DisplayName = "Kiosks", DistinguishedName = "OU=Kiosks,DC=corp,DC=contoso,DC=com" }
        ],
        DefaultOuId = "workstations",
        AllowOuSelectionDuringDeployment = true
    };
}
