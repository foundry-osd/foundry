// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Represents the <c>deploy</c> section of <see cref="FoundryWinPeConfigurationDocument"/>: the settings Foundry.Deploy applies
/// to Windows. Media preparation binds the content manifests of this section, and an active network cache moves its content
/// sections into the signed index.
/// </summary>
public sealed record DeployRuntimeSettings
{
    /// <summary>Gets successful deployment completion behavior.</summary>
    public DeployCompletionSettings Completion { get; init; } = new();

    /// <summary>Gets Windows catalog selection settings used during apply image selection.</summary>
    public DeployOperatingSystemSelectionSettings OperatingSystemSelection { get; init; } = new();

    /// <summary>Gets custom image defaults and external media manifest binding.</summary>
    public DeployCustomImagesSettings CustomImages { get; init; } = new();

    /// <summary>Gets the offered custom driver sets and external media manifest binding.</summary>
    public DeployCustomDriversSettings CustomDrivers { get; init; } = new();

    /// <summary>Gets post-installation actions bound to external media content.</summary>
    public DeployPreOobeSettings PreOobe { get; init; } = new();

    /// <summary>Gets localization settings, including the default time zone the guardian applies before deployment starts.</summary>
    public DeployLocalizationSettings Localization { get; init; } = new();

    /// <summary>Gets Windows customization settings applied during deployment.</summary>
    public DeployCustomizationSettings Customization { get; init; } = new();

    /// <summary>Gets the manifest of protected custom answer-file choices.</summary>
    public DeployUnattendSettings Unattend { get; init; } = new();

    /// <summary>Gets Autopilot provisioning settings.</summary>
    public DeployAutopilotSettings Autopilot { get; init; } = new();

    /// <summary>Gets domain metadata and any protected automatic credentials consumed during deployment.</summary>
    public DeployDomainJoinSettings DomainJoin { get; init; } = new();

    /// <summary>Gets the SMB shares used as a shared download cache and the IP scopes that select them.</summary>
    public DeployNetworkCacheSettings NetworkCache { get; init; } = new();
}
