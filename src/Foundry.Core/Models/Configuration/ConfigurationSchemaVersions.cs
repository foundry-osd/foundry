namespace Foundry.Core.Models.Configuration;

public static class ConfigurationSchemaVersions
{
    public const int FoundryCurrent = 10;

    public const int ConnectCurrent = 2;

    public const int DeployCurrent = 8;

    public static bool IsBootMediaUpdateRecommended(int schemaVersion, int currentSchemaVersion)
    {
        return schemaVersion < currentSchemaVersion;
    }
}
