namespace Foundry.Core.Models.Configuration;

public static class ConfigurationSchemaVersions
{
    public const int FoundryCurrent = 11;

    public const int ConnectCurrent = 2;

    public const int DeployCurrent = 9;

    public static bool IsBootMediaUpdateRecommended(int schemaVersion, int currentSchemaVersion)
    {
        return schemaVersion < currentSchemaVersion;
    }
}
