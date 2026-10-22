// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Drivers;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CoreDeployNetworkSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkSettings;

namespace Foundry.Deploy.Services.Wizard;

public sealed class DeploymentWizardContext : IDisposable
{
    private readonly CustomDriverSetCatalogService? _customDriverSetCatalog;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private int _customDriverDiscoveryVersion;
    private bool _isDisposed;

    public DeploymentWizardContext(
        DeploymentPreparationViewModel preparation,
        OperatingSystemCatalogViewModel operatingSystemCatalog,
        DriverPackSelectionViewModel driverPackSelection,
        CustomDriverSetCatalogService? customDriverSetCatalog = null,
        ILogger<DeploymentWizardContext>? logger = null)
    {
        _customDriverSetCatalog = customDriverSetCatalog;
        _logger = logger ?? NullLogger<DeploymentWizardContext>.Instance;
        Preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
        OperatingSystemCatalog = operatingSystemCatalog ?? throw new ArgumentNullException(nameof(operatingSystemCatalog));
        DriverPackSelection = driverPackSelection ?? throw new ArgumentNullException(nameof(driverPackSelection));
        CustomImages = new();
        DomainJoinStep = new();

        Preparation.StateChanged += OnPreparationStateChanged;
        DomainJoinStep.StateChanged += OnDomainJoinStepStateChanged;
        OperatingSystemCatalog.StateChanged += OnOperatingSystemCatalogStateChanged;
        DriverPackSelection.StateChanged += OnDriverPackSelectionStateChanged;
        CustomImages.StateChanged += OnOperatingSystemCatalogStateChanged;

        RefreshDriverPackSelectionContext();
    }

    public DeploymentPreparationViewModel Preparation { get; }
    public OperatingSystemCatalogViewModel OperatingSystemCatalog { get; }
    public DriverPackSelectionViewModel DriverPackSelection { get; }
    public CustomImageSelectionViewModel CustomImages { get; }
    public DomainJoinStepViewModel DomainJoinStep { get; }
    public OperatingSystemMetadata? SelectedOperatingSystem => CustomImages.IsCustom ? CustomImages.Selection : OperatingSystemCatalog.SelectedOperatingSystem;
    public Foundry.Core.Models.Configuration.Deploy.DeployPreOobeSettings PreOobe { get; private set; } = new();

    /// <summary>Gets the custom driver media binding from the Deploy configuration; deployment re-checks it before changing the target disk.</summary>
    public Foundry.Core.Models.Configuration.Deploy.DeployCustomDriversSettings CustomDrivers { get; private set; } = new();
    public Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings DomainJoin { get; private set; } = new();
    public DeployCompletionSettings Completion { get; private set; } = new();
    public CoreDeployNetworkSettings Network { get; private set; } = new();
    public DeployOobeSettings Oobe { get; private set; } = new();
    public DeployAppxRemovalSettings AppxRemoval { get; private set; } = new();
    public DeployAiComponentRemovalSettings AiComponentRemoval { get; private set; } = new();
    public DeployWindowsOptionalFeatureSettings WindowsOptionalFeatures { get; private set; } = new();

    public event EventHandler? StateChanged;

