// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Application;
using Foundry.Services.Operations;
using Foundry.Services.Shell;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Serilog;

namespace Foundry.Views
{
    /// <summary>
    /// Hosts the main WinUI shell, navigation view, global operation dialog, and update footer item.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly IApplicationLocalizationService localizationService;
        private readonly IOperationProgressService operationProgressService;
        private readonly IShellNavigationGuardService shellNavigationGuardService;
        private readonly IExternalProcessLauncher externalProcessLauncher;
        private readonly ILogger logger = Log.ForContext<MainWindow>();
        private const string DocumentationNavigationTag = "Foundry.External.Documentation";
        private const string AboutNavigationTag = "Foundry.External.About";
        private const string UpdateNavigationTag = "Foundry.Navigation.UpdateAvailable";
        private const string UpdateNavigationGlyph = "\uEBD3";
        private const string StringInfoBadgeStyleKey = "StringInfoBadgeStyle";
        private ContentDialog? operationDialog;
        private TextBlock? operationStatusText;
        private ProgressBar? operationProgressBar;
        private TextBlock? operationProgressPercentText;
        private Microsoft.UI.Xaml.Controls.ProgressRing? operationProgressRing;
        private StackPanel? operationSecondaryProgressPanel;
        private TextBlock? operationSecondaryStatusText;
        private ProgressBar? operationSecondaryProgressBar;
        private TextBlock? operationSecondaryProgressPercentText;
        private bool operationDialogCanClose;

        /// <summary>
        /// Gets the shell view model.
        /// </summary>
        public MainViewModel ViewModel { get; }

        public IAppNavigationService NavigationService { get; }

        internal FrameworkElement RootElement => RootGrid;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            localizationService = App.GetService<IApplicationLocalizationService>();
            operationProgressService = App.GetService<IOperationProgressService>();
            shellNavigationGuardService = App.GetService<IShellNavigationGuardService>();
            externalProcessLauncher = App.GetService<IExternalProcessLauncher>();
            NavigationService = App.GetService<IAppNavigationService>();
            ViewModel = App.GetService<MainViewModel>();
            this.InitializeComponent();
            AppTitleBar.Title = ApplicationInfo.ProductName;
            AppTitleBar.Subtitle = ApplicationInfo.VersionWithPrefix;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            NavigationService.Initialize(NavView, NavFrame);
            NavigationService.StateChanged += OnNavigationStateChanged;
            ApplyNavigationState();
            ApplyLocalizedShellText();
            ApplyShellNavigationState();

