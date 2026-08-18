// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Localization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;

namespace Foundry.Services.Shell;

internal sealed class AppNavigationService(
    IApplicationLocalizationService localizationService,
    IShellNavigationGuardService navigationGuard,
    INavigationStatusService navigationStatusService) : IAppNavigationService
{
    private readonly Dictionary<string, NavigationViewItem> routeItems = [];
    private NavigationView? navigationView;
    private Frame? frame;
    private DispatcherQueue? dispatcherQueue;
    private NavigationRoute? currentRoute;

    public ObservableCollection<BreadcrumbEntry> Breadcrumbs { get; } = [];

    private bool CanGoBack => frame?.CanGoBack == true && navigationGuard.State != ShellNavigationState.OperationRunning;

    public bool IsBreadcrumbVisible =>
        currentRoute?.PageType == typeof(Views.SettingsPage) || currentRoute?.ParentPageType is not null;

    public event EventHandler? StateChanged;

    public void Initialize(NavigationView navigationView, Frame frame)
    {
        if (this.navigationView is not null)
        {
            return;
        }

        this.navigationView = navigationView;
        this.frame = frame;
        dispatcherQueue = navigationView.DispatcherQueue;
        frame.Navigated += OnFrameNavigated;
        navigationGuard.StateChanged += OnNavigationGuardStateChanged;
        navigationStatusService.StatusChanged += OnNavigationStatusChanged;
        BuildMenuItems();
        NavigateTo(typeof(Views.HomeLandingPage));
    }

    public bool NavigateTo(Type pageType)
    {
        NavigationRoute? route = NavigationRouteCatalog.FindByPageType(pageType);
        if (route is null || !IsRouteEnabled(route, navigationGuard.State) || frame is null)
        {
            return false;
        }

        if (frame.CurrentSourcePageType == pageType)
        {
            return true;
        }

        return frame.Navigate(pageType);
    }

    private bool GoBack()
    {
        if (!CanGoBack || frame is null)
        {
            return false;
        }

        frame.GoBack();
        return true;
    }

    public bool NavigateToBreadcrumb(BreadcrumbEntry entry)
    {
        if (Breadcrumbs.Count > 1 && Breadcrumbs[0].PageType == entry.PageType)
        {
            return GoBack();
        }

        return currentRoute?.PageType == entry.PageType || NavigateTo(entry.PageType);
    }

    public bool RefreshCurrentPage()
    {
        if (frame is null || currentRoute is null)
        {
            return false;
        }

        int backStackCount = frame.BackStack.Count;
        bool navigated = frame.Navigate(
            currentRoute.PageType,
            null,
            new SuppressNavigationTransitionInfo());
        if (navigated && frame.BackStack.Count > backStackCount)
        {
            frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
            UpdateNavigationState();
        }

        return navigated;
    }

    public void RefreshLocalizedState()
    {
        BuildMenuItems(raiseStateChanged: false);
        UpdateNavigationState();
    }

    private void BuildMenuItems(bool raiseStateChanged = true)
    {
        if (navigationView is null)
        {
            return;
        }

        navigationView.MenuItems.Clear();
        routeItems.Clear();
        NavigationSection? currentSection = null;
        foreach (NavigationRoute route in NavigationRouteCatalog.PrimaryRoutes)
        {
            if (route.Section is { } section && section != currentSection)
            {
                currentSection = section;
                string headerKey = NavigationRouteCatalog.GetSectionTitleResourceKey(section);
                navigationView.MenuItems.Add(new NavigationViewItemHeader
                {
                    Content = localizationService.GetString(headerKey)
                });
            }

            NavigationViewItem item = new()
            {
                Content = localizationService.GetString(route.TitleResourceKey),
                Tag = route.Id,
                Icon = new FontIcon
                {
                    Glyph = char.ConvertFromUtf32(Convert.ToInt32(route.IconGlyph, 16))
                }
            };
            if (route.DescriptionResourceKey is not null)
            {
                string description = localizationService.GetString(route.DescriptionResourceKey);
                ToolTipService.SetToolTip(item, description);
                AutomationProperties.SetHelpText(item, description);
            }

            AutomationProperties.SetName(item, localizationService.GetString(route.TitleResourceKey));
            ApplyNavigationStatus(route, item);
            routeItems.Add(route.Id, item);
            navigationView.MenuItems.Add(item);
        }

        if (navigationView.SettingsItem is NavigationViewItem settingsItem)
        {
            string settingsTitle = localizationService.GetString("SettingsPage.PageTitle");
            settingsItem.Content = settingsTitle;
            AutomationProperties.SetName(settingsItem, settingsTitle);
        }

        ApplyGuardState(raiseStateChanged);
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        currentRoute = NavigationRouteCatalog.FindByPageType(e.SourcePageType);
        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        if (navigationView is null || currentRoute is null)
        {
            return;
        }

        if (currentRoute.PageType == typeof(Views.SettingsPage) || currentRoute.ParentPageType == typeof(Views.SettingsPage))
        {
            navigationView.SelectedItem = navigationView.SettingsItem;
        }
        else if (routeItems.TryGetValue(currentRoute.Id, out NavigationViewItem? selectedItem))
        {
            navigationView.SelectedItem = selectedItem;
        }

        Breadcrumbs.Clear();
        if (currentRoute.ParentPageType is not null &&
            NavigationRouteCatalog.FindByPageType(currentRoute.ParentPageType) is { } parentRoute)
        {
            Breadcrumbs.Add(new BreadcrumbEntry(
                localizationService.GetString(parentRoute.TitleResourceKey),
                parentRoute.PageType));
        }

        Breadcrumbs.Add(new BreadcrumbEntry(
            localizationService.GetString(currentRoute.TitleResourceKey),
            currentRoute.PageType));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNavigationGuardStateChanged(object? sender, EventArgs e)
    {
        if (dispatcherQueue?.HasThreadAccess == false)
        {
            dispatcherQueue.TryEnqueue(() => ApplyGuardState());
            return;
        }

        ApplyGuardState();
    }

    private void OnNavigationStatusChanged(object? sender, EventArgs e)
    {
        if (dispatcherQueue?.HasThreadAccess == false)
        {
            dispatcherQueue.TryEnqueue(ApplyNavigationStatuses);
            return;
        }

        ApplyNavigationStatuses();
    }

    private void ApplyNavigationStatuses()
    {
        foreach ((string routeId, NavigationViewItem item) in routeItems)
        {
            if (NavigationRouteCatalog.FindById(routeId) is { } route)
            {
                ApplyNavigationStatus(route, item);
            }
        }
    }

    private void ApplyNavigationStatus(NavigationRoute route, NavigationViewItem item)
    {
        NavigationStatus? status = navigationStatusService.GetStatus(route.PageType);
        if (status is null)
        {
            item.InfoBadge = null;
            AutomationProperties.SetItemStatus(item, string.Empty);
            return;
        }

        string statusText = localizationService.GetString(status.StatusResourceKey);
        AutomationProperties.SetItemStatus(item, statusText);
        item.InfoBadge = status.Severity is { } severity
            ? NavigationInfoBadgeFactory.Create(severity)
            : null;
        if (item.InfoBadge is not null)
        {
            ToolTipService.SetToolTip(item.InfoBadge, statusText);
            AutomationProperties.SetName(item.InfoBadge, statusText);
        }
    }

    private void ApplyGuardState(bool raiseStateChanged = true)
    {
        foreach ((string routeId, NavigationViewItem item) in routeItems)
        {
            NavigationRoute? route = NavigationRouteCatalog.FindById(routeId);
            item.IsEnabled = route is not null && IsRouteEnabled(route, navigationGuard.State);
        }

        if (navigationView?.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.IsEnabled = navigationGuard.State != ShellNavigationState.OperationRunning;
        }

        if (raiseStateChanged)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsRouteEnabled(NavigationRoute route, ShellNavigationState state) => state switch
    {
        ShellNavigationState.Ready => true,
        ShellNavigationState.AdkBlocked => route.IsAvailableWhenAdkBlocked,
        ShellNavigationState.OperationRunning => false,
        _ => false
    };
}
