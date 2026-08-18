// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

internal static class NetworkProfileRoamingLegacyMigration
{
    public static NetworkProfileRoamingTransportSettings Apply(
        NetworkProfileRoamingTransportSettings settings,
        bool? legacyIsEnabled,
        bool? legacyIncludePrivateKeyMaterial)
    {
        if (legacyIsEnabled is true && !settings.IsEnabledConfigured)
        {
            settings = settings with { IsEnabled = true };
        }

        if (legacyIncludePrivateKeyMaterial is true && !settings.IncludePrivateKeyMaterialConfigured)
        {
            settings = settings with { IncludePrivateKeyMaterial = true };
        }

        return settings;
    }
}
