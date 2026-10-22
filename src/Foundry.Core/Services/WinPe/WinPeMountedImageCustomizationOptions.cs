// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes all inputs required to customize a mounted WinPE or WinRE boot image.
/// </summary>
public sealed record WinPeMountedImageCustomizationOptions
{
    /// <summary>
    /// Gets the WinPE build artifact containing workspace paths.
    /// </summary>
    public WinPeBuildArtifact? Artifact { get; init; }

    /// <summary>
    /// Gets ADK tool paths used during customization.
    /// </summary>
    public WinPeToolPaths? Tools { get; init; }

    /// <summary>
    /// Gets the prepared boot image: its source decides the WinRE startup fixes, and its staged files are added to System32.
    /// </summary>
    public WinPeBootImagePreparationResult? BootImagePreparation { get; init; }

    /// <summary>
    /// Gets the WinPE language pack to apply.
    /// </summary>
    public string WinPeLanguage { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional keyboard layout applied to the boot image; null follows the WinPE language default.
    /// </summary>
    public KeyboardLayout? WinPeKeyboardLayout { get; init; }

    /// <summary>
    /// Gets driver package directories to inject.
    /// </summary>
    public IReadOnlyList<string> DriverPackagePaths { get; init; } = [];

    /// <summary>
    /// Gets optional asset provisioning settings.
    /// </summary>
    public WinPeMountedImageAssetProvisioningOptions? AssetProvisioning { get; init; }

    /// <summary>
    /// Gets optional runtime payload provisioning settings for Foundry.Connect and Foundry.Deploy.
    /// </summary>
    public WinPeRuntimePayloadProvisioningOptions? RuntimePayloadProvisioning { get; init; }

    /// <summary>
    /// Gets download progress callbacks for Windows source and driver artifacts.
    /// </summary>
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }

    /// <summary>
    /// Gets customization progress callbacks.
    /// </summary>
    public IProgress<WinPeMountedImageCustomizationProgress>? Progress { get; init; }
}
