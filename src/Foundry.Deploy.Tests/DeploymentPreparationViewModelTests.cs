using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPreparationViewModelTests
{
    [Fact]
    public void ApplyAutopilotConfiguration_WhenProfilesExistButConfigIsDisabled_KeepsAutopilotDisabled()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        AutopilotProfileCatalogItem profile = CreateProfile("default", "Default Profile");

        viewModel.ApplyAutopilotConfiguration(new DeployAutopilotSettings(), [profile]);

        Assert.True(viewModel.HasAutopilotProfiles);
        Assert.True(viewModel.IsAutopilotSectionVisible);
        Assert.False(viewModel.IsAutopilotEnabled);
        Assert.False(viewModel.IsAutopilotProfileSelectionEnabled);
        Assert.Null(viewModel.SelectedAutopilotProfile);
    }

    [Fact]
    public void ApplyAutopilotConfiguration_WhenDefaultProfileExists_SelectsDefaultProfile()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        AutopilotProfileCatalogItem firstProfile = CreateProfile("first", "First Profile");
        AutopilotProfileCatalogItem defaultProfile = CreateProfile("default", "Default Profile");

        viewModel.ApplyAutopilotConfiguration(
            new DeployAutopilotSettings { IsEnabled = true, DefaultProfileFolderName = "default" },
            [firstProfile, defaultProfile]);

        Assert.True(viewModel.IsAutopilotEnabled);
        Assert.Same(defaultProfile, viewModel.SelectedAutopilotProfile);
    }

    [Fact]
    public void ApplyAutopilotConfiguration_WhenEnabledDefaultProfileIsMissing_DoesNotFallbackToFirstProfile()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        AutopilotProfileCatalogItem firstProfile = CreateProfile("first", "First Profile");

        viewModel.ApplyAutopilotConfiguration(
            new DeployAutopilotSettings { IsEnabled = true, DefaultProfileFolderName = "missing" },
            [firstProfile]);

        Assert.True(viewModel.HasAutopilotProfiles);
        Assert.True(viewModel.IsAutopilotEnabled);
        Assert.True(viewModel.IsAutopilotSectionVisible);
        Assert.True(viewModel.IsAutopilotProfileSelectionEnabled);
        Assert.Null(viewModel.SelectedAutopilotProfile);
    }

    [Fact]
    public void ApplyAutopilotConfiguration_WhenEnabledProfileIsMissing_KeepsAutopilotEnabledWithoutSelection()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();

        viewModel.ApplyAutopilotConfiguration(new DeployAutopilotSettings { IsEnabled = true }, []);

        Assert.False(viewModel.HasAutopilotProfiles);
        Assert.True(viewModel.IsAutopilotEnabled);
        Assert.True(viewModel.IsAutopilotSectionVisible);
        Assert.False(viewModel.IsAutopilotProfileSelectionEnabled);
        Assert.Null(viewModel.SelectedAutopilotProfile);
        Assert.NotEqual(string.Empty, viewModel.AutopilotProfileHint);
    }

    [Fact]
    public void IsAutopilotProfileSelectionEnabled_FollowsAutopilotToggle()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        AutopilotProfileCatalogItem profile = CreateProfile("default", "Default Profile");

        viewModel.ApplyAutopilotConfiguration(
            new DeployAutopilotSettings { IsEnabled = true, DefaultProfileFolderName = "default" },
            [profile]);
        viewModel.IsAutopilotEnabled = false;

        Assert.False(viewModel.IsAutopilotProfileSelectionEnabled);
        Assert.True(viewModel.IsAutopilotSectionVisible);

        viewModel.IsAutopilotEnabled = true;

        Assert.True(viewModel.IsAutopilotProfileSelectionEnabled);
        Assert.Same(profile, viewModel.SelectedAutopilotProfile);
    }

    private static DeploymentPreparationViewModel CreateViewModel()
    {
        return new DeploymentPreparationViewModel(
            new FakeTargetDiskService(),
            new FakeHardwareProfileService(),
            new FakeOfflineWindowsComputerNameService(),
            new LocalizationService(),
            NullLogger.Instance,
            isDebugSafeMode: false);
    }

    private static AutopilotProfileCatalogItem CreateProfile(string folderName, string displayName)
    {
        return new AutopilotProfileCatalogItem
        {
            FolderName = folderName,
            DisplayName = displayName,
            ConfigurationFilePath = $@"X:\Foundry\Config\Autopilot\{folderName}\AutopilotConfigurationFile.json"
        };
    }

    private sealed class FakeTargetDiskService : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
        }
    }

    private sealed class FakeHardwareProfileService : IHardwareProfileService
    {
        public Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HardwareProfile());
        }
    }

    private sealed class FakeOfflineWindowsComputerNameService : IOfflineWindowsComputerNameService
    {
        public Task<string?> TryGetOfflineComputerNameAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
