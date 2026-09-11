// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Bootstrap;

/// <summary>Immutable media selection and diagnostic context shared by boot stages.</summary>
internal sealed record BootstrapContext(string WinPeRoot, string RuntimeRoot, string RuntimeIdentifier,
    string SessionId, string? PersistenceDirectory, bool ConnectIsDebug, bool DeployIsDebug)
{
    internal bool IsUsb => PersistenceDirectory is not null;

    internal string ConnectConfigurationPath => Path.Combine(WinPeRoot, "Config", "foundry.connect.config.json");

    /// <summary>Clears stale ISO persistence while preserving one session across all launched applications.</summary>
    internal IReadOnlyDictionary<string, string?> ChildEnvironment => new Dictionary<string, string?>
    {
        [DiagnosticSessionContext.EnvironmentVariableName] = SessionId,
        [DiagnosticClock.EnvironmentVariableName] = DiagnosticClock.Current.IsSynchronized == true ? "true" : "false",
        [DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName] = PersistenceDirectory,
        ["FOUNDRY_DEPLOYMENT_MODE"] = IsUsb ? "Usb" : "Iso"
    };
}
