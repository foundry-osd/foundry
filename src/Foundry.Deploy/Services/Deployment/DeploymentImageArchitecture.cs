// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Deploy.Services.Deployment.Steps;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Rejects a Windows image whose processor architecture differs from the running WinPE.
/// </summary>
/// <remarks>
/// WinPE only boots on hardware of its own architecture, so the WinPE architecture is the device architecture.
/// A mismatched image cannot be made bootable: its own <c>bcdboot.exe</c> cannot run in WinPE, and its kernel cannot
/// start on the device. Image architectures come from DISM (<c>x86</c>, <c>x64</c>, <c>arm64</c>, <c>unknown</c>) and
/// catalog architectures are already normalized to <c>x64</c> or <c>arm64</c>.
/// </remarks>
internal static class DeploymentImageArchitecture
{
    /// <summary>Maps a processor architecture to the lowercase name DISM and the catalog use (x64, arm64).</summary>
    public static string ToArchitectureName(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        _ => architecture.ToString().ToLowerInvariant()
    };

    /// <summary>Returns whether an image architecture is known and equals the expected architecture.</summary>
    public static bool Matches(string? imageArchitecture, string expectedArchitecture) =>
        !string.IsNullOrWhiteSpace(imageArchitecture) &&
        string.Equals(imageArchitecture.Trim(), expectedArchitecture.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Throws a preflight guard failure when the image cannot run on the device.</summary>
    /// <exception cref="DeploymentOperationException">The image architecture is unknown or differs from the device.</exception>
    public static void EnsureMatchesDevice(string? imageArchitecture, Architecture deviceArchitecture)
    {
        string device = ToArchitectureName(deviceArchitecture);
        if (!Matches(imageArchitecture, device)) throw Mismatch(imageArchitecture, "DeviceArchitecture", device);
    }

    /// <summary>
    /// Throws a preflight guard failure when the image DISM reports differs from the architecture that was selected,
    /// so a mislabeled catalog entry is never applied.
    /// </summary>
    /// <exception cref="DeploymentOperationException">The image architecture is unknown or differs from the selection.</exception>
    public static void EnsureMatchesSelection(string? imageArchitecture, string selectedArchitecture)
    {
        if (!Matches(imageArchitecture, selectedArchitecture)) throw Mismatch(imageArchitecture, "SelectedArchitecture", selectedArchitecture);
    }

    private static DeploymentOperationException Mismatch(string? imageArchitecture, string expectedKind, string expected)
    {
        Serilog.Log.ForContext(typeof(DeploymentImageArchitecture)).Error(
            "Windows image architecture does not match. ImageArchitecture={ImageArchitecture}, ExpectedKind={ExpectedKind}, ExpectedArchitecture={ExpectedArchitecture}",
            string.IsNullOrWhiteSpace(imageArchitecture) ? "unknown" : imageArchitecture, expectedKind, expected);
        return PreflightDeploymentStep.Guard("Preflight.ArchitectureMismatch", "image_architecture_mismatch");
    }
}