            localizationService.LanguageChanged += OnLanguageChanged;
            operationProgressService.StateChanged += OnOperationProgressChanged;
            shellNavigationGuardService.StateChanged += OnShellNavigationStateChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            AppTitleBar.PaneToggleRequested += OnTitleBarPaneToggleRequested;
            Closed += OnClosed;
        }

        private void ApplyLocalizedShellText()
        {
            EnsureExternalDocumentationFooterItem();
            EnsureExternalAboutFooterItem();
            RefreshUpdateFooterItem();
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
            NavigationService.RefreshLocalizedState();
            ApplyLocalizedShellText();
            ApplyShellNavigationState();
            NavigationService.RefreshCurrentPage();
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            localizationService.LanguageChanged -= OnLanguageChanged;
            operationProgressService.StateChanged -= OnOperationProgressChanged;
            shellNavigationGuardService.StateChanged -= OnShellNavigationStateChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            NavigationService.StateChanged -= OnNavigationStateChanged;
            AppTitleBar.PaneToggleRequested -= OnTitleBarPaneToggleRequested;
            Closed -= OnClosed;
            ViewModel.Dispose();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.IsUpdateFooterItemVisible)
                or nameof(MainViewModel.UpdateFooterTitle)
                or nameof(MainViewModel.UpdateFooterToolTip)
                or nameof(MainViewModel.UpdateFooterBadgeValue))
            {
                RefreshUpdateFooterItem();
            }
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

        private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            switch (args.InvokedItemContainer?.Tag as string)
            {
                case UpdateNavigationTag:
                    NavigateToUpdateSettingsPage();
                    return;
                case DocumentationNavigationTag:
                    await OpenDocumentationAsync();
                    return;
                case AboutNavigationTag:
                    await ShowAboutDialogAsync();
                    return;
            }

            if (args.IsSettingsInvoked)
            {
                NavigationService.NavigateTo(typeof(SettingsPage));
                return;
            }

            if (args.InvokedItemContainer?.Tag is string routeId &&
                NavigationRouteCatalog.FindById(routeId) is { } route)
            {
                NavigationService.NavigateTo(route.PageType);
            }
        }

        private void Breadcrumbs_ItemClicked(
            Microsoft.UI.Xaml.Controls.BreadcrumbBar sender,
            Microsoft.UI.Xaml.Controls.BreadcrumbBarItemClickedEventArgs args)
        {
            if (args.Item is BreadcrumbEntry entry)
            {
                NavigationService.NavigateToBreadcrumb(entry);
            }
        }

        private void ApplyShellNavigationState()
        {
            ShellNavigationState state = shellNavigationGuardService.State;
            bool isOperationRunning = state == ShellNavigationState.OperationRunning;
            UpdateOperationDialog(isOperationRunning);

            // The navigation guard owns route availability so individual pages do not duplicate ADK or operation checks.
            ApplyFooterItemsState(state);

            ApplyNavigationState();
            RefreshUpdateFooterItem();
        }

        private void OnNavigationStateChanged(object? sender, EventArgs e)
        {
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(ApplyNavigationState);
                return;
            }

            ApplyNavigationState();
        }

        private void ApplyNavigationState()
        {
            bool showBreadcrumbs = NavigationService.IsBreadcrumbVisible;
            NavView.AlwaysShowHeader = showBreadcrumbs;
            Breadcrumbs.Visibility = showBreadcrumbs
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateOperationDialog(bool isOperationRunning)
        {
            if (isOperationRunning)
            {
                ShowOperationDialog();
                return;
            }

            CompleteOperationDialog();
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

            operationDialogCanClose = false;
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

                ClearOperationDialogReferences();
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

            operationProgressRing = new Microsoft.UI.Xaml.Controls.ProgressRing
            {
                Width = 56,
                Height = 56,
                IsActive = true,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            return new StackPanel
            {
                MinWidth = 420,
                MaxWidth = 520,
                Padding = new Thickness(0, 8, 0, 0),
                Spacing = 16,
                Children =
                {
                    operationProgressRing,
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

        private void CompleteOperationDialog()
        {
            if (operationDialog is null)
            {
                return;
            }

            operationDialogCanClose = true;
            operationDialog.Title = localizationService.GetString("Shell.OperationCompleted");
            operationDialog.CloseButtonText = localizationService.GetString("Common.Close");
            operationDialog.DefaultButton = ContentDialogButton.Close;
            ApplyOperationState(operationProgressService.State);
        }

        private void ClearOperationDialogReferences()
        {
            operationStatusText = null;
            operationProgressBar = null;
            operationProgressPercentText = null;
            operationProgressRing = null;
            operationSecondaryProgressPanel = null;
            operationSecondaryStatusText = null;
            operationSecondaryProgressBar = null;
            operationSecondaryProgressPercentText = null;
        }

        private void OnOperationDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            if (!operationDialogCanClose)
            {
                args.Cancel = true;
            }
        }

        private void ApplyFooterItemsState(ShellNavigationState state)
        {
            foreach (NavigationViewItem item in NavView.FooterMenuItems.OfType<NavigationViewItem>())
            {
                item.IsEnabled = state != ShellNavigationState.OperationRunning;
            }
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
                NavView.FooterMenuItems.Insert(0, item);
            }

            item.Content = localizationService.GetString("Nav_DocumentationKey.Title");
            string description = localizationService.GetString("Nav_DocumentationKey.Description");
            ToolTipService.SetToolTip(item, description);
            AutomationProperties.SetName(item, item.Content?.ToString() ?? string.Empty);
            AutomationProperties.SetHelpText(item, description);
            item.IsEnabled = shellNavigationGuardService.State != ShellNavigationState.OperationRunning;
        }

        private void RefreshUpdateFooterItem()
        {
            if (!ViewModel.IsUpdateFooterItemVisible)
            {
                RemoveUpdateFooterItem();
                return;
            }

            EnsureUpdateFooterItem();
        }

        private void EnsureUpdateFooterItem()
        {
            NavigationViewItem? item = FindNavigationItem(NavView.FooterMenuItems, UpdateNavigationTag);
            if (item is null)
            {
                item = new()
                {
                    Tag = UpdateNavigationTag,
                    Icon = new FontIcon { Glyph = UpdateNavigationGlyph }
                };
                NavView.FooterMenuItems.Insert(0, item);
            }
            else
            {
                int currentIndex = NavView.FooterMenuItems.IndexOf(item);
                if (currentIndex > 0)
                {
                    NavView.FooterMenuItems.RemoveAt(currentIndex);
                    NavView.FooterMenuItems.Insert(0, item);
                }
            }

            item.Content = ViewModel.UpdateFooterTitle;
            item.InfoBadge = CreateUpdateInfoBadge();
            ToolTipService.SetToolTip(item, ViewModel.UpdateFooterToolTip);
            AutomationProperties.SetName(item, ViewModel.UpdateFooterTitle);
            AutomationProperties.SetHelpText(item, ViewModel.UpdateFooterToolTip);
            item.IsEnabled = shellNavigationGuardService.State != ShellNavigationState.OperationRunning;
        }

        private void RemoveUpdateFooterItem()
        {
            NavigationViewItem? item = FindNavigationItem(NavView.FooterMenuItems, UpdateNavigationTag);
            if (item is null)
            {
                return;
            }

            NavView.FooterMenuItems.Remove(item);
        }

        private InfoBadge CreateUpdateInfoBadge()
        {
            InfoBadge badge = new()
            {
                Tag = ViewModel.UpdateFooterBadgeValue
            };

            if (App.Current.Resources.TryGetValue(StringInfoBadgeStyleKey, out object style)
                && style is Style infoBadgeStyle)
            {
                badge.Style = infoBadgeStyle;
            }

            return badge;
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
                NavView.FooterMenuItems.Insert(Math.Min(1, NavView.FooterMenuItems.Count), item);
            }

            item.Content = localizationService.GetString("Nav_AboutKey.Title");
            string description = localizationService.GetString("Nav_AboutKey.Description");
            ToolTipService.SetToolTip(item, description);
            AutomationProperties.SetName(item, item.Content?.ToString() ?? string.Empty);
            AutomationProperties.SetHelpText(item, description);
            item.IsEnabled = shellNavigationGuardService.State != ShellNavigationState.OperationRunning;
        }

        private void NavigateToUpdateSettingsPage()
        {
            if (shellNavigationGuardService.State == ShellNavigationState.OperationRunning)
            {
                return;
            }

            if (NavFrame.CurrentSourcePageType == typeof(AppUpdateSettingPage))
            {
                return;
            }

            NavigationService.NavigateTo(typeof(SettingsPage));
            NavigationService.NavigateTo(typeof(AppUpdateSettingPage));
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
            ApplyNavigationState();
        }

        private void ApplyOperationState(OperationProgressState state)
        {
            if (operationStatusText is not null)
            {
                operationStatusText.Text = GetOperationDialogStatusText();
            }

            if (operationProgressBar is not null)
            {
                operationProgressBar.Value = operationDialogCanClose ? 100 : state.Progress;
            }

            if (operationProgressPercentText is not null)
            {
                operationProgressPercentText.Text = FormatProgressPercent(operationDialogCanClose ? 100 : state.Progress);
            }

            if (operationProgressRing is not null)
            {
                operationProgressRing.IsActive = !operationDialogCanClose;
            }

            if (operationSecondaryProgressPanel is not null)
            {
                operationSecondaryProgressPanel.Visibility = !operationDialogCanClose && state.HasSecondaryProgress
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
