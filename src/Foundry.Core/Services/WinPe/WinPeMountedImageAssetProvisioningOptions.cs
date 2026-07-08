// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeMountedImageAssetProvisioningOptions
{
    public string MountedImagePath { get; init; } = string.Empty;
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public string BootstrapScriptContent { get; init; } = string.Empty;
    public string CurlExecutableSourcePath { get; init; } = string.Empty;
    public string PSBootstrapperSourceExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the boot Unattend.xml includes a minimized, interactive
    /// PowerShell troubleshooting console (debug mode). Off by default to prevent tampering.
    /// </summary>
    public bool IncludeTroubleshootingConsole { get; init; }

    /// <summary>
    /// Gets a value indicating whether the WinPE firewall is enabled via Unattend.xml. On by default.
    /// </summary>
    public bool EnableFirewall { get; init; } = true;

    /// <summary>
    /// Gets source folders whose contents are copied into the boot image root, preserving the
    /// destination folder structure they contain (for example, a <c>Windows\System32</c> subtree).
    /// </summary>
    public IReadOnlyList<string> AdditionalRootFolderSourcePaths { get; init; } = [];
    public string SevenZipSourceDirectoryPath { get; init; } = string.Empty;
    public string IanaWindowsTimeZoneMapJson { get; init; } = string.Empty;
    public string FoundryConnectConfigurationJson { get; init; } = string.Empty;
    public string DeployConfigurationJson { get; init; } = string.Empty;
    public byte[]? MediaSecretsKey { get; init; }
    public IReadOnlyList<FoundryConnectProvisionedAssetFile> FoundryConnectAssetFiles { get; init; } = [];

    /// <summary>
    /// Gets the Autopilot mode that controls whether profile JSON or hash capture assets are staged.
    /// </summary>
    public AutopilotProvisioningMode AutopilotProvisioningMode { get; init; } = AutopilotProvisioningMode.JsonProfile;

    /// <summary>
    /// Gets the ADK OA3Tool source path copied into media when hardware hash upload mode is selected.
    /// </summary>
    public string? Oa3ToolSourcePath { get; init; }

    public IReadOnlyList<AutopilotProfileSettings> AutopilotProfiles { get; init; } = [];
    public WinPeProvisioningSource ConnectProvisioningSource { get; init; } = WinPeProvisioningSource.Release;
    public WinPeProvisioningSource DeployProvisioningSource { get; init; } = WinPeProvisioningSource.Release;
}
