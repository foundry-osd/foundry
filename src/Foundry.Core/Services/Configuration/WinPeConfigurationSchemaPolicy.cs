// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Describes how a WinPE process treats the schema version of <c>foundry.winpe.config.json</c>.</summary>
public enum WinPeConfigurationSchemaDecision
{
    /// <summary>The schema version matches the version this runtime was built for.</summary>
    Current,

    /// <summary>The schema version was authored by a newer release; the runtime loads it and logs a warning.</summary>
    Newer,

    /// <summary>The schema version is not a valid version number, so the configuration is rejected.</summary>
    Invalid
}

/// <summary>
/// Classifies the schema version of <c>foundry.winpe.config.json</c> the same way for the guardian, Connect and Deploy.
/// </summary>
public static class WinPeConfigurationSchemaPolicy
{
    /// <summary>Classifies <paramref name="schemaVersion"/> against <see cref="ConfigurationSchemaVersions.WinPeCurrent"/>.</summary>
    /// <param name="schemaVersion">The schema version read from the configuration file.</param>
    /// <returns>
    /// <see cref="WinPeConfigurationSchemaDecision.Invalid"/> for zero or negative versions,
    /// <see cref="WinPeConfigurationSchemaDecision.Newer"/> above the current version, otherwise
    /// <see cref="WinPeConfigurationSchemaDecision.Current"/>.
    /// </returns>
    public static WinPeConfigurationSchemaDecision Evaluate(int schemaVersion) => schemaVersion switch
    {
        <= 0 => WinPeConfigurationSchemaDecision.Invalid,
        > ConfigurationSchemaVersions.WinPeCurrent => WinPeConfigurationSchemaDecision.Newer,
        _ => WinPeConfigurationSchemaDecision.Current
    };
}
