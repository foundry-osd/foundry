using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.ViewModels;
using CoreDeployNetworkSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings;

namespace Foundry.Deploy.Services.Wizard;

public sealed class DeploymentWizardContext : IDisposable
{
    private bool _isDisposed;

    public DeploymentWizardContext(
        DeploymentPreparationViewModel preparation,
        OperatingSystemCatalogViewModel operatingSystemCatalog,
        DriverPackSelectionViewModel driverPackSelection)
    {
        Preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        OperatingSystemCatalog = operatingSystemCatalog ?? throw new ArgumentNullException(nameof(operatingSystemCatalog));
        DriverPackSelection = driverPackSelection ?? throw new ArgumentNullException(nameof(driverPackSelection));

        Preparation.StateChanged += OnPreparationStateChanged;
        OperatingSystemCatalog.StateChanged += OnOperatingSystemCatalogStateChanged;
        DriverPackSelection.StateChanged += OnDriverPackSelectionStateChanged;

        RefreshDriverPackSelectionContext();
    }

    public DeploymentPreparationViewModel Preparation { get; }
    public OperatingSystemCatalogViewModel OperatingSystemCatalog { get; }
    public DriverPackSelectionViewModel DriverPackSelection { get; }
    public string? DefaultTimeZoneId { get; private set; }
    public DeployOsRecoverySettings OsRecovery { get; private set; } = new();
    public CoreDeployNetworkSettings Network { get; private set; } = new();
    public DeployOobeSettings Oobe { get; private set; } = new();
    public DeployAppxRemovalSettings AppxRemoval { get; private set; } = new();
    public DeployAiComponentRemovalSettings AiComponentRemoval { get; private set; } = new();

    public event EventHandler? StateChanged;

    public void ApplyStartupSnapshot(DeploymentStartupSnapshot startupSnapshot)
    {
        ArgumentNullException.ThrowIfNull(startupSnapshot);

        Preparation.CacheRootPath = startupSnapshot.CacheRootPath;

        if (startupSnapshot.DeployConfigurationDocument is not null)
        {
            ApplyDeployConfiguration(
                startupSnapshot.DeployConfigurationDocument,
                startupSnapshot.EffectiveComputerName,
                startupSnapshot.AutopilotProfiles);
        }
        else
        {
            Preparation.ApplyAutopilotConfiguration(new DeployAutopilotSettings(), startupSnapshot.AutopilotProfiles);
        }

        Preparation.ApplyOfflineComputerName(startupSnapshot.EffectiveComputerName);

        if (startupSnapshot.DetectedHardware is not null)
        {
            Preparation.SetDetectedHardware(startupSnapshot.DetectedHardware);
            OperatingSystemCatalog.SetEffectiveArchitecture(startupSnapshot.DetectedHardware.Architecture);
        }
        else if (!string.IsNullOrWhiteSpace(startupSnapshot.HardwareDetectionFailureMessage))
        {
            Preparation.SetHardwareDetectionFailure(startupSnapshot.HardwareDetectionFailureMessage);
        }

        Preparation.ApplyTargetDisks(startupSnapshot.TargetDisks);
        ApplyCatalogSnapshot(startupSnapshot.CatalogSnapshot);
    }

    public void ApplyCatalogSnapshot(DeploymentCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        OperatingSystemCatalog.ApplyCatalog(snapshot.OperatingSystems);
        DriverPackSelection.ReplaceCatalog(snapshot.DriverPacks);
        RefreshDriverPackSelectionContext();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Preparation.StateChanged -= OnPreparationStateChanged;
        OperatingSystemCatalog.StateChanged -= OnOperatingSystemCatalogStateChanged;
        DriverPackSelection.StateChanged -= OnDriverPackSelectionStateChanged;
        Preparation.Dispose();
        DriverPackSelection.Dispose();
        _isDisposed = true;
    }

    private void ApplyDeployConfiguration(
        FoundryDeployConfigurationDocument document,
        string seedComputerName,
        IReadOnlyList<AutopilotProfileCatalogItem> autopilotProfiles)
    {
        OperatingSystemCatalog.ApplyOperatingSystemSelection(document.OperatingSystemSelection);
        DefaultTimeZoneId = string.IsNullOrWhiteSpace(document.Localization.DefaultTimeZoneId)
            ? null
            : document.Localization.DefaultTimeZoneId.Trim();
        Preparation.ApplyMachineNamingConfiguration(
            document.Customization.MachineNaming ?? new DeployMachineNamingSettings(),
            string.IsNullOrWhiteSpace(Preparation.TargetComputerName)
                ? seedComputerName
                : Preparation.TargetComputerName);
        OsRecovery = document.OsRecovery ?? new DeployOsRecoverySettings();
        Network = document.Network ?? new CoreDeployNetworkSettings();
        Oobe = document.Customization.Oobe ?? new DeployOobeSettings();
        AppxRemoval = document.Customization.AppxRemoval ?? new DeployAppxRemovalSettings();
        AiComponentRemoval = document.Customization.AiComponentRemoval ?? new DeployAiComponentRemovalSettings();
        Preparation.ApplyAutopilotConfiguration(document.Autopilot ?? new DeployAutopilotSettings(), autopilotProfiles);
    }

    private void OnPreparationStateChanged(object? sender, EventArgs e)
    {
        RefreshDriverPackSelectionContext();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnOperatingSystemCatalogStateChanged(object? sender, EventArgs e)
    {
        RefreshDriverPackSelectionContext();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDriverPackSelectionStateChanged(object? sender, EventArgs e)
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshDriverPackSelectionContext()
    {
        DriverPackSelection.UpdateSelectionContext(
            Preparation.DetectedHardware,
            OperatingSystemCatalog.SelectedOperatingSystem,
            OperatingSystemCatalog.EffectiveOsArchitecture);
    }
}
