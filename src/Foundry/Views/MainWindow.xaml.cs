using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Shell;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Serilog;

namespace Foundry.Views
{
    public sealed partial class MainWindow : Window
    {
        private readonly IApplicationLocalizationService localizationService;
        private readonly IOperationProgressService operationProgressService;
        private readonly IShellNavigationGuardService shellNavigationGuardService;
        private readonly IExternalProcessLauncher externalProcessLauncher;
        private readonly ILogger logger = Log.ForContext<MainWindow>();
        private const string DocumentationNavigationTag = "Foundry.External.Documentation";
        private const string AboutNavigationTag = "Foundry.External.About";
        private ContentDialog? operationDialog;
        private bool isClosingOperationDialog;
        private JsonNavigationService? jsonNavigationService;
        private TextBlock? operationStatusText;
        private ProgressBar? operationProgressBar;
        private TextBlock? operationProgressPercentText;
        private StackPanel? operationSecondaryProgressPanel;
        private TextBlock? operationSecondaryStatusText;
        private ProgressBar? operationSecondaryProgressBar;
        private TextBlock? operationSecondaryProgressPercentText;

        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            localizationService = App.GetService<IApplicationLocalizationService>();
            operationProgressService = App.GetService<IOperationProgressService>();
            shellNavigationGuardService = App.GetService<IShellNavigationGuardService>();
            externalProcessLauncher = App.GetService<IExternalProcessLauncher>();
            ViewModel = App.GetService<MainViewModel>();
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            InitializeNavigation();
            ApplyLocalizedShellText();
            ApplyShellNavigationState();

            localizationService.LanguageChanged += OnLanguageChanged;
            operationProgressService.StateChanged += OnOperationProgressChanged;
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

            EnsureExternalDocumentationFooterItem();
            EnsureExternalAboutFooterItem();
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                if (!DispatcherQueue.TryEnqueue(RefreshLocalizedShell))
                {
                    logger.Warning(
                        "Failed to enqueue shell localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                        e.OldLanguage,
                        e.NewLanguage);
                }

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

            if (step.Page == typeof(HomeLandingPage))
            {
                return localizationService.GetString("Nav_HomeKey.Title");
            }

            if (step.Page == typeof(AdkPage))
            {
                return localizationService.GetString("Adk.PageTitle");
            }

            if (step.Page == typeof(GeneralConfigurationPage))
            {
                return localizationService.GetString("GeneralConfigurationPage_Title.Text");
            }

            if (step.Page == typeof(StartPage))
            {
                return localizationService.GetString("StartPage_Title.Text");
            }

            return step.Label;
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            localizationService.LanguageChanged -= OnLanguageChanged;
            operationProgressService.StateChanged -= OnOperationProgressChanged;
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
                if (!DispatcherQueue.TryEnqueue(ApplyShellNavigationState))
                {
                    logger.Warning(
                        "Failed to enqueue shell navigation state refresh. State={State}",
                        shellNavigationGuardService.State);
                }

                return;
            }

