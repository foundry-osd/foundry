using Foundry.Services.Localization;
using Foundry.Services.Shell;
using Microsoft.UI.Windowing;

namespace Foundry.Views
{
    public sealed partial class MainWindow : Window
    {
        private readonly IApplicationLocalizationService localizationService;
        private readonly IShellNavigationGuardService shellNavigationGuardService;
        private JsonNavigationService? jsonNavigationService;

        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            localizationService = App.GetService<IApplicationLocalizationService>();
            shellNavigationGuardService = App.GetService<IShellNavigationGuardService>();
            ViewModel = App.GetService<MainViewModel>();
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            ApplyLocalizedShellText();
            InitializeNavigation();
            ApplyShellNavigationState();

            localizationService.LanguageChanged += OnLanguageChanged;
            shellNavigationGuardService.StateChanged += OnShellNavigationStateChanged;
            NavFrame.Navigated += OnNavFrameNavigated;
            AppTitleBar.BackRequested += OnTitleBarBackRequested;
            AppTitleBar.PaneToggleRequested += OnTitleBarPaneToggleRequested;
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
                    .ConfigureJsonFile("Assets/NavViewMenu/AppData.json", OrderItemsType.None)
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
            ApplyShellNavigationState();

            Type? currentPageType = NavFrame.CurrentSourcePageType;
            if (currentPageType is not null)
            {
                NavFrame.Navigate(currentPageType);
                RefreshLocalizedBreadcrumbs();
                ApplyShellNavigationState();
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
            shellNavigationGuardService.StateChanged -= OnShellNavigationStateChanged;
            NavFrame.Navigated -= OnNavFrameNavigated;
            AppTitleBar.BackRequested -= OnTitleBarBackRequested;
            AppTitleBar.PaneToggleRequested -= OnTitleBarPaneToggleRequested;
            Closed -= OnClosed;
            ViewModel.Dispose();
        }

        private async void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            await App.Current.ThemeService.SetElementThemeWithoutSaveAsync();
        }

        private void OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (shellNavigationGuardService.State != ShellNavigationState.Ready)
            {
                sender.ItemsSource = null;
                return;
            }

            AutoSuggestBoxHelper.OnITitleBarAutoSuggestBoxTextChangedEvent(sender, args, NavFrame);
        }

        private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (shellNavigationGuardService.State != ShellNavigationState.Ready)
            {
                return;
            }

            AutoSuggestBoxHelper.OnITitleBarAutoSuggestBoxQuerySubmittedEvent(sender, args, NavFrame);
        }

        private void OnShellNavigationStateChanged(object? sender, EventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                _ = DispatcherQueue.TryEnqueue(ApplyShellNavigationState);
                return;
            }

            ApplyShellNavigationState();
        }

        private void OnNavFrameNavigated(object sender, NavigationEventArgs e)
        {
            ApplyShellNavigationState();
        }

        private void OnTitleBarBackRequested(TitleBar sender, object args)
        {
            if (shellNavigationGuardService.State == ShellNavigationState.OperationRunning)
            {
                return;
            }

            jsonNavigationService?.GoBack();
        }

        private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        private void UpdateInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            if (args.Reason == InfoBarCloseReason.CloseButton)
            {
                ViewModel.DismissUpdateBanner();
            }
        }

        private void UpdateInfoBarActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (shellNavigationGuardService.State == ShellNavigationState.OperationRunning)
            {
                return;
            }

            ViewModel.MarkUpdateBannerActionOpened();
            jsonNavigationService?.NavigateTo(
                typeof(AppUpdateSettingPage),
                localizationService.GetString("SettingsPage_UpdateCard.Header"));
        }

        private void ApplyShellNavigationState()
        {
            ShellNavigationState state = shellNavigationGuardService.State;
            bool isOperationRunning = state == ShellNavigationState.OperationRunning;
            SearchBox.IsEnabled = state == ShellNavigationState.Ready;
            OperationOverlay.Visibility = isOperationRunning ? Visibility.Visible : Visibility.Collapsed;
            OperationOverlayTitle.Text = localizationService.GetString("Shell.OperationRunning");

            ApplyNavigationItemsState(NavView.MenuItems, isFooter: false, state);
            ApplyNavigationItemsState(NavView.FooterMenuItems, isFooter: true, state);

            if (NavView.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.IsEnabled = !isOperationRunning;
            }

            NavView.IsBackEnabled = !isOperationRunning && NavFrame.CanGoBack;
            AppTitleBar.IsBackButtonVisible = !isOperationRunning && NavFrame.CanGoBack;
        }

        private static void ApplyNavigationItemsState(IList<object> items, bool isFooter, ShellNavigationState state)
        {
            foreach (object item in items)
            {
                ApplyNavigationItemState(item, isFooter, state);
            }
        }

        private static void ApplyNavigationItemState(object item, bool isFooter, ShellNavigationState state)
        {
            if (item is not NavigationViewItem navigationItem)
            {
                return;
            }

            navigationItem.IsEnabled = IsNavigationItemEnabled(navigationItem.Tag as string, isFooter, state);

            foreach (object child in navigationItem.MenuItems)
            {
                ApplyNavigationItemState(child, isFooter, state);
            }
        }

        private static bool IsNavigationItemEnabled(string? uniqueId, bool isFooter, ShellNavigationState state)
        {
            return state switch
            {
                ShellNavigationState.Ready => true,
                ShellNavigationState.OperationRunning => false,
                ShellNavigationState.AdkBlocked => isFooter
                    || string.Equals(uniqueId, typeof(HomeLandingPage).FullName, StringComparison.Ordinal)
                    || string.Equals(uniqueId, typeof(AdkPage).FullName, StringComparison.Ordinal),
                _ => false
            };
        }
    }

}
