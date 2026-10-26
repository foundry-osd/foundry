// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

/// <summary>Authors the SMB shares Foundry Deploy uses as a shared cache, their accounts and the IP scopes that choose them.</summary>
public sealed partial class NetworkCachePage : Page
{
    public NetworkCacheConfigurationViewModel ViewModel { get; }

    /// <summary>Gets the read-only state of a managed configuration.</summary>
    public ManagedConfigurationViewModel Managed { get; } = App.GetService<ManagedConfigurationViewModel>();

    public NetworkCachePage()
    {
        ViewModel = App.GetService<NetworkCacheConfigurationViewModel>();
        InitializeComponent();
        TableViewDefaultSort.Attach(AccountsTable, nameof(NetworkCacheAccountEntryViewModel.UserName));
        TableViewFullWidth.Attach(AccountsTable);
        TableViewDefaultSort.Attach(SharesTable, nameof(NetworkCacheShareEntryViewModel.Path));
        TableViewFullWidth.Attach(SharesTable);
        TableViewDefaultSort.Attach(ScopesTable, nameof(NetworkCacheScopeEntryViewModel.Name));
        TableViewFullWidth.Attach(ScopesTable);
        ViewModel.AccountRowsRemoving += OnAccountRowsRemoving;
        ViewModel.ShareRowsRemoving += OnShareRowsRemoving;
        ViewModel.ScopeRowsRemoving += OnScopeRowsRemoving;
        Unloaded += OnUnloaded;
    }

    private void OnAccountRowsRemoving(object? sender, EventArgs e) => AccountsTable.DeselectAll();

    private void OnShareRowsRemoving(object? sender, EventArgs e) => SharesTable.DeselectAll();

    private void OnScopeRowsRemoving(object? sender, EventArgs e) => ScopesTable.DeselectAll();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.AccountRowsRemoving -= OnAccountRowsRemoving;
        ViewModel.ShareRowsRemoving -= OnShareRowsRemoving;
        ViewModel.ScopeRowsRemoving -= OnScopeRowsRemoving;
        ViewModel.Dispose();
    }
}
