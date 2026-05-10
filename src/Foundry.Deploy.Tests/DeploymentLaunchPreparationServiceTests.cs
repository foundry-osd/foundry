using Foundry.Deploy.Models;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentLaunchPreparationServiceTests
{
    [Fact]
    public void Prepare_WhenDryRunAndTargetDiskMissing_UsesDebugVirtualDisk()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);

        DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(selectedTargetDisk: null, isDryRun: true));

        Assert.True(result.IsReadyToStart);
        Assert.Equal(999, result.EffectiveTargetDisk?.DiskNumber);
        Assert.Equal("LAB-01", result.NormalizedComputerName);
        Assert.Equal(0, shell.ConfirmationCallCount);
    }

    [Fact]
    public void Prepare_WhenSelectedDiskIsBlocked_FailsBeforeConfirmation()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);
        TargetDiskInfo blockedDisk = CreateDisk(isSelectable: false, selectionWarning: "System disk");

        DeploymentLaunchPreparationResult result = service.Prepare(CreateRequest(selectedTargetDisk: blockedDisk));

        Assert.False(result.IsReadyToStart);
        Assert.Equal("Selected disk is blocked: System disk", result.StatusMessage);
        Assert.Equal(0, shell.ConfirmationCallCount);
    }

    [Fact]
    public void Prepare_WhenOemDriverPackSelectionHasNoPackage_FailsValidation()
    {
        var shell = new FakeApplicationShellService();
        var service = new DeploymentLaunchPreparationService(shell);

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: CreateDisk(),
                driverPackSelectionKind: DriverPackSelectionKind.OemCatalog,
                selectedDriverPack: null));

        Assert.False(result.IsReadyToStart);
        Assert.Equal("Select a valid OEM model/version before starting deployment.", result.StatusMessage);
    }

    [Fact]
    public void Prepare_WhenRequestIsValidAndConfirmed_ReturnsDeploymentContext()
    {
        var shell = new FakeApplicationShellService { ConfirmationResult = true };
        var service = new DeploymentLaunchPreparationService(shell);
        TargetDiskInfo targetDisk = CreateDisk();
        DriverPackCatalogItem driverPack = new()
        {
            Id = "pack-1",
            Manufacturer = "Dell",
            Name = "Dell 24H2",
            FileName = "pack.cab",
            DownloadUrl = "https://example.test/pack.cab",
            OsName = "Windows 11",
            OsReleaseId = "24H2",
            OsArchitecture = "x64"
        };
        AutopilotProfileCatalogItem autopilotProfile = new()
        {
            FolderName = "profile",
            DisplayName = "Corporate Profile",
            ConfigurationFilePath = @"C:\Autopilot\profile.json"
        };

        DeploymentLaunchPreparationResult result = service.Prepare(
            CreateRequest(
                selectedTargetDisk: targetDisk,
                targetComputerName: " LAB_01 ",
                driverPackSelectionKind: DriverPackSelectionKind.OemCatalog,
                selectedDriverPack: driverPack,
                isAutopilotEnabled: true,
                selectedAutopilotProfile: autopilotProfile));

        Assert.True(result.IsReadyToStart);
        Assert.Equal("LAB01", result.NormalizedComputerName);
        Assert.Equal(1, shell.ConfirmationCallCount);
        Assert.Equal(targetDisk.DiskNumber, result.Context?.TargetDiskNumber);
        Assert.Equal("LAB01", result.Context?.TargetComputerName);
        Assert.Same(driverPack, result.Context?.DriverPack);
        Assert.Same(autopilotProfile, result.Context?.SelectedAutopilotProfile);
    }

    private static DeploymentLaunchRequest CreateRequest(
        TargetDiskInfo? selectedTargetDisk,
        string targetComputerName = "LAB-01",
        DriverPackSelectionKind driverPackSelectionKind = DriverPackSelectionKind.None,
        DriverPackCatalogItem? selectedDriverPack = null,
        bool isAutopilotEnabled = false,
        AutopilotProfileCatalogItem? selectedAutopilotProfile = null,
        bool isDryRun = false)
    {
        return new DeploymentLaunchRequest
        {
            Mode = DeploymentMode.Usb,
            CacheRootPath = @"X:\Foundry\Runtime",
            TargetComputerName = targetComputerName,
            SelectedTargetDisk = selectedTargetDisk,
            SelectedOperatingSystem = new OperatingSystemCatalogItem
            {
                WindowsRelease = "11",
                ReleaseId = "24H2",
                Architecture = "x64",
                LanguageCode = "en-US",
                Language = "English",
                Edition = "Professional",
                LicenseChannel = "Retail",
                Build = "26100"
            },
            DriverPackSelectionKind = driverPackSelectionKind,
            SelectedDriverPack = selectedDriverPack,
            ApplyFirmwareUpdates = false,
            IsAutopilotEnabled = isAutopilotEnabled,
            SelectedAutopilotProfile = selectedAutopilotProfile,
            IsDryRun = isDryRun
        };
    }

    private static TargetDiskInfo CreateDisk(bool isSelectable = true, string selectionWarning = "")
    {
        return new TargetDiskInfo
        {
            DiskNumber = 3,
            FriendlyName = "NVMe Disk",
            BusType = "NVMe",
            SizeBytes = 256UL * 1024UL * 1024UL * 1024UL,
            IsSelectable = isSelectable,
            SelectionWarning = selectionWarning
        };
    }

    private sealed class FakeApplicationShellService : IApplicationShellService
    {
        public bool ConfirmationResult { get; init; } = true;

        public int ConfirmationCallCount { get; private set; }

        public void ShowAbout()
        {
        }

        public bool ConfirmWarning(string title, string message)
        {
            ConfirmationCallCount++;
            return ConfirmationResult;
        }
    }
}
