// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Shell;
using Foundry.Services.Updates;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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
        private const string BugReportNavigationTag = "Foundry.External.BugReport";
        private const string AboutNavigationTag = "Foundry.External.About";
        private const string UpdateNavigationTag = "Foundry.Navigation.UpdateAvailable";
        private const string BugReportNavigationGlyph = "\uEBE8";
        private Grid? updateFooterContentRoot;
        private TextBlock? updateFooterLabel;
        private long paneOpenCallbackToken;
        private long paneDisplayModeCallbackToken;
        private string? pendingUpdateAnnouncement;
        private bool hasAnnouncedUpdateStatus;
        private bool isClosed;
        private OperationProgressDialog? operationDialog;

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
            AppTitleBar.Title = FoundryApplicationInfo.AppName;
            AppTitleBar.Subtitle = FoundryApplicationInfo.VersionWithPrefix;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            NavigationService.Initialize(NavView, NavFrame);
            NavigationService.StateChanged += OnNavigationStateChanged;
            ApplyLocalizedShellText();
            ApplyShellNavigationState();

            localizationService.LanguageChanged += OnLanguageChanged;
            shellNavigationGuardService.StateChanged += OnShellNavigationStateChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.UpdateFooterStatusChanged += OnUpdateFooterStatusChanged;
            NavView.DisplayModeChanged += OnNavigationDisplayModeChanged;
            paneOpenCallbackToken = NavView.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, OnNavigationLayoutChanged);
            paneDisplayModeCallbackToken = NavView.RegisterPropertyChangedCallback(NavigationView.PaneDisplayModeProperty, OnNavigationLayoutChanged);
            AppTitleBar.PaneToggleRequested += OnTitleBarPaneToggleRequested;
            Closed += OnClosed;
        }

        private void ApplyLocalizedShellText()
        {
            EnsureExternalDocumentationFooterItem();
            EnsureExternalBugReportFooterItem();
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
            isClosed = true;
            pendingUpdateAnnouncement = null;
            localizationService.LanguageChanged -= OnLanguageChanged;
            shellNavigationGuardService.StateChanged -= OnShellNavigationStateChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.UpdateFooterStatusChanged -= OnUpdateFooterStatusChanged;
            NavView.DisplayModeChanged -= OnNavigationDisplayModeChanged;
            NavView.UnregisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, paneOpenCallbackToken);
            NavView.UnregisterPropertyChangedCallback(NavigationView.PaneDisplayModeProperty, paneDisplayModeCallbackToken);
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
                or nameof(MainViewModel.UpdateFooterAutomationName)
                or nameof(MainViewModel.UpdateFooterItemStatus)
                or nameof(MainViewModel.UpdateFooterHelpText)
                or nameof(MainViewModel.IsUpdateDownloading)
                or nameof(MainViewModel.IsUpdateReadyToApply))
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

        private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            switch (args.InvokedItemContainer?.Tag as string)
            {
                case UpdateNavigationTag:
                    if (ViewModel.IsUpdateReadyToApply)
                    {
                        await App.GetService<IApplicationUpdateRestartService>().ApplyUpdateAsync();
                    }
                    else
                    {
                        NavigationService.NavigateToUpdateSettings();
                    }
                    return;
                case DocumentationNavigationTag:
                    await OpenDocumentationAsync();
                    return;
                case BugReportNavigationTag:
                    await OpenBugReportAsync();
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
            if (state == ShellNavigationState.OperationRunning)
            {
                ShowOperationDialog();
            }

            // The navigation guard owns route availability so individual pages do not duplicate ADK or operation checks.
            ApplyFooterItemsState(state);

            ApplyNavigationState();
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

        private async void ShowOperationDialog()
        {
            if (operationDialog is not null)
            {
                return;
            }

            // The dialog follows the operation progress service until the user closes it after the outcome.
            OperationProgressDialog dialog = new(operationProgressService, localizationService)
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme
            };
            operationDialog = dialog;

            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                dialog.Detach();
                if (ReferenceEquals(operationDialog, dialog))
                {
                    operationDialog = null;
                }
            }
        }

        private void ApplyFooterItemsState(ShellNavigationState state)
        {
            foreach (NavigationViewItem item in NavView.FooterMenuItems.OfType<NavigationViewItem>())
            {
                item.IsEnabled = state is not (ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending);
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
            item.IsEnabled = shellNavigationGuardService.State is not (ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending);
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
                    ContentTemplate = (DataTemplate)RootGrid.Resources["UpdateFooterContentTemplate"],
                    SelectsOnInvoked = false
                };
                NavView.FooterMenuItems.Insert(0, item);
            }
            if (!Equals(item.Content, ViewModel.UpdateFooterAutomationName))
            {
                item.Content = ViewModel.UpdateFooterAutomationName;
            }

            bool showAttentionBadge = !ViewModel.IsUpdateDownloading && !ViewModel.IsUpdateReadyToApply;
            if (showAttentionBadge && item.InfoBadge is null)
            {
                item.InfoBadge = NavigationInfoBadgeFactory.Create(NavigationInfoBadgeSeverity.Attention);
            }
            else if (!showAttentionBadge)
            {
                item.InfoBadge = null;
            }

            ToolTipService.SetToolTip(item, ViewModel.UpdateFooterToolTip);
            AutomationProperties.SetName(item, ViewModel.UpdateFooterAutomationName);
            AutomationProperties.SetHelpText(item, ViewModel.UpdateFooterHelpText);
            AutomationProperties.SetItemStatus(item, ViewModel.UpdateFooterItemStatus);
            item.IsEnabled = shellNavigationGuardService.State is not (ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending);
            UpdateFooterLayout();
        }

        private void OnUpdateFooterContentLoaded(object sender, RoutedEventArgs e)
        {
            if (isClosed || sender is not Grid root)
            {
                return;
            }

            root.DataContext = ViewModel;
            updateFooterContentRoot = root;
            updateFooterLabel = root.FindName("UpdateFooterLabel") as TextBlock;
            UpdateFooterLayout();
            if (!hasAnnouncedUpdateStatus && pendingUpdateAnnouncement is null)
            {
                pendingUpdateAnnouncement = ViewModel.UpdateFooterItemStatus;
            }

            if (pendingUpdateAnnouncement is not null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (pendingUpdateAnnouncement is { } message)
                    {
                        AnnounceUpdateFooterStatus(message);
                    }
                });
            }
        }

        private void OnUpdateFooterContentUnloaded(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, updateFooterContentRoot))
            {
                return;
            }

            updateFooterContentRoot.DataContext = null;
            updateFooterContentRoot = null;
            updateFooterLabel = null;
        }

        private void OnNavigationLayoutChanged(DependencyObject sender, DependencyProperty property) => UpdateFooterLayout();

        private void OnNavigationDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) => UpdateFooterLayout();

        private void UpdateFooterLayout()
        {
            if (updateFooterLabel is not null)
            {
                updateFooterLabel.Visibility = NavView.IsPaneOpen || NavView.PaneDisplayMode == NavigationViewPaneDisplayMode.Top
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void OnUpdateFooterStatusChanged(object? sender, string message) => AnnounceUpdateFooterStatus(message);

        private void AnnounceUpdateFooterStatus(string message)
        {
            if (isClosed)
            {
                return;
            }

            NavigationViewItem? item = FindNavigationItem(NavView.FooterMenuItems, UpdateNavigationTag);
            if (item is null || !item.IsLoaded)
            {
                pendingUpdateAnnouncement = message;
                return;
            }

            AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(item)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(item);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.Other,
                AutomationNotificationProcessing.MostRecent,
                message,
                "Foundry.ApplicationUpdate");
            pendingUpdateAnnouncement = null;
            hasAnnouncedUpdateStatus = true;
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
                NavView.FooterMenuItems.Insert(Math.Min(2, NavView.FooterMenuItems.Count), item);
            }

            item.Content = localizationService.GetString("Nav_AboutKey.Title");
            string description = localizationService.GetString("Nav_AboutKey.Description");
            ToolTipService.SetToolTip(item, description);
            AutomationProperties.SetName(item, item.Content?.ToString() ?? string.Empty);
            AutomationProperties.SetHelpText(item, description);
            item.IsEnabled = shellNavigationGuardService.State is not (ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending);
        }

        private void EnsureExternalBugReportFooterItem()
        {
            NavigationViewItem? item = FindNavigationItem(NavView.FooterMenuItems, BugReportNavigationTag);
            if (item is null)
            {
                item = new()
                {
                    Tag = BugReportNavigationTag,
                    Icon = new FontIcon { Glyph = BugReportNavigationGlyph }
                };
                NavView.FooterMenuItems.Insert(Math.Min(1, NavView.FooterMenuItems.Count), item);
            }

            item.Content = localizationService.GetString("Nav_ReportBugKey.Title");
            string description = localizationService.GetString("Nav_ReportBugKey.Description");
            ToolTipService.SetToolTip(item, description);
            AutomationProperties.SetName(item, item.Content?.ToString() ?? string.Empty);
            AutomationProperties.SetHelpText(item, description);
            item.IsEnabled = shellNavigationGuardService.State is not (ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending);
        }

        internal async Task OpenDocumentationAsync()
        {
            await OpenExternalUriAsync(
                new Uri(FoundryApplicationInfo.DocumentationUrl),
                "Documentation.ExternalLaunchFailed.Title",
                "Documentation.ExternalLaunchFailed.Message",
                "Documentation");
        }

        private async Task OpenBugReportAsync()
        {
            await OpenExternalUriAsync(
                new Uri(FoundryApplicationInfo.BugReportUrl),
                "BugReport.ExternalLaunchFailed.Title",
                "BugReport.ExternalLaunchFailed.Message",
                "BugReport");
        }

        private async Task OpenExternalUriAsync(
            Uri uri,
            string failureTitleResourceKey,
            string failureMessageResourceKey,
            string target)
        {
            if (shellNavigationGuardService.State is ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending)
            {
                return;
            }

            try
            {
                await externalProcessLauncher.OpenUriAsync(uri);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to open external URI. Target={Target}", target);
                await ShowExternalUriFallbackDialogAsync(uri, failureTitleResourceKey, failureMessageResourceKey);
            }
        }

        private async Task ShowExternalUriFallbackDialogAsync(
            Uri uri,
            string failureTitleResourceKey,
            string failureMessageResourceKey)
        {
            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                Style = ContentDialogStyleProvider.DefaultStyle,
                Title = localizationService.GetString(failureTitleResourceKey),
                CloseButtonText = localizationService.GetString("Common.Close"),
                DefaultButton = ContentDialogButton.Close,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = localizationService.GetString(failureMessageResourceKey),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new Microsoft.UI.Xaml.Controls.TextBox
                        {
                            Text = uri.AbsoluteUri,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };

            await dialog.ShowAsync();
        }

        private async Task ShowAboutDialogAsync()
        {
            if (shellNavigationGuardService.State is ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending)
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

            }

            return null;
        }
    }

}
