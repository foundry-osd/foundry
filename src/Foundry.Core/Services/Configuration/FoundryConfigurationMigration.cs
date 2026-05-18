using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class FoundryConfigurationMigration
{
    public static FoundryConfigurationDocument ApplyLegacyGeneralSettings(
        FoundryConfigurationDocument document,
        GeneralSettings? legacyGeneralSettings)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (legacyGeneralSettings is null)
        {
            return document;
        }

        return document with { General = legacyGeneralSettings };
    }
}
