using Foundry.Services.Localization;
using Microsoft.UI.Windowing;

namespace Foundry.Views
{
    public sealed partial class MainWindow : Window
    {
        private readonly IApplicationLocalizationService localizationService;
        private JsonNavigationService? jsonNavigationService;

        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            localizationService = App.GetService<IApplicationLocalizationService>();
            ViewModel = App.GetService<MainViewModel>();
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            ApplyLocalizedShellText();
            InitializeNavigation();

            localizationService.LanguageChanged += OnLanguageChanged;
            Closed += OnClosed;
        }

        private void InitializeNavigation()
        {
            jsonNavigationService = App.GetService<IJsonNavigationService>() as JsonNavigationService;
            if (jsonNavigationService != null)
            {
                jsonNavigationService.Initialize(NavView, NavFrame, NavigationPageMappings.PageDictionary)
                    .ConfigureDefaultPage(typeof(HomeLandingPage))
                    .ConfigureSettingsPage(typeof(SettingsPage))
                    .ConfigureJsonFile("Assets/NavViewMenu/AppData.json")
                    .ConfigureTitleBar(AppTitleBar)
                    .ConfigureBreadcrumbBar(BreadCrumbNav, BreadcrumbPageMappings.PageDictionary);
            }
        }

        private void ApplyLocalizedShellText()
        {
            SearchBox.PlaceholderText = localizationService.GetString("MainWindow.SearchBox.PlaceholderText");
            ToolTipService.SetToolTip(ThemeButton, localizationService.GetString("MainWindow.ThemeButton.ToolTip"));

            if (NavView.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.Content = localizationService.GetString("SettingsPage.PageTitle");
            }
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                _ = DispatcherQueue.TryEnqueue(RefreshLocalizedShell);
                return;
            }

            RefreshLocalizedShell();
        }

        private void RefreshLocalizedShell()
        {
            jsonNavigationService?.ReInitialize();
            ApplyLocalizedShellText();
            RefreshLocalizedBreadcrumbs();

            Type? currentPageType = NavFrame.CurrentSourcePageType;
            if (currentPageType is not null)
            {
                NavFrame.Navigate(currentPageType);
                RefreshLocalizedBreadcrumbs();
            }
        }

        private void RefreshLocalizedBreadcrumbs()
        {
            if (BreadCrumbNav.BreadCrumbs is null || BreadCrumbNav.BreadCrumbs.Count == 0)
            {
                return;
            }

            BreadCrumbNav.BreadCrumbs = new(BreadCrumbNav.BreadCrumbs.Select(step =>
                new BreadcrumbStep(GetLocalizedBreadcrumbLabel(step), step.Page, step.Parameter)));
        }

        private string GetLocalizedBreadcrumbLabel(BreadcrumbStep step)
        {
            if (step.Page == typeof(SettingsPage))
            {
                return localizationService.GetString("SettingsPage.PageTitle");
            }

            if (step.Page == typeof(GeneralSettingPage))
            {
                return localizationService.GetString("SettingsPage_GeneralCard.Header");
            }

            if (step.Page == typeof(ThemeSettingPage))
            {
                return localizationService.GetString("SettingsPage_ThemeCard.Header");
            }

            if (step.Page == typeof(AppUpdateSettingPage))
            {
                return localizationService.GetString("SettingsPage_UpdateCard.Header");
            }

            if (step.Page == typeof(AboutUsSettingPage))
            {
                return localizationService.GetString("SettingsPage_AboutCard.Header");
            }

            return step.Label;
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            localizationService.LanguageChanged -= OnLanguageChanged;
            Closed -= OnClosed;
        }

        private async void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            await App.Current.ThemeService.SetElementThemeWithoutSaveAsync();
        }

        private void OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            AutoSuggestBoxHelper.OnITitleBarAutoSuggestBoxTextChangedEvent(sender, args, NavFrame);
        }

        private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            AutoSuggestBoxHelper.OnITitleBarAutoSuggestBoxQuerySubmittedEvent(sender, args, NavFrame);
        }
    }

}
