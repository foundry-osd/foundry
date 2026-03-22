using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Services.Wizard;

public sealed class DeploymentWizardContext : IDisposable
{
    private readonly bool _isDebugSafeMode;
    private bool _isDisposed;

    public DeploymentWizardContext(
        DeploymentPreparationViewModel preparation,
        OperatingSystemCatalogViewModel operatingSystemCatalog,
        DriverPackSelectionViewModel driverPackSelection,
        bool isDebugSafeMode)
    {
        Preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        OperatingSystemCatalog = operatingSystemCatalog ?? throw new ArgumentNullException(nameof(operatingSystemCatalog));
        DriverPackSelection = driverPackSelection ?? throw new ArgumentNullException(nameof(driverPackSelection));
        _isDebugSafeMode = isDebugSafeMode;

        Preparation.StateChanged += OnPreparationStateChanged;
        Preparation.StatusMessageGenerated += OnPreparationStatusMessageGenerated;
        OperatingSystemCatalog.StateChanged += OnOperatingSystemCatalogStateChanged;
        DriverPackSelection.StateChanged += OnDriverPackSelectionStateChanged;

        RefreshDriverPackSelectionContext();
    }

    public DeploymentPreparationViewModel Preparation { get; }
    public OperatingSystemCatalogViewModel OperatingSystemCatalog { get; }
    public DriverPackSelectionViewModel DriverPackSelection { get; }

    public event EventHandler? StateChanged;
    public event Action<string>? StatusMessageGenerated;

    public string ApplyStartupSnapshot(DeploymentStartupSnapshot startupSnapshot)
    {
        ArgumentNullException.ThrowIfNull(startupSnapshot);

        Preparation.CacheRootPath = startupSnapshot.CacheRootPath;

        if (startupSnapshot.ExpertConfigurationDocument is not null)
        {
            ApplyExpertConfiguration(
                startupSnapshot.ExpertConfigurationDocument,
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

        string targetDiskStatusMessage = Preparation.ApplyTargetDisks(startupSnapshot.TargetDisks);
        ApplyCatalogSnapshot(startupSnapshot.CatalogSnapshot);

        return !string.IsNullOrWhiteSpace(startupSnapshot.TargetDiskStatusMessage)
            ? startupSnapshot.TargetDiskStatusMessage
            : targetDiskStatusMessage;
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
        Preparation.StatusMessageGenerated -= OnPreparationStatusMessageGenerated;
        OperatingSystemCatalog.StateChanged -= OnOperatingSystemCatalogStateChanged;
        DriverPackSelection.StateChanged -= OnDriverPackSelectionStateChanged;
        _isDisposed = true;
    }

    private void ApplyExpertConfiguration(
        FoundryDeployConfigurationDocument document,
        string seedComputerName,
        IReadOnlyList<AutopilotProfileCatalogItem> autopilotProfiles)
    {
        OperatingSystemCatalog.ApplyExpertLocalization(
            document.Localization.VisibleLanguageCodes,
            document.Localization.DefaultLanguageCodeOverride,
            document.Localization.ForceSingleVisibleLanguage);
        Preparation.ApplyMachineNamingConfiguration(
            document.Customization.MachineNaming ?? new DeployMachineNamingSettings(),
            string.IsNullOrWhiteSpace(Preparation.TargetComputerName)
                ? seedComputerName
                : Preparation.TargetComputerName);
        Preparation.ApplyAutopilotConfiguration(document.Autopilot ?? new DeployAutopilotSettings(), autopilotProfiles);
    }

    private void OnPreparationStateChanged(object? sender, EventArgs e)
    {
        RefreshDriverPackSelectionContext();

        if (Preparation.SelectedTargetDisk is not null &&
            !_isDebugSafeMode &&
            !Preparation.SelectedTargetDisk.IsSelectable)
        {
            StatusMessageGenerated?.Invoke($"Selected disk blocked: {Preparation.SelectedTargetDisk.SelectionWarning}");
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreparationStatusMessageGenerated(string message)
    {
        StatusMessageGenerated?.Invoke(message);
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
