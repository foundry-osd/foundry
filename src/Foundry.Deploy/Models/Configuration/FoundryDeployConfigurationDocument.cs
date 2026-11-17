// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Telemetry;

namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// The Deploy view of <c>foundry.winpe.config.json</c>: the shared root sections (schema, authoring version, telemetry,
/// protection, network) and the properties of its <c>deploy</c> section, flattened by
/// <see cref="Foundry.Core.Services.Configuration.WinPeConfigurationReader.ReadModeView"/>.
/// </summary>
public sealed record FoundryDeployConfigurationDocument
{
    /// <summary>Gets runtime domain metadata and preparation-only encrypted credentials.</summary>
    public Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings DomainJoin { get; init; } = new();
    /// <summary>Gets the SMB shares used as a shared download cache and the IP scopes that select them.</summary>
    public Foundry.Core.Models.Configuration.Deploy.DeployNetworkCacheSettings NetworkCache { get; init; } = new();
    /// <summary>Gets ordered post-installation actions and their authenticated media binding.</summary>
    public Foundry.Core.Models.Configuration.Deploy.DeployPreOobeSettings PreOobe { get; init; } = new();

    /// <summary>
    /// Gets the schema version of the WinPE runtime configuration.
    /// </summary>
    public int SchemaVersion { get; init; } = ConfigurationSchemaVersions.WinPeCurrent;

    /// <summary>Gets the OSD release that authored this media, when recorded.</summary>
    public string? AuthoringVersion { get; init; }

    /// <summary>
    /// Gets the protected answer-file manifest without authoring paths or XML content.
    /// </summary>
    public Foundry.Core.Models.Configuration.Deploy.DeployUnattendSettings Unattend { get; init; } = new();

    /// <summary>Gets deployment media access-protection settings.</summary>
    public DeployProtectionSettings Protection { get; init; } = new();

    /// <summary>
    /// Gets successful deployment completion behavior.
    /// </summary>
    public DeployCompletionSettings Completion { get; init; } = new();

    /// <summary>
    /// Gets Windows catalog selection settings used during apply image selection.
    /// </summary>
    public DeployOperatingSystemSelectionSettings OperatingSystemSelection { get; init; } = new();

    /// <summary>Gets custom source defaults bound to external media content.</summary>
    public Foundry.Core.Models.Configuration.Deploy.DeployCustomImagesSettings CustomImages { get; init; } = new();

    /// <summary>Gets the offered custom driver sets and external media manifest binding.</summary>
    public Foundry.Core.Models.Configuration.Deploy.DeployCustomDriversSettings CustomDrivers { get; init; } = new();

    /// <summary>
    /// Gets the network profile roaming shared with Connect: the transports to import and the captured artifact directory.
    /// </summary>
    public WinPeNetworkSettings Network { get; init; } = new();

    /// <summary>
    /// Gets Windows customization settings applied during deployment.
    /// </summary>
    public DeployCustomizationSettings Customization { get; init; } = new();

    /// <summary>
    /// Gets Autopilot provisioning settings.
    /// </summary>
    public DeployAutopilotSettings Autopilot { get; init; } = new();

    /// <summary>
    /// Gets telemetry policy and runtime settings for Foundry.Deploy; disabled when the file carries none.
    /// </summary>
    public TelemetrySettings Telemetry { get; init; } = TelemetrySettings.Disabled;
}
