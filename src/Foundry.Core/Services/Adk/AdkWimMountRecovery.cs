// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Foundry.Core.Services.Adk;

/// <summary>
/// Unblocks an ADK bundle uninstall that cannot re-acquire its WIM mount driver payload.
/// </summary>
/// <remarks>
/// ADK setup needs the original driver installer to remove the driver package. When that payload is gone from the
/// package cache and the publisher no longer serves the same file, setup fails its hash check and removes nothing.
/// Releasing the driver with the copy shipped in the Deployment Tools and clearing its marker makes setup detect
/// the package as absent, so the retried uninstall no longer needs the payload.
/// </remarks>
public sealed class AdkWimMountRecovery
{
    /// <summary>The setup exit code (CRYPT_E_HASH_VALUE) returned when a re-acquired payload fails verification.</summary>
    public const int PayloadHashMismatchExitCode = unchecked((int)0x80091007);

    /// <summary>The switch that makes the driver setup restore the inbox WIM mount driver.</summary>
    public const string DriverUninstallArguments = "/uninstall";

    private readonly IAdkInstallationProbe probe;
    private readonly IAdkWimMountMarker marker;
    private readonly Architecture osArchitecture;

    /// <summary>Targets the driver package and registry marker of the running operating system.</summary>
    public AdkWimMountRecovery(IAdkInstallationProbe probe)
        : this(probe, new RegistryWimMountMarker(), RuntimeInformation.OSArchitecture)
    {
    }

    /// <summary>Separates registry and platform state from the recovery policy.</summary>
    internal AdkWimMountRecovery(IAdkInstallationProbe probe, IAdkWimMountMarker marker, Architecture osArchitecture)
    {
        this.probe = probe;
        this.marker = marker;
        this.osArchitecture = osArchitecture;
    }

    /// <summary>Returns whether a failed bundle uninstall matches the unverifiable payload case this recovery handles.</summary>
    public static bool CanRecover(AdkSetupException exception)
    {
        return exception.Reason == "installer_exit_failed" && exception.ExitCode == PayloadHashMismatchExitCode;
    }

    /// <summary>Locates the driver setup installed with the Deployment Tools for the operating system architecture.</summary>
    public string? FindDriverSetupPath()
    {
        string? kitsRootPath = probe.GetKitsRootPath();
        (string Folder, string FileName)? driver = osArchitecture switch
        {
            Architecture.X64 => ("amd64", "WimMountAdkSetupAmd64.exe"),
            Architecture.Arm64 => ("arm64", "WimMountAdkSetupArm64.exe"),
            Architecture.X86 => ("x86", "WimMountAdkSetupX86.exe"),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(kitsRootPath) || driver is null)
        {
            return null;
        }

        string path = Path.Combine(
            kitsRootPath, AdkInstallationDetector.DeploymentToolsRelativePath, driver.Value.Folder, "DISM", driver.Value.FileName);
        return probe.FileExists(path) ? path : null;
    }

    /// <summary>
    /// Clears the marker left by a released driver and returns whether setup will detect the driver package as absent.
    /// </summary>
    /// <remarks>
    /// Setup tests only that the marker exists, while the driver setup resets it to zero instead of deleting it.
    /// A non-zero marker means the ADK driver is still installed and is left untouched.
    /// </remarks>
    public bool ClearReleasedMarker()
    {
        int? value = marker.GetValue();
        if (value is null)
        {
            return true;
        }

        if (value != 0)
        {
            return false;
        }

        marker.Delete();
        return true;
    }

    private sealed class RegistryWimMountMarker : IAdkWimMountMarker
    {
        private const string KeyPath = @"SOFTWARE\Microsoft\WIMMount";
        private const string ValueName = "AdkInstallation";

        public int? GetValue()
        {
            using RegistryKey? key = OpenKey(writable: false);
            return key?.GetValue(ValueName) is int value ? value : null;
        }

        public void Delete()
        {
            using RegistryKey? key = OpenKey(writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        // The driver setup writes the marker in the native registry view of the operating system.
        private static RegistryKey? OpenKey(bool writable)
        {
            using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return localMachine.OpenSubKey(KeyPath, writable);
        }
    }
}

/// <summary>Reads and clears the machine marker that ADK setup uses to detect its WIM mount driver package.</summary>
internal interface IAdkWimMountMarker
{
    /// <summary>Gets the marker value, or null when the marker does not exist.</summary>
    int? GetValue();

    /// <summary>Removes the marker; requires administrative rights.</summary>
    void Delete();
}
