// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Telemetry;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Represents <c>X:\Foundry\Config\foundry.winpe.config.json</c>, the one runtime configuration of a boot media, read by the
/// Foundry.WinPE guardian and by its Connect and Deploy modes.
/// </summary>
/// <remarks>
/// The sections every process reads (<see cref="Telemetry"/>, <see cref="Protection"/>, <see cref="Network"/>) are written once
/// at the root; <see cref="Connect"/> and <see cref="Deploy"/> keep the per-mode shapes, so media preparation binds and splits the
/// <c>deploy</c> node without touching the rest of the document.
/// </remarks>
public sealed record FoundryWinPeConfigurationDocument
{
    /// <summary>Gets the current schema version of the WinPE runtime configuration.</summary>
    public const int CurrentSchemaVersion = ConfigurationSchemaVersions.WinPeCurrent;

    /// <summary>Gets the schema version of this configuration document.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Gets the OSD release that authored this media, when recorded.</summary>
    public string? AuthoringVersion { get; init; }

    /// <summary>
    /// Gets the telemetry policy shared by the guardian, Connect and Deploy; consent is off unless authoring supplies it.
    /// </summary>
    public TelemetrySettings Telemetry { get; init; } = new()
    {
        IsEnabled = false,
        IsRemoteDiagnosticsEnabled = false
    };

    /// <summary>
    /// Gets the deployment password protection block. It is always written: an unprotected media carries a disabled block, and a
    /// protected media carries the password-wrapped deployment key that unlocks every deployment-keyed secret of the document.
    /// </summary>
    public DeployProtectionSettings Protection { get; init; } = new();

    /// <summary>Gets the network profile roaming settings shared by Connect, which captures profiles, and Deploy, which stages them.</summary>
    public WinPeNetworkSettings Network { get; init; } = new();

    /// <summary>Gets the network provisioning settings read by the Connect mode.</summary>
    public ConnectRuntimeSettings Connect { get; init; } = new();

    /// <summary>Gets the deployment settings read by the Deploy mode and the guardian's default time zone.</summary>
    public DeployRuntimeSettings Deploy { get; init; } = new();
}
