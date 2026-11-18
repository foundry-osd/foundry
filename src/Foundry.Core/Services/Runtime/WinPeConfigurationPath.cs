// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// Locates <c>foundry.winpe.config.json</c> for the guardian and the Connect and Deploy phases of the UI process: the
/// <see cref="OverrideEnvironmentVariable"/> full path when it is set, otherwise the file of the booted media.
/// </summary>
public static class WinPeConfigurationPath
{
    /// <summary>The environment variable that replaces the configuration path with a full path, for development.</summary>
    public const string OverrideEnvironmentVariable = "FOUNDRY_WINPE_CONFIG";

    /// <summary>Resolves the configuration path against <see cref="WinPeWorkspaceLayout.ConfigurationPath"/>.</summary>
    public static WinPeConfigurationLocation Resolve() => Resolve(WinPeWorkspaceLayout.ConfigurationPath);

    /// <summary>Resolves the configuration path against <paramref name="defaultPath"/>.</summary>
    /// <param name="defaultPath">The configuration file used when the override is not set.</param>
    public static WinPeConfigurationLocation Resolve(string defaultPath) =>
        Resolve(defaultPath, Environment.GetEnvironmentVariable(OverrideEnvironmentVariable));

    /// <summary>Resolves the configuration path against <paramref name="defaultPath"/> and an already read override.</summary>
    /// <param name="defaultPath">The configuration file used when <paramref name="overridePath"/> is empty.</param>
    /// <param name="overridePath">The value of <see cref="OverrideEnvironmentVariable"/>, or <see langword="null"/> when unset.</param>
    public static WinPeConfigurationLocation Resolve(string defaultPath, string? overridePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);
        return string.IsNullOrWhiteSpace(overridePath)
            ? new WinPeConfigurationLocation(defaultPath, IsOverride: false)
            : new WinPeConfigurationLocation(overridePath, IsOverride: true);
    }
}

/// <summary>A resolved <c>foundry.winpe.config.json</c> location.</summary>
/// <param name="Path">The configuration file path; an override is used as given and is expected to be a full path.</param>
/// <param name="IsOverride">Whether the path comes from <see cref="WinPeConfigurationPath.OverrideEnvironmentVariable"/>.</param>
public readonly record struct WinPeConfigurationLocation(string Path, bool IsOverride);
