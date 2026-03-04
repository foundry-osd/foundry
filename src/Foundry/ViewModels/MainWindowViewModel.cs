using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Services.Adk;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.Services.WinPe;
using Microsoft.Extensions.Logging;

namespace Foundry.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string DefaultIsoFileName = "foundry-winpe.iso";
    private const string IsoVolumeLabel = "FOUNDRY_WINPE";
    private static readonly string AppVersion = ResolveAppVersion();

    private readonly IApplicationShellService _applicationShellService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly IOperationProgressService _operationProgressService;
    private readonly IAdkService _adkService;
    private readonly IMediaOutputService _mediaOutputService;
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
    private bool enablePcaRemediation;

    [ObservableProperty]
    private string pcaRemediationScriptPath = string.Empty;

    [ObservableProperty]
    private WinPeUsbDiskCandidate? selectedUsbDiskCandidate;

    [ObservableProperty]
    private string mediaActionMessage = string.Empty;

    [ObservableProperty]
    private bool isRefreshingUsbCandidates;

    [ObservableProperty]
    private WinPeLanguageOption? selectedWinPeLanguage;

    public ObservableCollection<WinPeUsbDiskCandidate> UsbDiskCandidates { get; } = [];
    public ObservableCollection<WinPeLanguageOption> AvailableWinPeLanguages { get; } = [];
    public ObservableCollection<UsbFormatModeOption> AvailableUsbFormatModes { get; } = [];

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
        (IsGlobalOperationInProgress ? Strings["OperationInProgress"] : Strings["OperationReady"]);
    public string UsbDevicesCountDisplay =>
        string.Format(CurrentCulture, Strings["UsbDevicesCountFormat"], UsbDiskCandidates.Count);
    public string VersionDisplay =>
        string.Format(CurrentCulture, Strings["VersionFormat"], AppVersion);
    public bool ShowUsbPartitionStyleArm64Hint => SelectedArchitecture == WinPeArchitecture.Arm64;
    public string UsbPartitionStyleArm64Hint => Strings["UsbPartitionStyleArm64Hint"];

    private static string StagingDirectoryPath => Path.Combine(Path.GetTempPath(), "FoundryMedia");

    private static string ResolveAppVersion()
    {
        Assembly assembly = typeof(MainWindowViewModel).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Trim();
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }

    public MainWindowViewModel(
        IApplicationShellService applicationShellService,
        IThemeService themeService,
        ILocalizationService localizationService,
        IOperationProgressService operationProgressService,
        IAdkService adkService,
        IMediaOutputService mediaOutputService,
        ILogger<MainWindowViewModel> logger)
    {
        _applicationShellService = applicationShellService;
        _themeService = themeService;
        _localizationService = localizationService;
        _operationProgressService = operationProgressService;
        _adkService = adkService;
        _mediaOutputService = mediaOutputService;
        _logger = logger;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _localizationService.LanguageChanged += OnLanguageChanged;
        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _adkService.AdkStatusChanged += OnAdkStatusChanged;
        _adkService.OperationProgressChanged += OnAdkOperationProgressChanged;
        UsbDiskCandidates.CollectionChanged += OnUsbDiskCandidatesCollectionChanged;

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
            Strings["CustomDriverPathPickerTitle"],
            string.IsNullOrWhiteSpace(CustomDriverDirectoryPath) ? null : CustomDriverDirectoryPath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            CustomDriverDirectoryPath = selectedPath;
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
            MediaActionMessage = string.Format(CurrentCulture, Strings["UsbDiskRefreshFailedFormat"], ex.Message);
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
            Directory.CreateDirectory(StagingDirectoryPath);

            WinPeResult result = await _mediaOutputService.CreateIsoAsync(new IsoOutputOptions
            {
                StagingDirectoryPath = StagingDirectoryPath,
                OutputIsoPath = IsoOutputPath,
                VolumeLabel = IsoVolumeLabel,
                Architecture = SelectedArchitecture,
                SignatureMode = GetSignatureMode(),
                WinPeLanguage = SelectedWinPeLanguage?.Code ?? string.Empty,
                DriverVendors = GetSelectedDriverVendors(),
                CustomDriverDirectoryPath = NormalizeCustomDriverDirectoryPath(),
                RunPca2023RemediationWhenBootExUnsupported = EnablePcaRemediation,
                Pca2023RemediationScriptPath = string.IsNullOrWhiteSpace(PcaRemediationScriptPath) ? null : PcaRemediationScriptPath
            });

            MediaActionMessage = result.IsSuccess
                ? Strings["IsoCreatedSuccessMessage"]
                : string.Format(CurrentCulture, Strings["IsoFailedMessageFormat"], result.Error?.Code, result.Error?.Message);

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
            MediaActionMessage = string.Format(CurrentCulture, Strings["IsoCreateErrorFormat"], ex.Message);
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
            Strings["UsbWarningMessage"],
            SelectedUsbDiskCandidate.DiskNumber,
            SelectedUsbDiskCandidate.FriendlyName,
            diskSizeGb.ToString("F1", CultureInfo.CurrentCulture));

        bool confirmed = _applicationShellService.ConfirmWarning(Strings["UsbWarningTitle"], warningMessage);
        if (!confirmed)
        {
            MediaActionMessage = Strings["UsbWarningCancelled"];
            return;
        }

        try
        {
            Directory.CreateDirectory(StagingDirectoryPath);

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
                WinPeLanguage = SelectedWinPeLanguage?.Code ?? string.Empty,
                DriverVendors = GetSelectedDriverVendors(),
                CustomDriverDirectoryPath = NormalizeCustomDriverDirectoryPath(),
                RunPca2023RemediationWhenBootExUnsupported = EnablePcaRemediation,
                Pca2023RemediationScriptPath = string.IsNullOrWhiteSpace(PcaRemediationScriptPath) ? null : PcaRemediationScriptPath
            });

            MediaActionMessage = result.IsSuccess
                ? Strings["UsbCreatedSuccessMessage"]
                : string.Format(CurrentCulture, Strings["UsbFailedMessageFormat"], result.Error?.Code, result.Error?.Message);

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
            MediaActionMessage = string.Format(CurrentCulture, Strings["UsbCreateErrorFormat"], ex.Message);
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

    private bool CanBrowseIsoOutputPath()
    {
        return !IsOperationInProgress;
    }

    private bool CanBrowseCustomDriverDirectory()
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

    private void EnforcePartitionStyleForArchitecture(bool showInfoMessage)
    {
        if (SelectedArchitecture != WinPeArchitecture.Arm64 || SelectedPartitionStyle != UsbPartitionStyle.Mbr)
        {
            return;
        }

        SelectedPartitionStyle = UsbPartitionStyle.Gpt;
        if (showInfoMessage)
        {
            MediaActionMessage = Strings["UsbPartitionStyleArm64AutoSetMessage"];
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
            UsbFormatMode.Quick => Strings["UsbFormatModeQuick"],
            UsbFormatMode.Complete => Strings["UsbFormatModeComplete"],
            _ => mode.ToString()
        };
    }

    private static WinPeLanguageOption CreateWinPeLanguageOption(string languageCode)
    {
        string normalizedCode = NormalizeLanguageCode(languageCode);

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(normalizedCode);
            return new WinPeLanguageOption(normalizedCode, $"{culture.EnglishName} ({culture.Name})");
        }
        catch (CultureNotFoundException)
        {
            return new WinPeLanguageOption(normalizedCode, normalizedCode);
        }
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

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            RefreshUsbFormatModes();
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
        CanCreateIso = canCreate &&
            SelectedWinPeLanguage is not null &&
            !string.IsNullOrWhiteSpace(IsoOutputPath) &&
            IsoOutputPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
        CanCreateUsb = canCreate &&
            SelectedWinPeLanguage is not null &&
            SelectedUsbDiskCandidate is not null;

        BrowseIsoOutputPathCommand.NotifyCanExecuteChanged();
        BrowseCustomDriverDirectoryCommand.NotifyCanExecuteChanged();
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
}
