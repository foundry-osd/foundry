using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Wizard;

public sealed class DeploymentWizardContextFactory : IDeploymentWizardContextFactory
{
    private readonly ITargetDiskService _targetDiskService;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IOfflineWindowsComputerNameService _offlineWindowsComputerNameService;
    private readonly IDriverPackSelectionService _driverPackSelectionService;
    private readonly ILoggerFactory _loggerFactory;

    public DeploymentWizardContextFactory(
        ITargetDiskService targetDiskService,
        IHardwareProfileService hardwareProfileService,
        IOfflineWindowsComputerNameService offlineWindowsComputerNameService,
        IDriverPackSelectionService driverPackSelectionService,
        ILoggerFactory loggerFactory)
    {
        _targetDiskService = targetDiskService;
        _hardwareProfileService = hardwareProfileService;
        _offlineWindowsComputerNameService = offlineWindowsComputerNameService;
        _driverPackSelectionService = driverPackSelectionService;
        _loggerFactory = loggerFactory;
    }

    public DeploymentWizardContext Create(bool isDebugSafeMode)
    {
        DeploymentPreparationViewModel preparation = new(
            _targetDiskService,
            _hardwareProfileService,
            _offlineWindowsComputerNameService,
            _loggerFactory.CreateLogger<DeploymentPreparationViewModel>(),
            isDebugSafeMode);
        OperatingSystemCatalogViewModel operatingSystemCatalog = new(
            _loggerFactory.CreateLogger<OperatingSystemCatalogViewModel>(),
            Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty);
        DriverPackSelectionViewModel driverPackSelection = new(
            _driverPackSelectionService,
            operatingSystemCatalog.EffectiveOsArchitecture);

        return new DeploymentWizardContext(
            preparation,
            operatingSystemCatalog,
            driverPackSelection,
            isDebugSafeMode);
    }
}
