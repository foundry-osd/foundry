using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Logging;
using Foundry.Models.Configuration;
using Foundry.Services.Adk;
using Foundry.Services.ApplicationUpdate;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.Services.WinPe;
using Microsoft.Extensions.Logging;

namespace Foundry.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string DefaultIsoFileName = "foundry-winpe.iso";
    private const string DefaultExpertConfigFileName = "foundry.expert.config.json";
    private const string DefaultDeployConfigFileName = "foundry.deploy.config.json";
    private const string IsoVolumeLabel = "FOUNDRY_WINPE";

    private readonly IApplicationShellService _applicationShellService;
    private readonly IApplicationUpdateService _applicationUpdateService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly IOperationProgressService _operationProgressService;
    private readonly IAdkService _adkService;
    private readonly IMediaOutputService _mediaOutputService;
    private readonly IExpertConfigurationService _expertConfigurationService;
    private readonly IFoundryConnectProvisioningService _foundryConnectProvisioningService;
    private readonly IDeployConfigurationGenerator _deployConfigurationGenerator;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty]
    private bool showAdkBanner;

    [ObservableProperty]
    private bool isAdkMissing;

    [ObservableProperty]
    private bool isAdkIncompatible;

    [ObservableProperty]
    private bool canCreateIso;

    [ObservableProperty]
    private bool canCreateUsb;

    [ObservableProperty]
    private bool isOperationInProgress;

    [ObservableProperty]
    private string isoOutputPath = string.Empty;

    [ObservableProperty]
    private WinPeArchitecture selectedArchitecture = WinPeArchitecture.X64;

    [ObservableProperty]
    private bool isAdvancedOptionsExpanded;

    [ObservableProperty]
    private bool useCa2023;

    [ObservableProperty]
    private UsbPartitionStyle selectedPartitionStyle = UsbPartitionStyle.Gpt;

    [ObservableProperty]
    private UsbFormatMode selectedUsbFormatMode = UsbFormatMode.Quick;

    [ObservableProperty]
    private bool includeDellDrivers;

    [ObservableProperty]
    private bool includeHpDrivers;

    [ObservableProperty]
    private string customDriverDirectoryPath = string.Empty;

    [ObservableProperty]
    private WinPeUsbDiskCandidate? selectedUsbDiskCandidate;

    [ObservableProperty]
    private string mediaActionMessage = string.Empty;

    [ObservableProperty]
    private bool isRefreshingUsbCandidates;

    [ObservableProperty]
    private WinPeLanguageOption? selectedWinPeLanguage;

    [ObservableProperty]
    private bool isExpertMode;

    [ObservableProperty]
    private ExpertSectionItem? selectedExpertSection;

    public ObservableCollection<WinPeUsbDiskCandidate> UsbDiskCandidates { get; } = [];
    public ObservableCollection<WinPeLanguageOption> AvailableWinPeLanguages { get; } = [];
    public ObservableCollection<UsbFormatModeOption> AvailableUsbFormatModes { get; } = [];
    public ObservableCollection<ExpertSectionItem> ExpertSections { get; } = [];

    public NetworkSettingsViewModel Network { get; }
    public LocalizationSettingsViewModel Localization { get; }
    public AutopilotSettingsViewModel Autopilot { get; }
    public CustomizationSettingsViewModel Customization { get; }

    public IReadOnlyList<WinPeArchitecture> AvailableArchitectures { get; } = Enum.GetValues<WinPeArchitecture>();
    public IReadOnlyList<UsbPartitionStyle> AvailablePartitionStyles { get; } = Enum.GetValues<UsbPartitionStyle>();

    public ILocalizationService LocalizationService => _localizationService;
    public CultureInfo CurrentCulture => _localizationService.CurrentCulture;
    public ThemeMode CurrentTheme => _themeService.CurrentTheme;
    public StringsWrapper Strings => _localizationService.Strings;
    public int GlobalOperationProgress => _operationProgressService.Progress;
    public bool IsGlobalOperationInProgress => _operationProgressService.IsOperationInProgress;
    public string GlobalOperationStatusDisplay =>
        _operationProgressService.Status ??
        (IsGlobalOperationInProgress ? Strings["Operation.InProgress"] : Strings["Operation.Ready"]);
    public string UsbDevicesCountDisplay =>
        string.Format(CurrentCulture, Strings["Usb.DevicesCountFormat"], UsbDiskCandidates.Count);
    public string VersionDisplay =>
        string.Format(CurrentCulture, Strings["Common.VersionFormat"], FoundryApplicationInfo.Version);
    public bool ShowUsbPartitionStyleArm64Hint => SelectedArchitecture == WinPeArchitecture.Arm64;
    public string UsbPartitionStyleArm64Hint => Strings["Usb.PartitionStyleArm64Hint"];
    public bool IsStandardMode => !IsExpertMode;
    public bool IsDebugMenuVisible => IsVisualStudioDebugSession();

    private static string StagingDirectoryPath => WinPeDefaults.GetWinPeWorkspaceRootPath();

    public MainWindowViewModel(
        IApplicationShellService applicationShellService,
        IApplicationUpdateService applicationUpdateService,
        IThemeService themeService,
        ILocalizationService localizationService,
        IOperationProgressService operationProgressService,
        IAdkService adkService,
        IMediaOutputService mediaOutputService,
        IExpertConfigurationService expertConfigurationService,
        IFoundryConnectProvisioningService foundryConnectProvisioningService,
        IDeployConfigurationGenerator deployConfigurationGenerator,
        ILanguageRegistryService languageRegistryService,
        AutopilotSettingsViewModel autopilotSettingsViewModel,
        ILogger<MainWindowViewModel> logger)
    {
        _applicationShellService = applicationShellService;
        _applicationUpdateService = applicationUpdateService;
        _themeService = themeService;
        _localizationService = localizationService;
        _operationProgressService = operationProgressService;
        _adkService = adkService;
        _mediaOutputService = mediaOutputService;
        _expertConfigurationService = expertConfigurationService;
        _foundryConnectProvisioningService = foundryConnectProvisioningService;
        _deployConfigurationGenerator = deployConfigurationGenerator;
        _logger = logger;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        Network = new NetworkSettingsViewModel(localizationService, applicationShellService, operationProgressService);
        Localization = new LocalizationSettingsViewModel(localizationService, languageRegistryService.GetLanguages());
        Autopilot = autopilotSettingsViewModel;
        Customization = new CustomizationSettingsViewModel(localizationService);

        _localizationService.LanguageChanged += OnLanguageChanged;
        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _adkService.AdkStatusChanged += OnAdkStatusChanged;
        _adkService.OperationProgressChanged += OnAdkOperationProgressChanged;
        UsbDiskCandidates.CollectionChanged += OnUsbDiskCandidatesCollectionChanged;
        Network.PropertyChanged += OnNetworkPropertyChanged;

        RefreshExpertSections();
        SelectedExpertSection = ExpertSections.FirstOrDefault();
        UpdateAdkStatus();
        RefreshWinPeLanguages(preserveSelection: false);
        RefreshUsbFormatModes();
        UpdateOperationState();
    }

    public void Dispose()
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;
        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
        _adkService.AdkStatusChanged -= OnAdkStatusChanged;
        _adkService.OperationProgressChanged -= OnAdkOperationProgressChanged;
        UsbDiskCandidates.CollectionChanged -= OnUsbDiskCandidatesCollectionChanged;
        Network.PropertyChanged -= OnNetworkPropertyChanged;
        Network.Dispose();
        Localization.Dispose();
        Autopilot.Dispose();
        Customization.Dispose();
    }

    [RelayCommand]
    private void Exit()
    {
        _applicationShellService.Shutdown();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        _applicationShellService.ShowAbout();
    }

    [RelayCommand]
    private void OpenDocumentation()
    {
        _applicationShellService.OpenUrl(FoundryApplicationInfo.DocumentationUrl);
    }

    [RelayCommand]
    private void OpenGitHubRepository()
    {
        _applicationShellService.OpenUrl(FoundryApplicationInfo.RepositoryUrl);
    }

    [RelayCommand]
    private void OpenGitHubIssues()
    {
        _applicationShellService.OpenUrl(FoundryApplicationInfo.IssuesUrl);
    }

    [RelayCommand]
    private Task CheckForUpdatesAsync()
    {
        return _applicationUpdateService.CheckForUpdatesAsync();
    }

    public Task RunStartupUpdateCheckAsync()
    {
        return _applicationUpdateService.CheckForUpdatesOnStartupAsync();
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        string logFolderPath = FoundryLogging.GetLogsDirectoryPath();

        try
        {
            Directory.CreateDirectory(logFolderPath);
            _applicationShellService.OpenFolder(logFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open log folder. LogFolderPath={LogFolderPath}", logFolderPath);
        }
    }

    [RelayCommand]
    private void SetStandardMode()
    {
        IsExpertMode = false;
    }

    [RelayCommand]
    private void SetExpertMode()
    {
        IsExpertMode = true;
        SelectedExpertSection ??= ExpertSections.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanImportExpertConfiguration))]
    private async Task ImportExpertConfigurationAsync()
    {
        string? path = _applicationShellService.PickOpenFilePath(
            Strings["Expert.ConfigImportTitle"],
            Strings["Common.JsonPickerFilter"]);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            FoundryExpertConfigurationDocument document = await _expertConfigurationService.LoadAsync(path).ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                ApplyExpertConfigurationDocument(document);
                IsExpertMode = true;
                SelectedExpertSection = ExpertSections.FirstOrDefault(section => string.Equals(section.Key, "general", StringComparison.OrdinalIgnoreCase));
                MediaActionMessage = string.Format(CurrentCulture, Strings["Expert.ConfigImportedFormat"], Path.GetFileName(path));
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import expert configuration. Path={Path}", path);
            RunOnUiThread(() => MediaActionMessage = string.Format(CurrentCulture, Strings["Expert.ConfigImportFailedFormat"], ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportExpertConfiguration))]
    private async Task ExportExpertConfigurationAsync()
    {
        string? path = _applicationShellService.PickSaveFilePath(
            Strings["Expert.ConfigExportTitle"],
            Strings["Common.JsonPickerFilter"],
            DefaultExpertConfigFileName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await _expertConfigurationService.SaveAsync(path, BuildExpertConfigurationDocument()).ConfigureAwait(false);
            RunOnUiThread(() => MediaActionMessage = string.Format(CurrentCulture, Strings["Expert.ConfigExportedFormat"], Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export expert configuration. Path={Path}", path);
            RunOnUiThread(() => MediaActionMessage = string.Format(CurrentCulture, Strings["Expert.ConfigExportFailedFormat"], ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportDeployConfiguration))]
    private async Task ExportDeployConfigurationAsync()
    {
        string? path = _applicationShellService.PickSaveFilePath(
            Strings["DeployConfig.ExportTitle"],
            Strings["Common.JsonPickerFilter"],
            DefaultDeployConfigFileName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            string json = BuildDeployConfigurationJsonForCurrentMode() ?? string.Empty;
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            RunOnUiThread(() => MediaActionMessage = string.Format(CurrentCulture, Strings["DeployConfig.ExportedFormat"], Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export deploy configuration. Path={Path}", path);
            RunOnUiThread(() => MediaActionMessage = string.Format(CurrentCulture, Strings["DeployConfig.ExportFailedFormat"], ex.Message));
        }
    }

    [RelayCommand]
    private void SetSystemTheme()
    {
        _themeService.SetTheme(ThemeMode.System);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    [RelayCommand]
    private void SetLightTheme()
    {
        _themeService.SetTheme(ThemeMode.Light);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    [RelayCommand]
    private void SetDarkTheme()
    {
        _themeService.SetTheme(ThemeMode.Dark);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    [RelayCommand]
    private void SetCulture(string cultureName)
    {
        _localizationService.SetCulture(new CultureInfo(cultureName));
    }

    [RelayCommand(CanExecute = nameof(CanBrowseIsoOutputPath))]
    private void BrowseIsoOutputPath()
    {
        string defaultFileName = string.IsNullOrWhiteSpace(IsoOutputPath)
            ? DefaultIsoFileName
            : Path.GetFileName(IsoOutputPath);

        string? selectedPath = _applicationShellService.PickIsoOutputPath(defaultFileName);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            IsoOutputPath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseCustomDriverDirectory))]
    private void BrowseCustomDriverDirectory()
    {
        string? selectedPath = _applicationShellService.PickFolderPath(
            Strings["General.CustomDriverPathPickerTitle"],
            string.IsNullOrWhiteSpace(CustomDriverDirectoryPath) ? null : CustomDriverDirectoryPath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            CustomDriverDirectoryPath = selectedPath;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowseDot1xCertificate))]
    private void BrowseDot1xCertificate()
    {
        string? selectedPath = _applicationShellService.PickOpenFilePath(
            Strings["Dot1x.CertificatePickerTitle"],
            Strings["Common.CertificatePickerFilter"]);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            Network.Dot1xCertificatePath = selectedPath;
        }
    }

    [RelayCommand]
    private async Task InstallAdkAsync()
    {
        try
        {
            await _adkService.InstallAdkAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing ADK.");
        }
    }

    [RelayCommand]
    private async Task UpgradeAdkAsync()
    {
        try
        {
            await _adkService.UpgradeAdkAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upgrading ADK.");
        }
    }

    [RelayCommand]
    private async Task RefreshUsbCandidatesAsync()
    {
        if (IsRefreshingUsbCandidates)
        {
            return;
        }

        IsRefreshingUsbCandidates = true;
        try
        {
            WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>> result = await _mediaOutputService.GetUsbCandidatesAsync();
            UsbDiskCandidates.Clear();

            if (!result.IsSuccess || result.Value is null)
            {
                if (result.Error is not null)
                {
                    MediaActionMessage = $"{result.Error.Code}: {result.Error.Message}";
                }
                UpdateOperationState();
                return;
            }

            foreach (WinPeUsbDiskCandidate candidate in result.Value)
            {
                UsbDiskCandidates.Add(candidate);
            }

            if (SelectedUsbDiskCandidate is null || !UsbDiskCandidates.Contains(SelectedUsbDiskCandidate))
            {
                SelectedUsbDiskCandidate = UsbDiskCandidates.FirstOrDefault();
            }

            UpdateOperationState();
        }
        catch (Exception ex)
        {
            MediaActionMessage = string.Format(CurrentCulture, Strings["Usb.DiskRefreshFailedFormat"], ex.Message);
            _logger.LogError(ex, "{MediaActionMessage}", MediaActionMessage);
        }
        finally
        {
            IsRefreshingUsbCandidates = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteCreateIso))]
    private async Task CreateIso()
    {
        try
        {
            MediaActionMessage = Strings["Operation.InProgress"];
            Directory.CreateDirectory(StagingDirectoryPath);
            FoundryExpertConfigurationDocument expertConfiguration = BuildExpertConfigurationDocument();
            FoundryConnectProvisioningBundle foundryConnectBundle = _foundryConnectProvisioningService.Prepare(
                expertConfiguration,
                StagingDirectoryPath);

            WinPeResult result = await _mediaOutputService.CreateIsoAsync(new IsoOutputOptions
            {
                StagingDirectoryPath = StagingDirectoryPath,
                OutputIsoPath = IsoOutputPath,
                VolumeLabel = IsoVolumeLabel,
                Architecture = SelectedArchitecture,
                SignatureMode = GetSignatureMode(),
                BootImageSource = ResolveBootImageSource(),
                WinPeLanguage = SelectedWinPeLanguage?.Code ?? string.Empty,
                DriverVendors = GetSelectedDriverVendors(),
                CustomDriverDirectoryPath = NormalizeCustomDriverDirectoryPath(),
                FoundryConnectConfigurationJson = foundryConnectBundle.ConfigurationJson,
                FoundryConnectAssetFiles = foundryConnectBundle.AssetFiles,
                ExpertDeployConfigurationJson = !IsExpertMode
                    ? null
                    : _deployConfigurationGenerator.Serialize(_deployConfigurationGenerator.Generate(expertConfiguration)),
                AutopilotProfiles = IsExpertMode ? expertConfiguration.Autopilot.Profiles : []
            });

            MediaActionMessage = result.IsSuccess
                ? Strings["General.IsoCreatedSuccessMessage"]
                : string.Format(CurrentCulture, Strings["General.IsoFailedMessageFormat"], result.Error?.Code, result.Error?.Message);

            if (!result.IsSuccess && result.Error is not null)
            {
                _logger.LogError(
                    "Create ISO failed [{ErrorCode}] {ErrorMessage} | {ErrorDetails}",
                    result.Error.Code,
                    result.Error.Message,
                    result.Error.Details);
            }
        }
        catch (Exception ex)
        {
            MediaActionMessage = string.Format(CurrentCulture, Strings["General.IsoCreateErrorFormat"], ex.Message);
            _logger.LogError(ex, "{MediaActionMessage}", MediaActionMessage);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteCreateUsb))]
    private async Task CreateUsb()
    {
        if (SelectedUsbDiskCandidate is null)
        {
            return;
        }

        double diskSizeGb = SelectedUsbDiskCandidate.SizeBytes / 1024d / 1024d / 1024d;
        string warningMessage = string.Format(
            CultureInfo.CurrentCulture,
            Strings["Usb.WarningMessage"],
            SelectedUsbDiskCandidate.DiskNumber,
            SelectedUsbDiskCandidate.FriendlyName,
            diskSizeGb.ToString("F1", CultureInfo.CurrentCulture));

        bool confirmed = _applicationShellService.ConfirmWarning(Strings["Usb.WarningTitle"], warningMessage);
        if (!confirmed)
        {
            MediaActionMessage = Strings["Usb.WarningCancelled"];
            return;
        }

        try
        {
            MediaActionMessage = Strings["Operation.InProgress"];
            Directory.CreateDirectory(StagingDirectoryPath);
            FoundryExpertConfigurationDocument expertConfiguration = BuildExpertConfigurationDocument();
            FoundryConnectProvisioningBundle foundryConnectBundle = _foundryConnectProvisioningService.Prepare(
                expertConfiguration,
                StagingDirectoryPath);

            WinPeResult result = await _mediaOutputService.CreateUsbAsync(new UsbOutputOptions
            {
                StagingDirectoryPath = StagingDirectoryPath,
                TargetDiskNumber = SelectedUsbDiskCandidate.DiskNumber,
                ExpectedDiskFriendlyName = SelectedUsbDiskCandidate.FriendlyName,
                ExpectedDiskSerialNumber = SelectedUsbDiskCandidate.SerialNumber,
                ExpectedDiskUniqueId = SelectedUsbDiskCandidate.UniqueId,
                PartitionStyle = SelectedPartitionStyle,
                FormatMode = SelectedUsbFormatMode,
                Architecture = SelectedArchitecture,
                SignatureMode = GetSignatureMode(),
                BootImageSource = ResolveBootImageSource(),
                WinPeLanguage = SelectedWinPeLanguage?.Code ?? string.Empty,
                DriverVendors = GetSelectedDriverVendors(),
                CustomDriverDirectoryPath = NormalizeCustomDriverDirectoryPath(),
                FoundryConnectConfigurationJson = foundryConnectBundle.ConfigurationJson,
                FoundryConnectAssetFiles = foundryConnectBundle.AssetFiles,
                ExpertDeployConfigurationJson = !IsExpertMode
                    ? null
                    : _deployConfigurationGenerator.Serialize(_deployConfigurationGenerator.Generate(expertConfiguration)),
                AutopilotProfiles = IsExpertMode ? expertConfiguration.Autopilot.Profiles : []
            });

            MediaActionMessage = result.IsSuccess
                ? Strings["Usb.CreatedSuccessMessage"]
                : string.Format(CurrentCulture, Strings["Usb.FailedMessageFormat"], result.Error?.Code, result.Error?.Message);

            if (!result.IsSuccess && result.Error is not null)
            {
                _logger.LogError(
                    "Create USB failed [{ErrorCode}] {ErrorMessage} | {ErrorDetails}",
                    result.Error.Code,
                    result.Error.Message,
                    result.Error.Details);
            }
        }
        catch (Exception ex)
        {
            MediaActionMessage = string.Format(CurrentCulture, Strings["Usb.CreateErrorFormat"], ex.Message);
            _logger.LogError(ex, "{MediaActionMessage}", MediaActionMessage);
        }
    }

    partial void OnIsoOutputPathChanged(string value)
    {
        UpdateOperationState();
    }

    partial void OnSelectedUsbDiskCandidateChanged(WinPeUsbDiskCandidate? value)
    {
        UpdateOperationState();
    }

    partial void OnSelectedArchitectureChanged(WinPeArchitecture value)
    {
        RefreshWinPeLanguages(preserveSelection: true);
        EnforcePartitionStyleForArchitecture(showInfoMessage: true);
        OnPropertyChanged(nameof(ShowUsbPartitionStyleArm64Hint));
        OnPropertyChanged(nameof(UsbPartitionStyleArm64Hint));
        UpdateOperationState();
    }

    partial void OnSelectedPartitionStyleChanged(UsbPartitionStyle value)
    {
        EnforcePartitionStyleForArchitecture(showInfoMessage: false);
    }

    partial void OnCustomDriverDirectoryPathChanged(string value)
    {
        UpdateOperationState();
    }

    partial void OnIsExpertModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStandardMode));
        ExportExpertConfigurationCommand.NotifyCanExecuteChanged();
        ExportDeployConfigurationCommand.NotifyCanExecuteChanged();
    }

    private bool CanImportExpertConfiguration()
    {
        return !IsOperationInProgress;
    }

    private bool CanExportExpertConfiguration()
    {
        return IsExpertMode && !IsOperationInProgress;
    }

    private bool CanExportDeployConfiguration()
    {
        return IsExpertMode && !IsOperationInProgress;
    }

    private bool CanBrowseIsoOutputPath()
    {
        return !IsOperationInProgress;
    }

    private bool CanBrowseCustomDriverDirectory()
    {
        return !IsOperationInProgress;
    }

    private bool CanBrowseDot1xCertificate()
    {
        return !IsOperationInProgress;
    }

    private bool CanExecuteCreateIso()
    {
        return CanCreateIso;
    }

    private bool CanExecuteCreateUsb()
    {
        return CanCreateUsb;
    }

    private IReadOnlyList<WinPeVendorSelection> GetSelectedDriverVendors()
    {
        var vendors = new List<WinPeVendorSelection>(2);
        if (IncludeDellDrivers)
        {
            vendors.Add(WinPeVendorSelection.Dell);
        }

        if (IncludeHpDrivers)
        {
            vendors.Add(WinPeVendorSelection.Hp);
        }

        return vendors;
    }

    private WinPeSignatureMode GetSignatureMode()
    {
        return UseCa2023 ? WinPeSignatureMode.Pca2023 : WinPeSignatureMode.Pca2011;
    }

    private WinPeBootImageSource ResolveBootImageSource()
    {
        return Network.IsWifiProvisioned
            ? WinPeBootImageSource.WinReWifi
            : WinPeBootImageSource.WinPe;
    }

    private void EnforcePartitionStyleForArchitecture(bool showInfoMessage)
    {
        if (SelectedArchitecture != WinPeArchitecture.Arm64 || SelectedPartitionStyle != UsbPartitionStyle.Mbr)
        {
            return;
        }

        SelectedPartitionStyle = UsbPartitionStyle.Gpt;
        if (showInfoMessage)
        {
            MediaActionMessage = Strings["Usb.PartitionStyleArm64AutoSetMessage"];
        }
    }

    private string? NormalizeCustomDriverDirectoryPath()
    {
        return string.IsNullOrWhiteSpace(CustomDriverDirectoryPath)
            ? null
            : CustomDriverDirectoryPath.Trim();
    }

    private void RefreshWinPeLanguages(bool preserveSelection)
    {
        string? previousSelection = preserveSelection ? SelectedWinPeLanguage?.Code : null;

        WinPeResult<IReadOnlyList<string>> result = _mediaOutputService.GetAvailableWinPeLanguages(SelectedArchitecture);
        string[] languageCodes = result.IsSuccess && result.Value is { Count: > 0 }
            ? result.Value.ToArray()
            : [];

        WinPeLanguageOption[] options = languageCodes
            .Select(CreateWinPeLanguageOption)
            .OrderBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AvailableWinPeLanguages.Clear();
        foreach (WinPeLanguageOption option in options)
        {
            AvailableWinPeLanguages.Add(option);
        }

        string preferredCode = ResolvePreferredWinPeLanguageCode(options, previousSelection);
        SelectedWinPeLanguage = AvailableWinPeLanguages.FirstOrDefault(option =>
            option.Code.Equals(preferredCode, StringComparison.OrdinalIgnoreCase))
            ?? AvailableWinPeLanguages.FirstOrDefault();
    }

    private void RefreshUsbFormatModes()
    {
        UsbFormatMode selectedMode = SelectedUsbFormatMode;

        UsbFormatModeOption[] options = Enum.GetValues<UsbFormatMode>()
            .Select(mode => new UsbFormatModeOption(mode, GetUsbFormatModeDisplayName(mode)))
            .ToArray();

        AvailableUsbFormatModes.Clear();
        foreach (UsbFormatModeOption option in options)
        {
            AvailableUsbFormatModes.Add(option);
        }

        if (!options.Any(option => option.Mode == selectedMode))
        {
            selectedMode = UsbFormatMode.Quick;
        }

        if (SelectedUsbFormatMode != selectedMode)
        {
            SelectedUsbFormatMode = selectedMode;
        }

        OnPropertyChanged(nameof(SelectedUsbFormatMode));
    }

    private string GetUsbFormatModeDisplayName(UsbFormatMode mode)
    {
        return mode switch
        {
            UsbFormatMode.Quick => Strings["Usb.FormatModeQuick"],
            UsbFormatMode.Complete => Strings["Usb.FormatModeComplete"],
            _ => mode.ToString()
        };
    }

    private void RefreshWinPeLanguageDisplayNames()
    {
        if (AvailableWinPeLanguages.Count == 0)
        {
            return;
        }

        string? selectedCode = SelectedWinPeLanguage?.Code;
        List<WinPeLanguageOption> refreshedOptions = AvailableWinPeLanguages
            .Select(option => CreateWinPeLanguageOption(option.Code))
            .ToList();

        AvailableWinPeLanguages.Clear();
        foreach (WinPeLanguageOption option in refreshedOptions)
        {
            AvailableWinPeLanguages.Add(option);
        }

        SelectedWinPeLanguage = AvailableWinPeLanguages.FirstOrDefault(option =>
            option.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase))
            ?? AvailableWinPeLanguages.FirstOrDefault();
    }

    private WinPeLanguageOption CreateWinPeLanguageOption(string languageCode)
    {
        string normalizedCode = NormalizeLanguageCode(languageCode);

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(normalizedCode);
            return new WinPeLanguageOption(normalizedCode, $"{GetWinPeLanguageDisplayName(culture)} ({culture.Name})");
        }
        catch (CultureNotFoundException)
        {
            return new WinPeLanguageOption(normalizedCode, normalizedCode);
        }
    }

    private string GetWinPeLanguageDisplayName(CultureInfo culture)
    {
        string displayName = culture.DisplayName;
        if (CurrentCulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(displayName, culture.EnglishName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(culture.NativeName, culture.EnglishName, StringComparison.OrdinalIgnoreCase))
        {
            displayName = culture.NativeName;
        }

        return string.IsNullOrWhiteSpace(displayName) ? culture.NativeName : displayName;
    }

    private static string ResolvePreferredWinPeLanguageCode(
        IReadOnlyList<WinPeLanguageOption> options,
        string? previousSelection)
    {
        if (options.Count == 0)
        {
            return string.Empty;
        }

        string normalizedPrevious = NormalizeLanguageCode(previousSelection);
        if (!string.IsNullOrWhiteSpace(normalizedPrevious))
        {
            WinPeLanguageOption? existing = options.FirstOrDefault(option =>
                option.Code.Equals(normalizedPrevious, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing.Code;
            }
        }

        string normalizedSystem = NormalizeLanguageCode(CultureInfo.CurrentUICulture.Name);
        WinPeLanguageOption? systemExact = options.FirstOrDefault(option =>
            option.Code.Equals(normalizedSystem, StringComparison.OrdinalIgnoreCase));
        if (systemExact is not null)
        {
            return systemExact.Code;
        }

        string languagePrefix = $"{CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}-";
        WinPeLanguageOption? systemFamily = options.FirstOrDefault(option =>
            option.Code.StartsWith(languagePrefix, StringComparison.OrdinalIgnoreCase));
        if (systemFamily is not null)
        {
            return systemFamily.Code;
        }

        return options[0].Code;
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        return string.IsNullOrWhiteSpace(languageCode)
            ? string.Empty
            : languageCode.Trim().Replace('_', '-').ToLowerInvariant();
    }

    private void RefreshExpertSections()
    {
        string? selectedKey = SelectedExpertSection?.Key;
        ExpertSectionItem[] sections =
        [
            new("general", Strings["Expert.SectionGeneral"], this),
            new("network", Strings["Expert.SectionNetwork"], Network),
            new("localization", Strings["Expert.SectionLocalization"], Localization),
            new("autopilot", Strings["Expert.SectionAutopilot"], Autopilot),
            new("customization", Strings["Expert.SectionCustomization"], Customization)
        ];

        ExpertSections.Clear();
        foreach (ExpertSectionItem section in sections)
        {
            ExpertSections.Add(section);
        }

        SelectedExpertSection = ExpertSections.FirstOrDefault(section =>
                                  string.Equals(section.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                              ?? ExpertSections.FirstOrDefault();
    }

    private FoundryExpertConfigurationDocument BuildExpertConfigurationDocument()
    {
        return new FoundryExpertConfigurationDocument
        {
            General = new GeneralSettings
            {
                IsoOutputPath = string.IsNullOrWhiteSpace(IsoOutputPath) ? null : IsoOutputPath.Trim(),
                Architecture = SelectedArchitecture,
                WinPeLanguage = SelectedWinPeLanguage?.Code,
                UseCa2023 = UseCa2023,
                UsbPartitionStyle = SelectedPartitionStyle,
                UsbFormatMode = SelectedUsbFormatMode,
                IncludeDellDrivers = IncludeDellDrivers,
                IncludeHpDrivers = IncludeHpDrivers,
                CustomDriverDirectoryPath = NormalizeCustomDriverDirectoryPath()
            },
            Network = Network.BuildSettings(),
            Localization = Localization.BuildSettings(),
            Autopilot = Autopilot.BuildSettings(),
            Customization = Customization.BuildSettings()
        };
    }

    private void ApplyExpertConfigurationDocument(FoundryExpertConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        IsoOutputPath = document.General.IsoOutputPath ?? string.Empty;
        SelectedArchitecture = document.General.Architecture;
        UseCa2023 = document.General.UseCa2023;
        SelectedPartitionStyle = document.General.UsbPartitionStyle;
        SelectedUsbFormatMode = document.General.UsbFormatMode;
        IncludeDellDrivers = document.General.IncludeDellDrivers;
        IncludeHpDrivers = document.General.IncludeHpDrivers;
        CustomDriverDirectoryPath = document.General.CustomDriverDirectoryPath ?? string.Empty;

        RefreshWinPeLanguages(preserveSelection: false);
        if (!string.IsNullOrWhiteSpace(document.General.WinPeLanguage))
        {
            SelectedWinPeLanguage = AvailableWinPeLanguages.FirstOrDefault(option =>
                option.Code.Equals(document.General.WinPeLanguage, StringComparison.OrdinalIgnoreCase))
                ?? SelectedWinPeLanguage;
        }

        Network.ApplySettings(document.Network);
        Localization.ApplySettings(document.Localization);
        Autopilot.ApplySettings(document.Autopilot);
        Customization.ApplySettings(document.Customization);
        UpdateOperationState();
    }

    private string? BuildDeployConfigurationJsonForCurrentMode()
    {
        if (!IsExpertMode)
        {
            return null;
        }

        return _deployConfigurationGenerator.Serialize(
            _deployConfigurationGenerator.Generate(BuildExpertConfigurationDocument()));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            RefreshUsbFormatModes();
            RefreshWinPeLanguageDisplayNames();
            RefreshExpertSections();
            OnPropertyChanged(nameof(CurrentCulture));
            OnPropertyChanged(nameof(Strings));
            OnPropertyChanged(nameof(GlobalOperationStatusDisplay));
            OnPropertyChanged(nameof(UsbDevicesCountDisplay));
            OnPropertyChanged(nameof(VersionDisplay));
            OnPropertyChanged(nameof(UsbPartitionStyleArm64Hint));
        });
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            OnPropertyChanged(nameof(GlobalOperationProgress));
            OnPropertyChanged(nameof(IsGlobalOperationInProgress));
            OnPropertyChanged(nameof(GlobalOperationStatusDisplay));
            UpdateOperationState();
        });
    }

    private void OnAdkStatusChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            UpdateAdkStatus();
            RefreshWinPeLanguages(preserveSelection: true);
        });
    }

    private void OnAdkOperationProgressChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(UpdateOperationState);
    }

    private void OnUsbDiskCandidatesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RunOnUiThread(() =>
        {
            OnPropertyChanged(nameof(UsbDevicesCountDisplay));
            UpdateOperationState();
        });
    }

    private void OnNetworkPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RunOnUiThread(UpdateOperationState);
    }

    private void UpdateAdkStatus()
    {
        IsAdkMissing = !_adkService.IsAdkInstalled;
        IsAdkIncompatible = _adkService.IsAdkInstalled && !_adkService.IsAdkCompatible;
        ShowAdkBanner = IsAdkMissing || IsAdkIncompatible;
        UpdateOperationState();
    }

    private void UpdateOperationState()
    {
        IsOperationInProgress = _operationProgressService.IsOperationInProgress;

        bool canCreate = _adkService.IsAdkCompatible && !IsOperationInProgress;
        bool networkSettingsValid = !Network.HasValidationError;
        CanCreateIso = canCreate &&
            networkSettingsValid &&
            SelectedWinPeLanguage is not null &&
            !string.IsNullOrWhiteSpace(IsoOutputPath) &&
            IsoOutputPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
        CanCreateUsb = canCreate &&
            networkSettingsValid &&
            SelectedWinPeLanguage is not null &&
            SelectedUsbDiskCandidate is not null;

        BrowseIsoOutputPathCommand.NotifyCanExecuteChanged();
        BrowseCustomDriverDirectoryCommand.NotifyCanExecuteChanged();
        ImportExpertConfigurationCommand.NotifyCanExecuteChanged();
        ExportExpertConfigurationCommand.NotifyCanExecuteChanged();
        ExportDeployConfigurationCommand.NotifyCanExecuteChanged();
        CreateIsoCommand.NotifyCanExecuteChanged();
        CreateUsbCommand.NotifyCanExecuteChanged();
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    private static bool IsVisualStudioDebugSession()
    {
#if DEBUG
        return Debugger.IsAttached;
#else
        return false;
#endif
    }
}
