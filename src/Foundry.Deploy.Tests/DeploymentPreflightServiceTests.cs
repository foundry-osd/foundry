// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Deployment.Preflight;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPreflightServiceTests
{
    [Fact]
    public void Evaluate_WhenTargetMeetsRequirements_ReturnsNoFindings()
    {
        var service = new DeploymentPreflightService();

        DeploymentPreflightResult result = service.Evaluate(
            CreateReadyHardware(),
            CreateDisk(256),
            CreateOperatingSystem("x64"));

        Assert.Empty(result.Findings);
        Assert.False(result.HasBlockingFindings);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void Evaluate_WhenFirmwareIsNotUefi_ReturnsBlockingFinding()
    {
        var service = new DeploymentPreflightService();
        HardwareProfile hardware = CreateReadyHardware() with { FirmwareMode = FirmwareMode.Bios };

        DeploymentPreflightResult result = service.Evaluate(hardware, CreateDisk(256), CreateOperatingSystem("x64"));

        DeploymentPreflightFinding finding = Assert.Single(result.Findings);
        Assert.Equal(DeploymentPreflightFindingCodes.FirmwareMode, finding.Code);
        Assert.Equal(DeploymentPreflightSeverity.Blocking, finding.Severity);
    }

    [Fact]
    public void Evaluate_WhenArchitectureDoesNotMatch_ReturnsBlockingFinding()
    {
        var service = new DeploymentPreflightService();

        DeploymentPreflightResult result = service.Evaluate(
            CreateReadyHardware(),
            CreateDisk(256),
            CreateOperatingSystem("arm64"));

        DeploymentPreflightFinding finding = Assert.Single(result.Findings);
        Assert.Equal(DeploymentPreflightFindingCodes.ArchitectureMismatch, finding.Code);
        Assert.Equal(DeploymentPreflightSeverity.Blocking, finding.Severity);
        Assert.Equal(["arm64", "x64"], finding.MessageArguments);
    }

    [Fact]
    public void Evaluate_WhenTpmIsMissing_ReturnsBlockingFinding()
    {
        var service = new DeploymentPreflightService();
        HardwareProfile hardware = CreateReadyHardware() with { IsTpmPresent = false };

        DeploymentPreflightResult result = service.Evaluate(hardware, CreateDisk(256), CreateOperatingSystem("x64"));

        DeploymentPreflightFinding finding = Assert.Single(result.Findings);
        Assert.Equal(DeploymentPreflightFindingCodes.TpmMissing, finding.Code);
        Assert.Equal(DeploymentPreflightSeverity.Blocking, finding.Severity);
    }

    [Theory]
    [InlineData("1.2", true, true, DeploymentPreflightFindingCodes.TpmVersion)]
    [InlineData("2.0", false, true, DeploymentPreflightFindingCodes.TpmDisabled)]
    [InlineData("2.0", true, false, DeploymentPreflightFindingCodes.TpmDeactivated)]
    public void Evaluate_WhenTpmIsNotReady_ReturnsBlockingFinding(
        string version,
        bool isEnabled,
        bool isActivated,
        string expectedCode)
    {
        var service = new DeploymentPreflightService();
        HardwareProfile hardware = CreateReadyHardware() with
        {
            TpmSpecVersion = version,
            IsTpmEnabled = isEnabled,
            IsTpmActivated = isActivated
        };

        DeploymentPreflightResult result = service.Evaluate(hardware, CreateDisk(256), CreateOperatingSystem("x64"));

        DeploymentPreflightFinding finding = Assert.Single(result.Findings);
        Assert.Equal(expectedCode, finding.Code);
        Assert.Equal(DeploymentPreflightSeverity.Blocking, finding.Severity);
    }

    [Fact]
    public void Evaluate_WhenSecureBootIsDisabled_ReturnsWarning()
    {
        var service = new DeploymentPreflightService();
        HardwareProfile hardware = CreateReadyHardware() with { SecureBootState = SecureBootState.Disabled };

        DeploymentPreflightResult result = service.Evaluate(hardware, CreateDisk(256), CreateOperatingSystem("x64"));

        DeploymentPreflightFinding finding = Assert.Single(result.Findings);
        Assert.Equal(DeploymentPreflightFindingCodes.SecureBootDisabled, finding.Code);
        Assert.Equal(DeploymentPreflightSeverity.Warning, finding.Severity);
        Assert.False(result.HasBlockingFindings);
        Assert.True(result.HasWarnings);
    }

    [Theory]
    [InlineData(SecureBootState.Unknown)]
    [InlineData(SecureBootState.Unsupported)]
    public void Evaluate_WhenSecureBootCapabilityCannotBeVerified_ReturnsBlockingFinding(SecureBootState state)
    {
        var service = new DeploymentPreflightService();
        HardwareProfile hardware = CreateReadyHardware() with { SecureBootState = state };

        DeploymentPreflightResult result = service.Evaluate(hardware, CreateDisk(256), CreateOperatingSystem("x64"));

        DeploymentPreflightFinding finding = Assert.Single(result.Findings);
        Assert.Equal(DeploymentPreflightFindingCodes.SecureBootUnavailable, finding.Code);
        Assert.Equal(DeploymentPreflightSeverity.Blocking, finding.Severity);
    }

    [Fact]
    public void Evaluate_WhenDiskIsBelowWindowsRecommendation_ReturnsWarning()
    {
        var service = new DeploymentPreflightService();

        DeploymentPreflightResult result = service.Evaluate(
            CreateReadyHardware(),
            CreateDisk(48),
            CreateOperatingSystem("x64"));

        DeploymentPreflightFinding finding = Assert.Single(result.Findings);
        Assert.Equal(DeploymentPreflightFindingCodes.DiskBelowRecommendedSize, finding.Code);
        Assert.Equal(DeploymentPreflightSeverity.Warning, finding.Severity);
        Assert.False(result.HasBlockingFindings);
    }

    [Fact]
    public void Evaluate_WhenDiskIsFarBelowWindowsRecommendation_ReturnsWarning()
    {
        var service = new DeploymentPreflightService();

        DeploymentPreflightResult result = service.Evaluate(
            CreateReadyHardware(),
            CreateDisk(24),
            CreateOperatingSystem("x64"));

        DeploymentPreflightFinding finding = Assert.Single(result.Findings);
        Assert.Equal(DeploymentPreflightFindingCodes.DiskBelowRecommendedSize, finding.Code);
        Assert.Equal(DeploymentPreflightSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void GetUnacknowledgedWarnings_WhenExactWarningWasAcknowledged_ReturnsEmpty()
    {
        var service = new DeploymentPreflightService();
        DeploymentPreflightResult result = service.Evaluate(
            CreateReadyHardware(),
            CreateDisk(48),
            CreateOperatingSystem("x64"));

        IReadOnlyList<DeploymentPreflightFinding> unacknowledged =
            result.GetUnacknowledgedWarnings([result.Findings.Single().AcknowledgementKey]);

        Assert.Empty(unacknowledged);
    }

    [Fact]
    public void GetUnacknowledgedWarnings_WhenWarningArgumentsChanged_ReturnsWarning()
    {
        var service = new DeploymentPreflightService();
        DeploymentPreflightResult acknowledged = service.Evaluate(
            CreateReadyHardware(),
            CreateDisk(48),
            CreateOperatingSystem("x64"));
        DeploymentPreflightResult changed = service.Evaluate(
            CreateReadyHardware(),
            CreateDisk(32),
            CreateOperatingSystem("x64"));

        IReadOnlyList<DeploymentPreflightFinding> unacknowledged =
            changed.GetUnacknowledgedWarnings([acknowledged.Findings.Single().AcknowledgementKey]);

        Assert.Single(unacknowledged);
    }

    private static HardwareProfile CreateReadyHardware()
    {
        return new HardwareProfile
        {
            Architecture = "x64",
            FirmwareMode = FirmwareMode.Uefi,
            IsTpmPresent = true,
            TpmSpecVersion = "2.0, 0, 1.16",
            IsTpmEnabled = true,
            IsTpmActivated = true,
            SecureBootState = SecureBootState.Enabled
        };
    }

    private static TargetDiskInfo CreateDisk(ulong sizeGiB)
    {
        return new TargetDiskInfo
        {
            DiskNumber = 0,
            FriendlyName = "Test disk",
            SizeBytes = sizeGiB * 1024UL * 1024UL * 1024UL,
            IsSelectable = true
        };
    }

    private static OperatingSystemCatalogItem CreateOperatingSystem(string architecture)
    {
        return new OperatingSystemCatalogItem
        {
            WindowsRelease = "11",
            Architecture = architecture
        };
    }
}
