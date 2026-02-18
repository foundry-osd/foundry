using System.Collections.ObjectModel;
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
    private bool canCreateMedia;

    [ObservableProperty]
    private bool isOperationInProgress;

    [ObservableProperty]
    private string stagingDirectoryPath = Path.Combine(Path.GetTempPath(), "FoundryMedia");

    [ObservableProperty]
    private string isoOutputPath = Path.Combine(Path.GetTempPath(), "foundry-winpe.iso");

    [ObservableProperty]
    private string isoVolumeLabel = "FOUNDRY_WINPE";

    [ObservableProperty]
    private WinPeArchitecture selectedArchitecture = WinPeArchitecture.X64;

    [ObservableProperty]
    private WinPeSignatureMode selectedSignatureMode = WinPeSignatureMode.Pca2023;

    [ObservableProperty]
    private UsbPartitionStyle selectedPartitionStyle = UsbPartitionStyle.Gpt;

    [ObservableProperty]
    private WinPeVendorSelection selectedVendor = WinPeVendorSelection.Any;

    [ObservableProperty]
    private bool includeDrivers = true;

    [ObservableProperty]
    private bool includePreviewDrivers;

    [ObservableProperty]
    private string usbBootDriveLetter = "Z:";

    [ObservableProperty]
    private string startupBootstrapScriptPath = string.Empty;

    [ObservableProperty]
    private bool enablePcaRemediation;

    [ObservableProperty]
    private string pcaRemediationScriptPath = string.Empty;

    [ObservableProperty]
    private string usbConfirmationCode = string.Empty;

    [ObservableProperty]
    private string usbConfirmationCodeRepeat = string.Empty;

    [ObservableProperty]
    private WinPeUsbDiskCandidate? selectedUsbDiskCandidate;

    [ObservableProperty]
    private string mediaActionMessage = string.Empty;

    [ObservableProperty]
    private bool isRefreshingUsbCandidates;

    public ObservableCollection<WinPeUsbDiskCandidate> UsbDiskCandidates { get; } = [];

    public IReadOnlyList<WinPeArchitecture> AvailableArchitectures { get; } = Enum.GetValues<WinPeArchitecture>();
    public IReadOnlyList<WinPeSignatureMode> AvailableSignatureModes { get; } = Enum.GetValues<WinPeSignatureMode>();
    public IReadOnlyList<UsbPartitionStyle> AvailablePartitionStyles { get; } = Enum.GetValues<UsbPartitionStyle>();
    public IReadOnlyList<WinPeVendorSelection> AvailableVendors { get; } = Enum.GetValues<WinPeVendorSelection>();

    public ILocalizationService LocalizationService => _localizationService;
    public CultureInfo CurrentCulture => _localizationService.CurrentCulture;
    public ThemeMode CurrentTheme => _themeService.CurrentTheme;
    public StringsWrapper Strings => _localizationService.Strings;
    public int GlobalOperationProgress => _operationProgressService.Progress;
    public bool IsGlobalOperationInProgress => _operationProgressService.IsOperationInProgress;
    public string GlobalOperationStatusDisplay =>
        _operationProgressService.Status ??
        (IsGlobalOperationInProgress ? Strings["OperationInProgress"] : Strings["OperationReady"]);
    public string ExpectedUsbConfirmationCode =>
        SelectedUsbDiskCandidate is null
            ? string.Empty
            : $"ERASE-DISK-{SelectedUsbDiskCandidate.DiskNumber}";

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

        Directory.CreateDirectory(StagingDirectoryPath);

        _localizationService.LanguageChanged += OnLanguageChanged;
        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _adkService.AdkStatusChanged += OnAdkStatusChanged;
        _adkService.OperationProgressChanged += OnAdkOperationProgressChanged;

        UpdateAdkStatus();
        UpdateOperationState();
        _ = RefreshUsbCandidatesAsync();
    }

    public void Dispose()
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;
        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
        _adkService.AdkStatusChanged -= OnAdkStatusChanged;
        _adkService.OperationProgressChanged -= OnAdkOperationProgressChanged;
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

    [RelayCommand]
    private async Task DownloadAdkAsync()
    {
        try
        {
            await _adkService.DownloadAdkAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error downloading ADK: {ex.Message}");
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
    private async Task UninstallAdkAsync()
    {
        try
        {
            await _adkService.UninstallAdkAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error uninstalling ADK: {ex.Message}");
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
                return;
            }

            foreach (WinPeUsbDiskCandidate candidate in result.Value)
            {
                UsbDiskCandidates.Add(candidate);
            }

            SelectedUsbDiskCandidate = UsbDiskCandidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            MediaActionMessage = $"USB disk refresh failed: {ex.Message}";
            Debug.WriteLine(MediaActionMessage);
        }
        finally
        {
            IsRefreshingUsbCandidates = false;
            OnPropertyChanged(nameof(ExpectedUsbConfirmationCode));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateMedia))]
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
                SignatureMode = SelectedSignatureMode,
                Vendor = SelectedVendor,
                IncludeDrivers = IncludeDrivers,
                IncludePreviewDrivers = IncludePreviewDrivers,
                StartupBootstrapScriptPath = string.IsNullOrWhiteSpace(StartupBootstrapScriptPath) ? null : StartupBootstrapScriptPath,
                RunPca2023RemediationWhenBootExUnsupported = EnablePcaRemediation,
                Pca2023RemediationScriptPath = string.IsNullOrWhiteSpace(PcaRemediationScriptPath) ? null : PcaRemediationScriptPath
            });

            MediaActionMessage = result.IsSuccess
                ? "ISO created successfully."
                : $"ISO failed: {result.Error?.Code} {result.Error?.Message}";

            if (!result.IsSuccess && result.Error is not null)
            {
                Debug.WriteLine($"Create ISO failed [{result.Error.Code}] {result.Error.Message} | {result.Error.Details}");
            }
        }
        catch (Exception ex)
        {
            MediaActionMessage = $"Error creating ISO: {ex.Message}";
            Debug.WriteLine(MediaActionMessage);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateMedia))]
    private async Task CreateUsb()
    {
        try
        {
            Directory.CreateDirectory(StagingDirectoryPath);

            WinPeResult result = await _mediaOutputService.CreateUsbAsync(new UsbOutputOptions
            {
                StagingDirectoryPath = StagingDirectoryPath,
                TargetDriveLetter = UsbBootDriveLetter,
                TargetDiskNumber = SelectedUsbDiskCandidate?.DiskNumber,
                ExpectedDiskFriendlyName = SelectedUsbDiskCandidate?.FriendlyName ?? string.Empty,
                ExpectedDiskSerialNumber = SelectedUsbDiskCandidate?.SerialNumber ?? string.Empty,
                ExpectedDiskUniqueId = SelectedUsbDiskCandidate?.UniqueId ?? string.Empty,
                ConfirmationCode = UsbConfirmationCode,
                ConfirmationCodeRepeat = UsbConfirmationCodeRepeat,
                PartitionStyle = SelectedPartitionStyle,
                Architecture = SelectedArchitecture,
                SignatureMode = SelectedSignatureMode,
                Vendor = SelectedVendor,
                IncludeDrivers = IncludeDrivers,
                IncludePreviewDrivers = IncludePreviewDrivers,
                StartupBootstrapScriptPath = string.IsNullOrWhiteSpace(StartupBootstrapScriptPath) ? null : StartupBootstrapScriptPath,
                RunPca2023RemediationWhenBootExUnsupported = EnablePcaRemediation,
                Pca2023RemediationScriptPath = string.IsNullOrWhiteSpace(PcaRemediationScriptPath) ? null : PcaRemediationScriptPath
            });

            MediaActionMessage = result.IsSuccess
                ? "USB created successfully."
                : $"USB failed: {result.Error?.Code} {result.Error?.Message}";

            if (!result.IsSuccess && result.Error is not null)
            {
                Debug.WriteLine($"Create USB failed [{result.Error.Code}] {result.Error.Message} | {result.Error.Details}");
            }
        }
        catch (Exception ex)
        {
            MediaActionMessage = $"Error creating USB: {ex.Message}";
            Debug.WriteLine(MediaActionMessage);
        }
    }

    partial void OnSelectedUsbDiskCandidateChanged(WinPeUsbDiskCandidate? value)
    {
        OnPropertyChanged(nameof(ExpectedUsbConfirmationCode));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentCulture));
        OnPropertyChanged(nameof(Strings));
        OnPropertyChanged(nameof(GlobalOperationStatusDisplay));
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
        CanCreateMedia = _adkService.IsAdkCompatible && !IsOperationInProgress;

        CreateIsoCommand.NotifyCanExecuteChanged();
        CreateUsbCommand.NotifyCanExecuteChanged();
    }
}
