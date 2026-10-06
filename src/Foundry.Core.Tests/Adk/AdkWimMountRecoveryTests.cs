// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Core.Services.Adk;

namespace Foundry.Core.Tests.Adk;

public sealed class AdkWimMountRecoveryTests
{
    private const string KitsRoot = @"C:\Kits\10";

    [Theory]
    [InlineData("installer_exit_failed", AdkWimMountRecovery.PayloadHashMismatchExitCode, true)]
    [InlineData("installer_exit_failed", 1618, false)]
    [InlineData("launch_failed", AdkWimMountRecovery.PayloadHashMismatchExitCode, false)]
    public void CanRecover_MatchesOnlyUnverifiablePayloadExit(string reason, int exitCode, bool expected)
    {
        AdkSetupException failure = new(reason, "adksetup.exe", "setup.log", exitCode: exitCode);

        Assert.Equal(expected, AdkWimMountRecovery.CanRecover(failure));
    }

    [Theory]
    [InlineData(Architecture.X64, @"amd64\DISM\WimMountAdkSetupAmd64.exe")]
    [InlineData(Architecture.Arm64, @"arm64\DISM\WimMountAdkSetupArm64.exe")]
    public void FindDriverSetupPath_SelectsDriverSetupForOperatingSystemArchitecture(Architecture architecture, string relativePath)
    {
        string expected = Path.Combine(KitsRoot, AdkInstallationDetector.DeploymentToolsRelativePath, relativePath);
        AdkWimMountRecovery recovery = new(new FakeProbe(KitsRoot, expected), new FakeMarker(null), architecture);

        Assert.Equal(expected, recovery.FindDriverSetupPath());
    }

    [Fact]
    public void FindDriverSetupPath_ReturnsNullWhenDriverSetupIsNotInstalled()
    {
        Assert.Null(new AdkWimMountRecovery(new FakeProbe(KitsRoot), new FakeMarker(null), Architecture.X64).FindDriverSetupPath());
        Assert.Null(new AdkWimMountRecovery(new FakeProbe(null), new FakeMarker(null), Architecture.X64).FindDriverSetupPath());
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    public void ClearReleasedMarker_DeletesOnlyMarkerOfReleasedDriver(int? value, bool expectedReleased, bool expectedDeleted)
    {
        FakeMarker marker = new(value);
        AdkWimMountRecovery recovery = new(new FakeProbe(KitsRoot), marker, Architecture.X64);

        Assert.Equal(expectedReleased, recovery.ClearReleasedMarker());
        Assert.Equal(expectedDeleted, marker.Deleted);
    }

    private sealed class FakeMarker(int? value) : IAdkWimMountMarker
    {
        public bool Deleted { get; private set; }
        public int? GetValue() => value;
        public void Delete() => Deleted = true;
    }

    private sealed class FakeProbe(string? kitsRootPath, params string[] files) : IAdkInstallationProbe
    {
        public string? GetKitsRootPath() => kitsRootPath;
        public bool DirectoryExists(string path) => false;
        public bool FileExists(string path) => files.Contains(path, StringComparer.OrdinalIgnoreCase);
        public bool DirectoryContainsFile(string directoryPath, string fileName) => false;
        public IReadOnlyList<AdkInstalledProduct> GetInstalledProducts() => [];
    }
}
