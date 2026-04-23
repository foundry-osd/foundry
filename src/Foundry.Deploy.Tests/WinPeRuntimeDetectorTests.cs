using Foundry.Deploy.Services.Runtime;

namespace Foundry.Deploy.Tests;

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

    [Fact]
    public void IsWinPeRuntime_WhenSystemDriveIsX_ReturnsTrue()
    {
        bool isWinPe = WinPeRuntimeDetector.IsWinPeRuntime(
            systemDrive: "X:",
            windowsDirectory: @"C:\Windows",
            registryKeyExists: _ => false);

        Assert.True(isWinPe);
    }

    [Fact]
    public void IsWinPeRuntime_WhenWindowsDirectoryIsOnXDrive_ReturnsTrue()
    {
        bool isWinPe = WinPeRuntimeDetector.IsWinPeRuntime(
            systemDrive: "C:",
            windowsDirectory: @"X:\Windows",
            registryKeyExists: _ => false);

        Assert.True(isWinPe);
    }

    [Fact]
    public void IsWinPeRuntime_WhenNoMarkerExists_ReturnsFalse()
    {
        bool isWinPe = WinPeRuntimeDetector.IsWinPeRuntime(
            systemDrive: "C:",
            windowsDirectory: @"C:\Windows",
            registryKeyExists: _ => false);

        Assert.False(isWinPe);
    }
}
