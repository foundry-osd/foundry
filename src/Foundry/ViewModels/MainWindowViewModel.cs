using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Services.Adk;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.Services.WinPe;

namespace Foundry.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string DefaultIsoFileName = "foundry-winpe.iso";
    private const string IsoVolumeLabel = "FOUNDRY_WINPE";
    private const string AppVersion = "1.0.0.0";

    private readonly IApplicationShellService _applicationShellService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly IOperationProgressService _operationProgressService;
    private readonly IAdkService _adkService;
    private readonly IMediaOutputService _mediaOutputService;

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
    private bool useCa2023;

    [ObservableProperty]
    private UsbPartitionStyle selectedPartitionStyle = UsbPartitionStyle.Gpt;

    [ObservableProperty]
    private bool includeDellDrivers;

    [ObservableProperty]
    private bool includeHpDrivers;

    [ObservableProperty]
    private string startupBootstrapScriptPath = string.Empty;

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

    public ObservableCollection<WinPeUsbDiskCandidate> UsbDiskCandidates { get; } = [];

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

    private static string StagingDirectoryPath => Path.Combine(Path.GetTempPath(), "FoundryMedia");

    public MainWindowViewModel(
        IApplicationShellService applicationShellService,
        IThemeService themeService,
        ILocalizationService localizationService,
        IOperationProgressService operationProgressService,
        IAdkService adkService,
        IMediaOutputService mediaOutputService)
    {
        _applicationShellService = applicationShellService;
        _themeService = themeService;
        _localizationService = localizationService;
        _operationProgressService = operationProgressService;
        _adkService = adkService;
        _mediaOutputService = mediaOutputService;

        _localizationService.LanguageChanged += OnLanguageChanged;
        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _adkService.AdkStatusChanged += OnAdkStatusChanged;
        _adkService.OperationProgressChanged += OnAdkOperationProgressChanged;
        UsbDiskCandidates.CollectionChanged += OnUsbDiskCandidatesCollectionChanged;

        UpdateAdkStatus();
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

    [RelayCommand]
    private async Task InstallAdkAsync()
    {
        try
        {
            await _adkService.InstallAdkAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error installing ADK: {ex.Message}");
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
            Debug.WriteLine($"Error upgrading ADK: {ex.Message}");
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
            Debug.WriteLine(MediaActionMessage);
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
                DriverVendors = GetSelectedDriverVendors(),
                StartupBootstrapScriptPath = string.IsNullOrWhiteSpace(StartupBootstrapScriptPath) ? null : StartupBootstrapScriptPath,
                RunPca2023RemediationWhenBootExUnsupported = EnablePcaRemediation,
                Pca2023RemediationScriptPath = string.IsNullOrWhiteSpace(PcaRemediationScriptPath) ? null : PcaRemediationScriptPath
            });

            MediaActionMessage = result.IsSuccess
                ? Strings["IsoCreatedSuccessMessage"]
                : string.Format(CurrentCulture, Strings["IsoFailedMessageFormat"], result.Error?.Code, result.Error?.Message);

            if (!result.IsSuccess && result.Error is not null)
            {
                Debug.WriteLine($"Create ISO failed [{result.Error.Code}] {result.Error.Message} | {result.Error.Details}");
            }
        }
        catch (Exception ex)
        {
            MediaActionMessage = string.Format(CurrentCulture, Strings["IsoCreateErrorFormat"], ex.Message);
            Debug.WriteLine(MediaActionMessage);
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
                Architecture = SelectedArchitecture,
                SignatureMode = GetSignatureMode(),
                DriverVendors = GetSelectedDriverVendors(),
                StartupBootstrapScriptPath = string.IsNullOrWhiteSpace(StartupBootstrapScriptPath) ? null : StartupBootstrapScriptPath,
                RunPca2023RemediationWhenBootExUnsupported = EnablePcaRemediation,
                Pca2023RemediationScriptPath = string.IsNullOrWhiteSpace(PcaRemediationScriptPath) ? null : PcaRemediationScriptPath
            });

            MediaActionMessage = result.IsSuccess
                ? Strings["UsbCreatedSuccessMessage"]
                : string.Format(CurrentCulture, Strings["UsbFailedMessageFormat"], result.Error?.Code, result.Error?.Message);

            if (!result.IsSuccess && result.Error is not null)
            {
                Debug.WriteLine($"Create USB failed [{result.Error.Code}] {result.Error.Message} | {result.Error.Details}");
            }
        }
        catch (Exception ex)
        {
            MediaActionMessage = string.Format(CurrentCulture, Strings["UsbCreateErrorFormat"], ex.Message);
            Debug.WriteLine(MediaActionMessage);
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

    private bool CanBrowseIsoOutputPath()
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

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentCulture));
        OnPropertyChanged(nameof(Strings));
        OnPropertyChanged(nameof(GlobalOperationStatusDisplay));
        OnPropertyChanged(nameof(UsbDevicesCountDisplay));
        OnPropertyChanged(nameof(VersionDisplay));
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(GlobalOperationProgress));
        OnPropertyChanged(nameof(IsGlobalOperationInProgress));
        OnPropertyChanged(nameof(GlobalOperationStatusDisplay));
        UpdateOperationState();
    }

    private void OnAdkStatusChanged(object? sender, EventArgs e)
    {
        UpdateAdkStatus();
    }

    private void OnAdkOperationProgressChanged(object? sender, EventArgs e)
    {
        UpdateOperationState();
    }

    private void OnUsbDiskCandidatesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(UsbDevicesCountDisplay));
        UpdateOperationState();
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
            !string.IsNullOrWhiteSpace(IsoOutputPath) &&
            IsoOutputPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
        CanCreateUsb = canCreate && SelectedUsbDiskCandidate is not null;

        BrowseIsoOutputPathCommand.NotifyCanExecuteChanged();
        CreateIsoCommand.NotifyCanExecuteChanged();
        CreateUsbCommand.NotifyCanExecuteChanged();
    }
}
