// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Tells whether a Domain Join deployment will end on the Windows page that asks who will use the device. Windows
/// client editions only skip that page when the answer file creates a local account, and membership of a domain
/// does not replace it. The condition is advisory: the join itself works without a local account.
/// </summary>
public static class DomainJoinLocalAccountAdvisory
{
    /// <summary>Gets whether Domain Join is enabled and the answer file Foundry generates creates no local account.</summary>
    public static bool IsLocalAccountMissing(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.DomainJoin.IsEnabled) return false;
        // A custom answer file used by default owns its accounts; Foundry then writes none of its OOBE settings.
        if (configuration.Unattend is { IsEnabled: true, DefaultFileId: not null }) return false;
        OobeSettings oobe = configuration.Customization.Oobe;
        return !oobe.IsEnabled || !(oobe.EnableAdministratorAccount || oobe.AdditionalAccounts.Count > 0);
    }
}
