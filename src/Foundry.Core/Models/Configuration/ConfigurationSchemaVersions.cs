// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

public static class ConfigurationSchemaVersions
{
    public const int FoundryCurrent = 19;

    /// <summary>Gets the schema of <c>foundry.winpe.config.json</c>, the one runtime configuration of a boot media.</summary>
    public const int WinPeCurrent = 1;
}
