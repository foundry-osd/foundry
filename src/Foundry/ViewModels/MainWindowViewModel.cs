using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Services;

namespace Foundry.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IApplicationShellService _applicationShellService;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private bool isAdvancedEnabled;

    public MainWindowViewModel(
        IApplicationShellService applicationShellService,
        IThemeService themeService)
    {
        _applicationShellService = applicationShellService;
        _themeService = themeService;
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
    }

    [RelayCommand]
    private void SetLightTheme()
    {
        _themeService.SetTheme(ThemeMode.Light);
    }

    [RelayCommand]
    private void SetDarkTheme()
    {
        _themeService.SetTheme(ThemeMode.Dark);
    }
}
