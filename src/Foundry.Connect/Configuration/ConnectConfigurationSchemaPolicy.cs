// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Describes how Foundry.Connect treats the schema version of a loaded runtime configuration.
/// </summary>
public enum ConnectSchemaDecision
{
    /// <summary>The schema version matches the version this runtime was built for.</summary>
    Current,

    /// <summary>The schema version predates this runtime; missing newer fields take their defaults.</summary>
    Older,

    /// <summary>The schema version was authored by a newer release; the runtime loads it and logs a warning.</summary>
    Newer,

    /// <summary>The schema version is not a valid version number, so the configuration is rejected.</summary>
    Invalid
}

/// <summary>
/// Classifies the schema version of a Foundry.Connect runtime configuration document.
/// </summary>
public static class ConnectConfigurationSchemaPolicy
{
    /// <summary>
    /// Classifies a schema version against <see cref="FoundryConnectConfigurationDocument.CurrentSchemaVersion"/>.
    /// </summary>
    /// <param name="schemaVersion">The schema version read from the configuration file.</param>
    /// <returns>
    /// <see cref="ConnectSchemaDecision.Invalid"/> for zero or negative versions, otherwise how the version
    /// compares with the current one.
    /// </returns>
    public static ConnectSchemaDecision Evaluate(int schemaVersion)
    {
        if (schemaVersion <= 0)
        {
            return ConnectSchemaDecision.Invalid;
        }

        return schemaVersion.CompareTo(FoundryConnectConfigurationDocument.CurrentSchemaVersion) switch
        {
            < 0 => ConnectSchemaDecision.Older,
            0 => ConnectSchemaDecision.Current,
            _ => ConnectSchemaDecision.Newer
        };
    }
}
