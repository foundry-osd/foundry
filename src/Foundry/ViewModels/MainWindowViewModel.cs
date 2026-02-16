using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;

namespace Foundry.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationShellService _applicationShellService;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly IOperationProgressService _operationProgressService;

    [ObservableProperty]
    private bool isAdvancedEnabled;

    public ILocalizationService LocalizationService => _localizationService;
    public CultureInfo CurrentCulture => _localizationService.CurrentCulture;
    public ThemeMode CurrentTheme => _themeService.CurrentTheme;
    public StringsWrapper Strings => _localizationService.Strings;
    public int GlobalOperationProgress => _operationProgressService.Progress;
    public bool IsGlobalOperationInProgress => _operationProgressService.IsOperationInProgress;
    public string GlobalOperationStatusDisplay =>
        _operationProgressService.Status ??
        (IsGlobalOperationInProgress ? Strings["OperationInProgress"] : Strings["OperationReady"]);

    public MainWindowViewModel(
        IApplicationShellService applicationShellService,
        IThemeService themeService,
        ILocalizationService localizationService,
        IOperationProgressService operationProgressService)
    {
        _applicationShellService = applicationShellService;
        _themeService = themeService;
        _localizationService = localizationService;
        _operationProgressService = operationProgressService;

        _localizationService.LanguageChanged += OnLanguageChanged;
        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
    }

    public void Dispose()
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;
        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
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
    }
}
