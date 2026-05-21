using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Runtime;
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
    public void ApplyAutopilotConfiguration_WhenHardwareHashModeIsEnabled_DoesNotRequireJsonProfile()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        AutopilotProfileCatalogItem profile = CreateProfile("json", "JSON Profile");
        DeployAutopilotHardwareHashUploadSettings hardwareHashUpload = new()
        {
            TenantId = "tenant-id",
            ClientId = "client-id",
            ActiveCertificateKeyId = "certificate-key-id",
            ActiveCertificateThumbprint = "ABCDEF123456",
            ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(1),
            DefaultGroupTag = "Sales"
        };

        viewModel.ApplyAutopilotConfiguration(
            new DeployAutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                DefaultProfileFolderName = "json",
                HardwareHashUpload = hardwareHashUpload
            },
            [profile]);

        Assert.True(viewModel.IsAutopilotEnabled);
        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, viewModel.AutopilotProvisioningMode);
        Assert.True(viewModel.IsHardwareHashUploadControlsVisible);
        Assert.False(viewModel.IsJsonProfileControlsVisible);
        Assert.False(viewModel.IsAutopilotProfileSelectionEnabled);
        Assert.Null(viewModel.SelectedAutopilotProfile);
        Assert.Same(hardwareHashUpload, viewModel.AutopilotHardwareHashUpload);
        Assert.True(viewModel.IsHardwareHashCertificateUsable);
        Assert.True(viewModel.IsHardwareHashGroupTagControlsVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.AutopilotHardwareHashReadinessText));
        Assert.Contains("Sales", viewModel.UseDefaultHardwareHashGroupTagText);
    }

    [Fact]
    public void ApplyAutopilotConfiguration_WhenHardwareHashCertificateExpired_SurfacesExpiredState()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();

        viewModel.ApplyAutopilotConfiguration(
            new DeployAutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = new DeployAutopilotHardwareHashUploadSettings
                {
                    ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddDays(-1)
                }
            },
            []);

        Assert.True(viewModel.IsHardwareHashCertificateExpired);
        Assert.False(viewModel.IsHardwareHashCertificateUsable);
        Assert.False(viewModel.IsHardwareHashGroupTagControlsVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.AutopilotHardwareHashReadinessText));
    }

    [Fact]
    public void ApplyAutopilotConfiguration_WhenHardwareHashCertificateKeyIdIsMissing_DoesNotExposeGroupTagControls()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        DeployAutopilotSettings settings = CreateHardwareHashSettings(defaultGroupTag: "Sales") with
        {
            HardwareHashUpload = CreateHardwareHashSettings(defaultGroupTag: "Sales").HardwareHashUpload! with
            {
                ActiveCertificateKeyId = null
            }
        };

        viewModel.ApplyAutopilotConfiguration(settings, []);

        Assert.False(viewModel.HasHardwareHashUploadMetadata);
        Assert.False(viewModel.IsHardwareHashCertificateUsable);
        Assert.False(viewModel.IsHardwareHashGroupTagControlsVisible);
        Assert.False(viewModel.IsHardwareHashPreRuntimeWarningVisible);
    }

    [Fact]
    public void CreateAutopilotHardwareHashUploadForLaunch_WhenDefaultGroupTagIsUsed_ReturnsDefaultGroupTag()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        viewModel.ApplyAutopilotConfiguration(
            CreateHardwareHashSettings(defaultGroupTag: "Sales"),
            []);

        DeployAutopilotHardwareHashUploadSettings result = viewModel.CreateAutopilotHardwareHashUploadForLaunch();

        Assert.Equal("Sales", result.DefaultGroupTag);
    }

    [Fact]
    public void CreateAutopilotHardwareHashUploadForLaunch_WhenCustomGroupTagIsUsed_ReturnsTrimmedCustomGroupTag()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        viewModel.ApplyAutopilotConfiguration(
            CreateHardwareHashSettings(defaultGroupTag: "Sales"),
            []);

        viewModel.UseCustomHardwareHashGroupTag = true;
        viewModel.CustomHardwareHashGroupTag = " Kiosk ";

        DeployAutopilotHardwareHashUploadSettings result = viewModel.CreateAutopilotHardwareHashUploadForLaunch();

        Assert.Equal("Kiosk", result.DefaultGroupTag);
        Assert.False(viewModel.UseDefaultHardwareHashGroupTag);
    }

    [Fact]
    public void CreateAutopilotHardwareHashUploadForLaunch_WhenDefaultGroupTagIsNone_ReturnsNullGroupTag()
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();
        viewModel.ApplyAutopilotConfiguration(
            CreateHardwareHashSettings(defaultGroupTag: null),
            []);

        DeployAutopilotHardwareHashUploadSettings result = viewModel.CreateAutopilotHardwareHashUploadForLaunch();

        Assert.Null(result.DefaultGroupTag);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.EffectiveHardwareHashGroupTagText));
    }

    [Theory]
    [InlineData(DebugAutopilotMode.None, false, AutopilotProvisioningMode.JsonProfile)]
    [InlineData(DebugAutopilotMode.JsonProfile, true, AutopilotProvisioningMode.JsonProfile)]
    [InlineData(DebugAutopilotMode.HardwareHashUpload, true, AutopilotProvisioningMode.HardwareHashUpload)]
    public void ApplyDebugAutopilotMode_OverridesAutopilotState(DebugAutopilotMode mode, bool expectedEnabled, AutopilotProvisioningMode expectedMode)
    {
        using DeploymentPreparationViewModel viewModel = CreateViewModel();

        viewModel.ApplyDebugAutopilotMode(mode);

        Assert.Equal(expectedEnabled, viewModel.IsAutopilotEnabled);
        Assert.Equal(expectedMode, viewModel.AutopilotProvisioningMode);
        Assert.Equal(mode == DebugAutopilotMode.JsonProfile, viewModel.SelectedAutopilotProfile is not null);
        Assert.Equal(mode == DebugAutopilotMode.HardwareHashUpload, viewModel.IsHardwareHashUploadControlsVisible);
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

    private static DeployAutopilotSettings CreateHardwareHashSettings(string? defaultGroupTag)
    {
        return new DeployAutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = new DeployAutopilotHardwareHashUploadSettings
            {
                TenantId = "tenant-id",
                ClientId = "client-id",
                ActiveCertificateKeyId = "certificate-key-id",
                ActiveCertificateThumbprint = "ABCDEF123456",
                ActiveCertificateExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(1),
                DefaultGroupTag = defaultGroupTag
            }
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