            ApplyShellNavigationState();
        }

        private void OnOperationProgressChanged(object? sender, OperationProgressChangedEventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => ApplyOperationState(e.State));
                return;
            }

            ApplyOperationState(e.State);
        }

        private void OnNavFrameNavigated(object sender, NavigationEventArgs e)
        {
            SynchronizeSelectedNavigationItem(e.SourcePageType);
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
            UpdateOperationDialog(isOperationRunning);

            ApplyNavigationItemsState(NavView.MenuItems, isFooter: false, state);
            ApplyNavigationItemsState(NavView.FooterMenuItems, isFooter: true, state);

            NavView.IsBackEnabled = !isOperationRunning && NavFrame.CanGoBack;
            AppTitleBar.IsBackButtonVisible = !isOperationRunning && NavFrame.CanGoBack;
        }

        private void UpdateOperationDialog(bool isOperationRunning)
        {
            if (isOperationRunning)
            {
                ShowOperationDialog();
                return;
            }

            HideOperationDialog();
        }

        private async void ShowOperationDialog()
        {
            if (operationDialog is not null)
            {
                return;
            }

            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = localizationService.GetString("Shell.OperationRunning"),
                Content = CreateOperationDialogContent(),
                DefaultButton = ContentDialogButton.None
            };

            dialog.Closing += OnOperationDialogClosing;
            operationDialog = dialog;

            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                dialog.Closing -= OnOperationDialogClosing;
                if (ReferenceEquals(operationDialog, dialog))
                {
                    operationDialog = null;
                }

                isClosingOperationDialog = false;
            }
        }

        private FrameworkElement CreateOperationDialogContent()
        {
            operationStatusText = new TextBlock
            {
                Text = GetOperationDialogStatusText(),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Left
            };

            operationProgressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = operationProgressService.State.Progress,
                Height = 4
            };

            operationProgressPercentText = CreatePercentText(operationProgressService.State.Progress);

            operationSecondaryStatusText = new TextBlock
            {
                Text = GetSecondaryOperationDialogStatusText(),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Left
            };

            operationSecondaryProgressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 4,
                Value = operationProgressService.State.SecondaryProgress ?? 0,
                IsIndeterminate = !operationProgressService.State.SecondaryProgress.HasValue
            };

            operationSecondaryProgressPercentText = CreatePercentText(operationProgressService.State.SecondaryProgress);
            operationSecondaryProgressPanel = new StackPanel
            {
                Spacing = 8,
                Visibility = operationProgressService.State.HasSecondaryProgress ? Visibility.Visible : Visibility.Collapsed,
                Children =
                {
                    operationSecondaryStatusText,
                    CreateProgressRow(operationSecondaryProgressBar, operationSecondaryProgressPercentText)
                }
            };

            return new StackPanel
            {
                MinWidth = 420,
                MaxWidth = 520,
                Padding = new Thickness(0, 8, 0, 0),
                Spacing = 16,
                Children =
                {
                    new Microsoft.UI.Xaml.Controls.ProgressRing
                    {
                        Width = 56,
                        Height = 56,
                        IsActive = true,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    operationStatusText,
                    CreateProgressRow(operationProgressBar, operationProgressPercentText),
                    operationSecondaryProgressPanel
                }
            };
        }

        private static Grid CreateProgressRow(ProgressBar progressBar, TextBlock percentText)
        {
            var grid = new Grid
            {
                ColumnSpacing = 12
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(progressBar, 0);
            Grid.SetColumn(percentText, 1);
            grid.Children.Add(progressBar);
            grid.Children.Add(percentText);
            return grid;
        }

        private static TextBlock CreatePercentText(int? progress)
        {
            return new TextBlock
            {
                Text = FormatProgressPercent(progress),
                MinWidth = 36,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void HideOperationDialog()
        {
            if (operationDialog is null)
            {
                return;
            }

            operationStatusText = null;
            operationProgressBar = null;
            operationProgressPercentText = null;
            operationSecondaryProgressPanel = null;
            operationSecondaryStatusText = null;
            operationSecondaryProgressBar = null;
            operationSecondaryProgressPercentText = null;
            isClosingOperationDialog = true;
            operationDialog.Hide();
        }

        private void OnOperationDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            if (shellNavigationGuardService.State == ShellNavigationState.OperationRunning && !isClosingOperationDialog)
            {
                args.Cancel = true;
            }
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

        private void EnsureExternalDocumentationFooterItem()
        {
            NavigationViewItem? item = FindNavigationItem(NavView.FooterMenuItems, DocumentationNavigationTag);
            if (item is null)
            {
                item = new()
                {
                    Tag = DocumentationNavigationTag,
                    Icon = new FontIcon { Glyph = "\uE8A5" }
                };
                item.Tapped += DocumentationFooterItem_Tapped;
                item.KeyDown += DocumentationFooterItem_KeyDown;
                NavView.FooterMenuItems.Insert(0, item);
            }

            item.Content = localizationService.GetString("Nav_DocumentationKey.Title");
            ToolTipService.SetToolTip(item, localizationService.GetString("Nav_DocumentationKey.Description"));
            ApplyNavigationItemState(item, isFooter: true, shellNavigationGuardService.State);
        }

        private void EnsureExternalAboutFooterItem()
        {
            NavigationViewItem? item = FindNavigationItem(NavView.FooterMenuItems, AboutNavigationTag);
            if (item is null)
            {
                item = new()
                {
                    Tag = AboutNavigationTag,
                    Icon = new FontIcon { Glyph = "\uE946" }
                };
                item.Tapped += AboutFooterItem_Tapped;
                item.KeyDown += AboutFooterItem_KeyDown;
                NavView.FooterMenuItems.Insert(Math.Min(1, NavView.FooterMenuItems.Count), item);
            }

            item.Content = localizationService.GetString("Nav_AboutKey.Title");
            ToolTipService.SetToolTip(item, localizationService.GetString("Nav_AboutKey.Description"));
            ApplyNavigationItemState(item, isFooter: true, shellNavigationGuardService.State);
        }

        private async void DocumentationFooterItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            await OpenDocumentationAsync();
        }

        private async void DocumentationFooterItem_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is not (VirtualKey.Enter or VirtualKey.Space))
            {
                return;
            }

            e.Handled = true;
            await OpenDocumentationAsync();
        }

        private async void AboutFooterItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            await ShowAboutDialogAsync();
        }

        private async void AboutFooterItem_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key is not (VirtualKey.Enter or VirtualKey.Space))
            {
                return;
            }

            e.Handled = true;
            await ShowAboutDialogAsync();
        }

        private async Task OpenDocumentationAsync()
        {
            if (shellNavigationGuardService.State == ShellNavigationState.OperationRunning)
            {
                return;
            }

            try
            {
                await externalProcessLauncher.OpenUriAsync(new Uri(FoundryApplicationInfo.DocumentationUrl));
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to open documentation URL.");
                await ShowDocumentationFallbackDialogAsync();
            }
        }

        private async Task ShowDocumentationFallbackDialogAsync()
        {
            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = localizationService.GetString("Documentation.ExternalLaunchFailed.Title"),
                PrimaryButtonText = localizationService.GetString("Common.Close"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = localizationService.GetString("Documentation.ExternalLaunchFailed.Message"),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new Microsoft.UI.Xaml.Controls.TextBox
                        {
                            Text = FoundryApplicationInfo.DocumentationUrl,
                            IsReadOnly = true
                        }
                    }
                }
            };

            await dialog.ShowAsync();
        }

        private async Task ShowAboutDialogAsync()
        {
            if (shellNavigationGuardService.State == ShellNavigationState.OperationRunning)
            {
                return;
            }

            AboutDialog dialog = new(App.GetService<AboutUsSettingViewModel>())
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
            };

            await dialog.ShowAsync();
        }

        private void ApplyOperationState(OperationProgressState state)
        {
            if (operationStatusText is not null)
            {
                operationStatusText.Text = GetOperationDialogStatusText();
            }

            if (operationProgressBar is not null)
            {
                operationProgressBar.Value = state.Progress;
            }

            if (operationProgressPercentText is not null)
            {
                operationProgressPercentText.Text = FormatProgressPercent(state.Progress);
            }

            if (operationSecondaryProgressPanel is not null)
            {
                operationSecondaryProgressPanel.Visibility = state.HasSecondaryProgress ? Visibility.Visible : Visibility.Collapsed;
            }

            if (operationSecondaryStatusText is not null)
            {
                operationSecondaryStatusText.Text = GetSecondaryOperationDialogStatusText();
            }

            if (operationSecondaryProgressBar is not null)
            {
                operationSecondaryProgressBar.IsIndeterminate = !state.SecondaryProgress.HasValue;
                operationSecondaryProgressBar.Value = state.SecondaryProgress ?? 0;
            }

            if (operationSecondaryProgressPercentText is not null)
            {
                operationSecondaryProgressPercentText.Text = FormatProgressPercent(state.SecondaryProgress);
            }
        }

        private string GetOperationDialogStatusText()
        {
            return !string.IsNullOrWhiteSpace(operationProgressService.State.Status)
                ? operationProgressService.State.Status
                : localizationService.GetString("Shell.OperationRunning");
        }

        private string GetSecondaryOperationDialogStatusText()
        {
            return operationProgressService.State.HasSecondaryProgress
                ? operationProgressService.State.SecondaryStatus
                : string.Empty;
        }

        private static string FormatProgressPercent(int? progress)
        {
            return progress.HasValue
                ? $"{Math.Clamp(progress.Value, 0, 100)}%"
                : string.Empty;
        }

        private void SynchronizeSelectedNavigationItem(Type? pageType)
        {
            if (pageType is null)
            {
                return;
            }

            if (IsSettingsPageType(pageType))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            NavigationViewItem? item = FindNavigationItem(NavView.MenuItems, pageType.FullName)
                ?? FindNavigationItem(NavView.FooterMenuItems, pageType.FullName);

            if (item is not null && !ReferenceEquals(NavView.SelectedItem, item))
            {
                NavView.SelectedItem = item;
            }
        }

        private static bool IsSettingsPageType(Type pageType)
        {
            return pageType == typeof(SettingsPage)
                || pageType == typeof(GeneralSettingPage)
                || pageType == typeof(ThemeSettingPage)
                || pageType == typeof(AppUpdateSettingPage);
        }

        private static NavigationViewItem? FindNavigationItem(IList<object> items, string? uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
            {
                return null;
            }

            foreach (object item in items)
            {
                if (item is not NavigationViewItem navigationItem)
                {
                    continue;
                }

                if (string.Equals(navigationItem.Tag as string, uniqueId, StringComparison.Ordinal))
                {
                    return navigationItem;
                }

                NavigationViewItem? child = FindNavigationItem(navigationItem.MenuItems, uniqueId);
                if (child is not null)
                {
                    return child;
                }
            }

            return null;
        }
    }

}
