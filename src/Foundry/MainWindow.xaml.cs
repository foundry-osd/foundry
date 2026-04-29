using Foundry.Services.ApplicationShell;
using Foundry.Services.Theme;
using Foundry.ViewModels;
using Foundry.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Foundry;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IThemeService _themeService;

    public MainWindow(
        MainWindowViewModel viewModel,
        ApplicationShellService applicationShellService,
        IThemeService themeService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _themeService = themeService;
        applicationShellService.AttachMainWindow(this);

        Root.DataContext = viewModel;
        Root.Loaded += OnLoadedAsync;
        Closed += OnClosed;
        _themeService.ThemeChanged += OnThemeChanged;
        ApplyTheme(_themeService.CurrentTheme);
        NavigateTo(GetInitialPageTag());
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnLoadedAsync;
        await _viewModel.RefreshUsbCandidatesCommand.ExecuteAsync(null);
        await _viewModel.RunStartupUpdateCheckAsync();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item ||
            item.Tag is not string tag)
        {
            return;
        }

        if (string.Equals(tag, "About", StringComparison.Ordinal))
        {
            _viewModel.ShowAboutCommand.Execute(null);
            sender.SelectedItem = null;
            return;
        }

        NavigateTo(tag);
    }

    private void NavigateTo(string tag)
    {
        Type pageType = tag switch
        {
            "Adk" => typeof(AdkPage),
            "Configuration" => typeof(ConfigurationPage),
            "Start" => typeof(StartPage),
            "Network" => typeof(NetworkPage),
            "Localization" => typeof(LocalizationPage),
            "Autopilot" => typeof(AutopilotPage),
            "Customization" => typeof(CustomizationPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        object dataContext = tag switch
        {
            "Network" => _viewModel.Network,
            "Localization" => _viewModel.Localization,
            "Autopilot" => _viewModel.Autopilot,
            "Customization" => _viewModel.Customization,
            _ => _viewModel
        };

        ContentFrame.Navigate(pageType);
        if (ContentFrame.Content is FrameworkElement page)
        {
            page.DataContext = dataContext;
        }
    }

    private static string GetInitialPageTag()
    {
#if DEBUG
        string? tag = Environment.GetEnvironmentVariable("FOUNDRY_INITIAL_PAGE");
        return string.IsNullOrWhiteSpace(tag) ? "Home" : tag;
#else
        return "Home";
#endif
    }

    private void OnThemeChanged(object? sender, ThemeMode theme)
    {
        ApplyTheme(theme);
    }

    private void ApplyTheme(ThemeMode theme)
    {
        Root.RequestedTheme = theme switch
        {
            ThemeMode.Light => ElementTheme.Light,
            ThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
        _viewModel.Dispose();
    }
}
