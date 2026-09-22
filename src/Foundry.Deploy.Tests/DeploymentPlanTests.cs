// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPlanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_OrdersImagePreparationAroundActualStorageRoute(bool usesTargetStorage)
    {
        string[] imageSteps = usesTargetStorage
            ? [DeploymentStepNames.PrepareTargetDiskLayout, DeploymentStepNames.DownloadOperatingSystemImage,
                DeploymentStepNames.CheckWindowsImage, DeploymentStepNames.ApplyOperatingSystemImage, DeploymentStepNames.ConfigureWindowsBoot]
            : [DeploymentStepNames.DownloadOperatingSystemImage, DeploymentStepNames.CheckWindowsImage,
                DeploymentStepNames.PrepareTargetDiskLayout, DeploymentStepNames.ApplyOperatingSystemImage, DeploymentStepNames.ConfigureWindowsBoot];

        string[] actual = DeploymentPlan.Build(Request(), usesTargetStorage: usesTargetStorage)
            .Select(step => step.Name).Where(name => imageSteps.Contains(name)).ToArray();

        Assert.Equal(imageSteps, actual);
    }

    [Fact]
    public void Build_DeferredInstaller_OmitsInfOperationsButRetainsSetupTasks()
    {
        DeploymentContext request = Request() with
        {
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem { Manufacturer = "Lenovo", FileName = "drivers.exe" }
        };
        string[] names = DeploymentPlan.Build(request).Select(step => step.Name).ToArray();

        Assert.Contains(DeploymentStepNames.DownloadDriverPack, names);
        Assert.Contains(DeploymentStepNames.StageDriverInstaller, names);
        Assert.Contains(DeploymentStepNames.StagePreOobeCustomization, names);
        Assert.DoesNotContain(DeploymentStepNames.ExtractDriverPack, names);
        Assert.DoesNotContain(DeploymentStepNames.ApplyDriverPack, names);
        Assert.DoesNotContain(DeploymentStepNames.ApplyRecoveryDrivers, names);
    }

    [Fact]
    public void Build_CustomAnswerFile_PreservesIndependentAiAndFeatures()
    {
        DeploymentContext request = Request() with
        {
            Unattend = new UnattendSelection(new Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile(), "answer.enc"),
            Oobe = new DeployOobeSettings { IsEnabled = true },
            AiComponentRemoval = new DeployAiComponentRemovalSettings { IsEnabled = true, DisableRecall = true },
            WindowsOptionalFeatures = new DeployWindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                Actions = [new DeployWindowsOptionalFeatureAction { Id = "wf:telnetclient", Enable = true }]
            }
        };
        string[] names = DeploymentPlan.Build(request).Select(step => step.Name).ToArray();

        Assert.Contains(DeploymentStepNames.ValidateCustomUnattend, names);
        Assert.Contains(DeploymentStepNames.StageCustomUnattend, names);
        Assert.Contains(DeploymentStepNames.ConfigureAiPolicies, names);
        Assert.Contains(DeploymentStepNames.ConfigureWindowsOptionalFeatures, names);
        Assert.DoesNotContain(DeploymentStepNames.ConfigureTargetComputerName, names);
        Assert.DoesNotContain(DeploymentStepNames.ConfigureOobeSettings, names);
    }

    [Fact]
    public void Build_NoMatchingCatalogPayload_RemovesDependentWorkButRetainsLookupOutcome()
    {
        DeploymentContext request = Request() with { DriverPackSelectionKind = DriverPackSelectionKind.MicrosoftUpdateCatalog, ApplyFirmwareUpdates = true };
        DeploymentRuntimeState state = new();
        state.StepOutcomes.Add(new(DeploymentStepNames.DownloadDriverPack, DeploymentStepState.Skipped, "No matches."));
        state.StepOutcomes.Add(new(DeploymentStepNames.DownloadFirmwareUpdate, DeploymentStepState.Skipped, "No updates."));
        string[] names = DeploymentPlan.Build(request, state).Select(step => step.Name).ToArray();

        Assert.Equal(DeploymentStepNames.DownloadDriverPack, names[0]);
        Assert.Equal(DeploymentStepNames.DownloadFirmwareUpdate, names[1]);
        Assert.DoesNotContain(DeploymentStepNames.ExtractDriverPack, names);
        Assert.DoesNotContain(DeploymentStepNames.ApplyRecoveryDrivers, names);
        Assert.DoesNotContain(DeploymentStepNames.ExtractFirmwareUpdate, names);
        Assert.DoesNotContain(DeploymentStepNames.ApplyFirmwareUpdate, names);
    }

    [Fact]
    public void Build_CachedCatalogPayload_RetainsExtractionAndInstallation()
    {
        DeploymentContext request = Request() with { DriverPackSelectionKind = DriverPackSelectionKind.MicrosoftUpdateCatalog };
        DeploymentRuntimeState state = new() { MicrosoftUpdateCatalogDriverPaths = ["selected.cab"] };
        state.StepOutcomes.Add(new(DeploymentStepNames.DownloadDriverPack, DeploymentStepState.Skipped, "Cached package reused."));
        string[] names = DeploymentPlan.Build(request, state).Select(step => step.Name).ToArray();

        Assert.Contains(DeploymentStepNames.ExtractDriverPack, names);
        Assert.Contains(DeploymentStepNames.ApplyDriverPack, names);
        Assert.Contains(DeploymentStepNames.ApplyRecoveryDrivers, names);
    }

    [Theory]
    [InlineData("RET", true)]
    [InlineData("VOL", false)]
    public void Build_ImplicitRetailActivation_RequiresSetupTasks(string channel, bool expected)
    {
        DeploymentContext request = Request() with { OperatingSystem = new OperatingSystemCatalogItem { LicenseChannel = channel } };
        Assert.Equal(expected, DeploymentPlan.Build(request, networkResolved: true).Any(step => step.Name == DeploymentStepNames.StagePreOobeCustomization));
    }

    [Theory]
    [InlineData(AutopilotProvisioningMode.JsonProfile, "Copy Autopilot profile")]
    [InlineData(AutopilotProvisioningMode.HardwareHashUpload, "Register Autopilot device")]
    [InlineData(AutopilotProvisioningMode.InteractiveHardwareHashUpload, "Prepare Autopilot assistant")]
    public void Build_AutopilotLabel_ChangesWithoutChangingIdentity(AutopilotProvisioningMode mode, string label)
    {
        DeploymentPlanEntry step = Assert.Single(DeploymentPlan.Build(Request() with { IsAutopilotEnabled = true, AutopilotProvisioningMode = mode }),
            step => step.Name == DeploymentStepNames.ProvisionAutopilot);
        Assert.Equal(label, step.Label);
    }

    private static DeploymentContext Request() => new()
    {
        Mode = DeploymentMode.Usb,
        CacheRootPath = "cache",
        TargetDiskNumber = 1,
        TargetComputerName = "TEST-PC",
        OperatingSystem = new OperatingSystemCatalogItem { LicenseChannel = "VOL" },
        DriverPackSelectionKind = DriverPackSelectionKind.None,
        ApplyFirmwareUpdates = false
    };
}
