// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Network;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Deployment.Unattend;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentCustomizationScenarioTests
{
    [Fact]
    public async Task CustomAnswerWithIndependentCustomizations_FinalizationRetainsEveryPostRebootDependency()
    {
        using var fixture = new DriverApplicationStepTestFixture();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string transientRoot = Path.Combine(fixture.WindowsRoot, "Foundry");
        Directory.CreateDirectory(transientRoot);
        string driverSource = Path.Combine(transientRoot, "driver.exe");
        byte[] driverBytes = [1, 2, 3];
        await File.WriteAllBytesAsync(driverSource, driverBytes, cancellationToken);
        string imageSource = Path.Combine(transientRoot, "install.wim");
        await File.WriteAllBytesAsync(imageSource, [4, 5, 6], cancellationToken);

        const string wifiXml = "<WLANProfile><name>Scenario network</name></WLANProfile>";
        string networkRoot = Path.Combine(fixture.WorkspaceRoot, "NetworkProfiles");
        Directory.CreateDirectory(networkRoot);
        await File.WriteAllTextAsync(Path.Combine(networkRoot, NetworkProfileRoamingArtifacts.WifiProfileFileName), wifiXml, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(networkRoot, NetworkProfileRoamingArtifacts.ManifestFileName),
            JsonSerializer.Serialize(new NetworkProfileRoamingManifest
            {
                WifiProfile = new NetworkProfileRoamingProfile
                {
                    RelativePath = NetworkProfileRoamingArtifacts.WifiProfileFileName,
                    Source = NetworkProfileRoamingArtifacts.ManualWifiSource
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), cancellationToken);

        byte[] answer = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="utf-8"?>
            <unattend xmlns="urn:schemas-microsoft-com:unattend"><settings pass="specialize"><component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64"><ComputerName>CUSTOM-PC</ComputerName></component></settings><!-- preserve exactly --></unattend>
            """);
        var request = new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            CacheRootPath = fixture.WorkspaceRoot,
            TargetDiskNumber = 1,
            TargetComputerName = string.Empty,
            Unattend = new UnattendSelection(new Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile(), "validated-answer.enc"),
            OperatingSystem = new OperatingSystemCatalogItem { Architecture = "amd64", LicenseChannel = "RET" },
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem { Manufacturer = "Lenovo", FileName = "driver.exe" },
            ApplyFirmwareUpdates = false,
            IsAutopilotEnabled = true,
            AutopilotProvisioningMode = AutopilotProvisioningMode.InteractiveHardwareHashUpload,
            AiComponentRemoval = new DeployAiComponentRemovalSettings { IsEnabled = true, RemoveCopilot = true },
            AppxRemoval = new DeployAppxRemovalSettings { IsEnabled = true, PackageNames = ["Microsoft.BingNews"] },
            WindowsOptionalFeatures = new DeployWindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                Actions = [new DeployWindowsOptionalFeatureAction { Id = "wf:telnetclient", Enable = true }]
            },
            Network = new Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings
            {
                ProfileRoaming = new Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings
                {
                    ArtifactRootPath = networkRoot,
                    Wifi = new Foundry.Core.Models.Configuration.NetworkProfileRoamingTransportSettings { IsEnabled = true }
                }
            }
        };
        var state = new DeploymentRuntimeState
        {
            WorkspaceRoot = fixture.WorkspaceRoot,
            TargetWindowsPartitionRoot = fixture.WindowsRoot,
            TargetFoundryRoot = transientRoot,
            DownloadedDriverPackPath = driverSource,
            DownloadedOperatingSystemPath = imageSource,
            AppliedImageIndex = 1,
            DriverPackInstallMode = DeploymentPlan.ResolveDriverMode(request),
            AiComponentRemoval = request.AiComponentRemoval,
            AppxRemoval = request.AppxRemoval,
            WindowsOptionalFeatures = request.WindowsOptionalFeatures
        };
        using var context = new DeploymentStepExecutionContext(request, state, [],
            new DriverApplicationOperationProgressService(), new DeploymentLogService(),
            new DriverApplicationTargetDiskService(), _ => { });
        // Start at the validated snapshot boundary; encryption validation has its own adversarial tests.
        context.UnattendSnapshot = new UnattendSnapshot(answer.ToArray(), UnattendFileService.Inspect(answer, "amd64"));
        var forbidden = new ForbiddenExternalServices();
        context.NetworkProfileRoamingPayload = await new NetworkProfileRoamingArtifactService(
            forbidden, NullLogger<NetworkProfileRoamingArtifactService>.Instance)
            .LoadAsync(request.Network.ProfileRoaming, fixture.WorkspaceRoot, cancellationToken);
        context.NetworkProfileRoamingResolved = true;
        Assert.NotNull(context.NetworkProfileRoamingPayload);

        var windows = new RecordingCustomizationService();
        var setupComplete = new SetupCompleteScriptService();
        IDeploymentStep[] customizationSteps =
        [
            new StageCustomUnattendStep(),
            new StageDriverInstallerStep(new DriverPackStrategyResolver()),
            new ConfigureAiPoliciesStep(windows),
            new ConfigureWindowsOptionalFeaturesStep(windows),
            new StagePreOobeCustomizationStep(new PreOobeScriptProvisioningService(setupComplete),
                new PreOobeScriptDefinitionBuilder(), new DriverPackStrategyResolver()),
            new ProvisionAutopilotStep(forbidden, forbidden,
                new AutopilotInteractiveRegistrationProvisioningService(setupComplete), forbidden),
            new FinalizeDeploymentAndWriteLogsStep()
        ];
        IReadOnlyList<DeploymentPlanEntry> plan = DeploymentPlan.Build(request, state,
            networkResolved: true, hasNetworkPayload: true);
        Assert.DoesNotContain(plan, step => step.Name == DeploymentStepNames.ConfigureTargetComputerName);
        Assert.DoesNotContain(plan, step => step.Name == DeploymentStepNames.ConfigureOobeSettings);
        string[] actualOrder = plan.Where(entry => customizationSteps.Any(step => step.Name == entry.Name))
            .Select(entry => entry.Name).ToArray();
        Assert.Equal(customizationSteps.Select(step => step.Name), actualOrder);
        context.UpdatePlan(plan);
        try
        {
            foreach (string name in actualOrder)
            {
                IDeploymentStep step = customizationSteps.Single(item => item.Name == name);
                context.SetCurrentStep(step, plan.ToList().FindIndex(entry => entry.Name == name) + 1);
                DeploymentStepResult result = await step.ExecuteAsync(context, cancellationToken);
                Assert.Equal(DeploymentStepState.Succeeded, result.State);
                state.CompletedSteps.Add(name);
                state.StepOutcomes.Add(new DeploymentStepOutcome(name, result.State, result.Message));
            }
        }
        finally
        {
            await context.TrySaveRuntimeStateAsync(CancellationToken.None);
        }

        Assert.Equal(1, windows.AiCalls);
        Assert.Equal(1, windows.FeatureCalls);
        Assert.False(Directory.Exists(transientRoot));
        Assert.Equal(answer, await File.ReadAllBytesAsync(Path.Combine(fixture.WindowsRoot, "Windows", "Panther", "unattend.xml"), cancellationToken));
        Assert.Equal(driverBytes, await File.ReadAllBytesAsync(state.DeferredDriverPackagePath!, cancellationToken));
        Assert.True(File.Exists(state.DeploymentSummaryPath));
        Assert.True(File.Exists(state.PreOobeManifestPath));
        Assert.All(state.PreOobeScriptPaths, path => Assert.True(File.Exists(path), path));

        string retainedRoot = Path.Combine(fixture.WindowsRoot, "Windows", "Temp", "Foundry");
        string dataRoot = Path.Combine(retainedRoot, "Payloads");
        Assert.Equal(wifiXml, await File.ReadAllTextAsync(Path.Combine(dataRoot, "NetworkProfiles", "wifi-profile.xml"), cancellationToken));
        Assert.True(File.Exists(Path.Combine(dataRoot, "NetworkProfiles", "import-settings.json")));
        Assert.Contains("Microsoft.Copilot", await File.ReadAllTextAsync(Path.Combine(dataRoot, "Customization", "Remove-AiComponents.settings.json"), cancellationToken));
        Assert.Contains("Microsoft.BingNews", await File.ReadAllTextAsync(Path.Combine(dataRoot, "Customization", "Remove-AppX.packages.json"), cancellationToken));
        string runner = await File.ReadAllTextAsync(state.PreOobeRunnerPath!, cancellationToken);
        foreach (string script in new[] { "Install-DriverPack.ps1", "Import-NetworkProfiles.ps1", "Remove-AiComponents.ps1", "Remove-AppX.ps1", "Cleanup-PreOobe.ps1" })
            Assert.Contains(script, runner, StringComparison.Ordinal);
        Assert.True(runner.IndexOf("Install-DriverPack.ps1", StringComparison.Ordinal) < runner.IndexOf("Import-NetworkProfiles.ps1", StringComparison.Ordinal));
        Assert.True(runner.IndexOf("Import-NetworkProfiles.ps1", StringComparison.Ordinal) < runner.IndexOf("Remove-AiComponents.ps1", StringComparison.Ordinal));
        Assert.True(runner.IndexOf("Remove-AppX.ps1", StringComparison.Ordinal) < runner.IndexOf("Cleanup-PreOobe.ps1", StringComparison.Ordinal));
        Assert.DoesNotContain("Activate-WindowsOem.ps1", runner, StringComparison.Ordinal);

        string setup = await File.ReadAllTextAsync(state.PreOobeSetupCompletePath!, cancellationToken);
        Assert.Equal(1, setup.Split("REM >>> FOUNDRY PRE-OOBE BEGIN", StringSplitOptions.None).Length - 1);
        string oobe = await File.ReadAllTextAsync(Path.Combine(fixture.WindowsRoot, "Windows", "Setup", "Scripts", "OOBE.cmd"), cancellationToken);
        Assert.Equal(1, oobe.Split("REM >>> FOUNDRY AUTOPILOT REGISTRATION BEGIN", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("FOUNDRY AUTOPILOT REGISTRATION", setup, StringComparison.Ordinal);
        Assert.True(File.Exists(state.StagedAutopilotConfigurationPath));
        foreach (string file in new[] { "Start-FoundryAutopilotRegistration.ps1", "Start-FoundryAutopilotRegistrationOobe.cmd", "Wait-FoundryAutopilotRegistrationOobe.ps1", "Start-FoundryAutopilotRegistrationForeground.ps1", "ServiceUI.exe" })
            Assert.True(File.Exists(Path.Combine(retainedRoot, "Runtime", "AutopilotRegistration", file)), file);
    }

    private sealed class RecordingCustomizationService : RecordingDriverApplicationService, IWindowsDeploymentService
    {
        public int AiCalls { get; private set; }
        public int FeatureCalls { get; private set; }

        Task IWindowsDeploymentService.ConfigureOfflineAiComponentRemovalAsync(string windowsPartitionRoot, DeployAiComponentRemovalSettings settings, string workingDirectory, CancellationToken cancellationToken)
        {
            Assert.True(settings.RemoveCopilot);
            AiCalls++;
            return Task.CompletedTask;
        }

        Task<WindowsOptionalFeatureServicingResult> IWindowsDeploymentService.ConfigureOfflineWindowsOptionalFeaturesAsync(string setupMediaImagePath, string windowsPartitionRoot, int appliedImageIndex, DeployWindowsOptionalFeatureSettings settings, string scratchDirectory, string sourceExtractionDirectory, string workingDirectory, CancellationToken cancellationToken, IProgress<double>? progress, Action? onInspectionStarted, Action? onSourcePreparationStarted, Action? onServicingStarted, string? customSourceDirectory)
        {
            Assert.True(File.Exists(setupMediaImagePath));
            Assert.Equal(1, appliedImageIndex);
            Assert.Equal("wf:telnetclient", Assert.Single(settings.Actions).Id);
            FeatureCalls++;
            return Task.FromResult(new WindowsOptionalFeatureServicingResult { RequestedActionCount = 1, ChangedActionCount = 1 });
        }
    }

    private sealed class ForbiddenExternalServices : INetworkSecretKeyReader, IAutopilotHardwareHashCaptureService, IAutopilotHardwareHashUploadService, IAutopilotProfileContentService
    {
        public Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutopilotHardwareHashCaptureResult> CaptureAsync(AutopilotHardwareHashCaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AutopilotHardwareHashUploadResult> UploadAsync(AutopilotHardwareHashUploadRequest request, IProgress<AutopilotHardwareHashUploadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(AutopilotProfileCatalogItem profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
