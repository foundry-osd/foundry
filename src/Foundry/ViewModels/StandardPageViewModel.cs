using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Services.Adk;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public sealed partial class StandardPageViewModel : ObservableObject
{
    private readonly IAdkService _adkService;
    private readonly ILocalizationService _localizationService;

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
    private int operationProgress;

    [ObservableProperty]
    private string? operationStatus;

    public string Title => "Standard";

    public StringsWrapper Strings => _localizationService.Strings;

    public StandardPageViewModel(
        IAdkService adkService,
        ILocalizationService localizationService)
    {
        _adkService = adkService;
        _localizationService = localizationService;

        _adkService.AdkStatusChanged += OnAdkStatusChanged;
        _adkService.OperationProgressChanged += OnOperationProgressChanged;
        _localizationService.LanguageChanged += OnLanguageChanged;

        UpdateAdkStatus();
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
            // Handle error - could show a dialog
            System.Diagnostics.Debug.WriteLine($"Error downloading ADK: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Error installing ADK: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Error upgrading ADK: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Error uninstalling ADK: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateMedia))]
    private void CreateIso()
    {
        // TODO: Implement ISO creation
    }

    [RelayCommand(CanExecute = nameof(CanCreateMedia))]
    private void CreateUsb()
    {
        // TODO: Implement USB creation
    }

    private void UpdateAdkStatus()
    {
        IsAdkMissing = !_adkService.IsAdkInstalled;
        IsAdkIncompatible = _adkService.IsAdkInstalled && !_adkService.IsAdkCompatible;
        ShowAdkBanner = IsAdkMissing || IsAdkIncompatible;
        CanCreateMedia = _adkService.IsAdkCompatible;

        CreateIsoCommand.NotifyCanExecuteChanged();
        CreateUsbCommand.NotifyCanExecuteChanged();
    }

    private void OnAdkStatusChanged(object? sender, EventArgs e)
    {
        UpdateAdkStatus();
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        IsOperationInProgress = _adkService.IsOperationInProgress;
        OperationProgress = _adkService.OperationProgress;
        OperationStatus = _adkService.OperationStatus;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Strings));
    }
}
