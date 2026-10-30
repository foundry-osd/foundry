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

    /// <summary>Bootstrap configuration read by child applications for early diagnostic preferences.</summary>
    public const string BootstrapConfigurationPath = ConfigDirectory + @"\foundry.bootstrap.config.json";

    /// <summary>Location of the media secrets key, relative to the configuration directory; the key exists on older (schema 5) media only.</summary>
    public const string MediaSecretsKeyRelativePath = @"Secrets\media-secrets.key";
}