    public void ApplyStartupSnapshot(DeploymentStartupSnapshot startupSnapshot)
    {
        ArgumentNullException.ThrowIfNull(startupSnapshot);

        Preparation.CacheRootPath = startupSnapshot.CacheRootPath;

        if (startupSnapshot.DeployConfigurationDocument is not null)
        {
            ApplyDeployConfiguration(
                startupSnapshot.DeployConfigurationDocument,
                startupSnapshot.AutopilotProfiles);
        }
        else
        {
            Preparation.ApplyAutopilotConfiguration(new DeployAutopilotSettings(), startupSnapshot.AutopilotProfiles);
        }

        Preparation.ApplyUnattendConfiguration(
            startupSnapshot.DeployConfigurationDocument?.Unattend ?? new(),
            startupSnapshot.ConfigurationPath,
            startupSnapshot.ConfigurationFailureMessage);
        Preparation.ApplyMachineNamePreparation(startupSnapshot.MachineNamePreparation);

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

    /// <summary>Replaces the media Domain Join configuration with a debug scenario and resets the technician's inputs.</summary>
    public void ApplyDebugDomainJoin(Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings domainJoin)
    {
        DomainJoin = domainJoin ?? throw new ArgumentNullException(nameof(domainJoin));
        DomainJoinStep.Configure(DomainJoin);
        RefreshDriverPackSelectionContext();
        StateChanged?.Invoke(this, EventArgs.Empty);
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
        CustomImages.StateChanged -= OnOperatingSystemCatalogStateChanged;
        DomainJoinStep.StateChanged -= OnDomainJoinStepStateChanged;
        DomainJoinStep.Dispose();
        CustomImages.Dispose();
        Preparation.Dispose();
        DriverPackSelection.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _isDisposed = true;
    }

    private void ApplyDeployConfiguration(
        FoundryDeployConfigurationDocument document,
        IReadOnlyList<AutopilotProfileCatalogItem> autopilotProfiles)
    {
        PreOobe = document.PreOobe ?? new();
        DomainJoin = document.DomainJoin ?? new();
        DomainJoinStep.Configure(DomainJoin);
        Completion = document.Completion ?? new DeployCompletionSettings();
        OperatingSystemCatalog.ApplyOperatingSystemSelection(document.OperatingSystemSelection);
        CustomImages.Configure(document.CustomImages ?? new());
        CustomDrivers = document.CustomDrivers ?? new();
        _ = DiscoverCustomDriverSetsAsync(CustomDrivers);
        Preparation.ApplyMachineNamingConfiguration(
            document.Customization.MachineNaming ?? new DeployMachineNamingSettings());
        Network = document.Network ?? new CoreDeployNetworkSettings();
        Oobe = document.Customization.Oobe ?? new DeployOobeSettings();
        AppxRemoval = document.Customization.AppxRemoval ?? new DeployAppxRemovalSettings();
        AiComponentRemoval = document.Customization.AiComponentRemoval ?? new DeployAiComponentRemovalSettings();
        WindowsOptionalFeatures = document.Customization.WindowsOptionalFeatures ?? new DeployWindowsOptionalFeatureSettings();
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

    private void OnDomainJoinStepStateChanged(object? sender, EventArgs e)
    {
        // Declining the join lifts what the join demands from a custom answer file.
        RefreshUnattendContext();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDriverPackSelectionStateChanged(object? sender, EventArgs e)
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshUnattendContext()
    {
        Preparation.UpdateUnattendContext(
            SelectedOperatingSystem?.Architecture ?? OperatingSystemCatalog.EffectiveOsArchitecture,
            DomainJoinPreparationService.IsRequiredFor(DomainJoin, SelectedOperatingSystem, DomainJoinStep.IsJoinDeclined));
    }

    /// <summary>
    /// Looks for the bound custom driver sets on the boot media and offers them in the Drivers step. Runs from the UI thread, so
    /// the result is applied there; a missing or invalid media keeps today's driver default.
    /// </summary>
    private async Task DiscoverCustomDriverSetsAsync(Foundry.Core.Models.Configuration.Deploy.DeployCustomDriversSettings settings)
    {
        int version = ++_customDriverDiscoveryVersion;
        if (_customDriverSetCatalog is null || !settings.IsEnabled)
        {
            return;
        }

        CustomDriverSetCatalog catalog;
        try
        {
            catalog = await _customDriverSetCatalog.DiscoverAsync(settings, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Custom driver set discovery failed.");
            catalog = new(CustomDriverSetCatalogStatus.Unavailable, []);
        }

        if (_isDisposed || version != _customDriverDiscoveryVersion)
        {
            return;
        }

        if (catalog.Status != CustomDriverSetCatalogStatus.Available)
        {
            _logger.LogWarning("Custom driver sets are not offered (status {Status}); the default driver source applies.", catalog.Status);
        }

        DriverPackSelection.ConfigureCustomDriverSets(catalog, settings.DefaultSetId);
    }

    private void RefreshDriverPackSelectionContext()
    {
        RefreshUnattendContext();
        DriverPackSelection.UpdateSelectionContext(
            Preparation.DetectedHardware,
            SelectedOperatingSystem,
            OperatingSystemCatalog.EffectiveOsArchitecture);
    }
}
