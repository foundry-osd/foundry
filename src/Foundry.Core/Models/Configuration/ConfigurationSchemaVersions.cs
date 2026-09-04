// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

public static class ConfigurationSchemaVersions
{
    public const int FoundryCurrent = 15;

    public const int ConnectCurrent = 4;

    public const int DeployCurrent = 13;

    public static bool IsBootMediaUpdateRecommended(int schemaVersion, int currentSchemaVersion)
    {
        return schemaVersion < currentSchemaVersion;
    }
}
