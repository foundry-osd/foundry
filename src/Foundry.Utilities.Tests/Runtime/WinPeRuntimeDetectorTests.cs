// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Runtime;

namespace Foundry.Utilities.Tests.Runtime;

public sealed class WinPeRuntimeDetectorTests
{
    private const string WinPeVersionRegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WinPE";
    private const string MiniNtRegistryKey = @"SYSTEM\CurrentControlSet\Control\MiniNT";

    [Theory]
    [InlineData(WinPeVersionRegistryKey)]
    [InlineData(MiniNtRegistryKey)]
    public void IsWinPeRuntime_WhenRegistryMarkerExists_ReturnsTrue(string registryMarker)
    {
        bool isWinPe = WinPeRuntimeDetector.IsWinPeRuntime(
            systemDrive: "C:",
            windowsDirectory: @"C:\Windows",
            registryKeyExists: key => string.Equals(key, registryMarker, StringComparison.OrdinalIgnoreCase));

        Assert.True(isWinPe);
    }

    [Theory]
    [InlineData("X:", @"C:\Windows")]
    [InlineData("x:", @"C:\Windows")]
    [InlineData("C:", @"X:\Windows")]
    [InlineData("C:", @"x:\Windows")]
    public void IsWinPeRuntime_WhenXDriveMarkerExists_ReturnsTrue(string systemDrive, string windowsDirectory)
    {
        bool isWinPe = WinPeRuntimeDetector.IsWinPeRuntime(
            systemDrive,
            windowsDirectory,
            registryKeyExists: _ => false);

        Assert.True(isWinPe);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData("C:", @"C:\Windows")]
    public void IsWinPeRuntime_WhenNoMarkerExists_ReturnsFalse(string? systemDrive, string? windowsDirectory)
    {
        bool isWinPe = WinPeRuntimeDetector.IsWinPeRuntime(
            systemDrive,
            windowsDirectory,
            registryKeyExists: _ => false);

        Assert.False(isWinPe);
    }
}
