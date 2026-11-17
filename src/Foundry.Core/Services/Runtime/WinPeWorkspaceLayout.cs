// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// Describes the fixed Foundry workspace layout inside a booted WinPE image.
/// </summary>
public static class WinPeWorkspaceLayout
{
    /// <summary>Root of the Foundry workspace on the WinPE RAM drive.</summary>
    public const string Root = @"X:\Foundry";

    /// <summary>Directory holding generated runtime configuration files.</summary>
    public const string ConfigDirectory = Root + @"\Config";

    /// <summary>Directory holding runtime log files.</summary>
    public const string LogsDirectory = Root + @"\Logs";

    /// <summary>Directory holding runtime artifacts shared between boot-media applications.</summary>
    public const string RuntimeDirectory = Root + @"\Runtime";

    /// <summary>File name of the one runtime configuration of a boot media, under <see cref="ConfigDirectory"/>.</summary>
    public const string ConfigurationFileName = "foundry.winpe.config.json";

    /// <summary>File name of the marker that records whether the WinPE runtime came from a debug or a release build.</summary>
    public const string ProvisioningSourceFileName = "foundry.winpe.provisioning-source.txt";

    /// <summary>Full path of the runtime configuration inside a booted WinPE image.</summary>
    public const string ConfigurationPath = ConfigDirectory + @"\" + ConfigurationFileName;

    /// <summary>Full path of the provisioning-source marker inside a booted WinPE image.</summary>
    public const string ProvisioningSourcePath = ConfigDirectory + @"\" + ProvisioningSourceFileName;

    /// <summary>
    /// Name of the directory, under <see cref="Root"/>, holding the Foundry.PostInstall launch files: the single-file
    /// executable, <c>Launch.cmd</c> and the runtime manifest. Every boot media carries it.
    /// </summary>
    public const string PostInstallDirectoryName = "PostInstall";

    /// <summary>Full path of the Foundry.PostInstall launch files inside a booted WinPE image.</summary>
    public const string PostInstallDirectory = Root + @"\" + PostInstallDirectoryName;

    /// <summary>Name of the directory, under <see cref="Root"/>, holding the debug Foundry.AutopilotInteractive archive.</summary>
    public const string AutopilotInteractiveDirectoryName = "AutopilotInteractive";

    /// <summary>File name of the debug Foundry.AutopilotInteractive archive under <see cref="AutopilotInteractiveDirectoryName"/>.</summary>
    public const string AutopilotInteractiveArchiveFileName = "Foundry.AutopilotInteractive.zip";

    /// <summary>
    /// Full path of the Foundry.AutopilotInteractive archive inside a booted WinPE image. Only debug media carry it; the
    /// read-only boot image is its trust root, so Foundry Deploy reads it only when the provisioning-source marker says debug.
    /// </summary>
    public const string AutopilotInteractiveArchivePath =
        Root + @"\" + AutopilotInteractiveDirectoryName + @"\" + AutopilotInteractiveArchiveFileName;
}
