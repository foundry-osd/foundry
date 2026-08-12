// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;

namespace Foundry.Services.Shell;

public interface IAppNavigationService
{
    ObservableCollection<BreadcrumbEntry> Breadcrumbs { get; }

    bool CanGoBack { get; }

    bool IsBreadcrumbVisible { get; }

    event EventHandler? StateChanged;

    void Initialize(NavigationView navigationView, Frame frame);

    bool NavigateTo(Type pageType, object? parameter = null);

    bool GoBack();

    bool NavigateToBreadcrumb(BreadcrumbEntry entry);

    bool RefreshCurrentPage();

    void RefreshLocalizedState();
}
