// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Localization;

namespace Foundry.Services.Configuration;

public sealed class NetworkCacheDialogService(IApplicationLocalizationService localization) : INetworkCacheDialogService
{
    public async Task ShowAccountAsync(NetworkCacheAccountDialogRequest request, Func<NetworkCacheAccountDialogInput, string?> trySave)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(trySave);

        var dialog = new NetworkCacheAccountDialog(localization, request, trySave)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task ShowShareAsync(NetworkCacheShareDialogRequest request, Func<NetworkCacheShareDialogInput, string?> trySave)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(trySave);

        var dialog = new NetworkCacheShareDialog(localization, request, trySave)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task ShowScopeAsync(NetworkCacheScopeDialogRequest request, Func<NetworkCacheScopeDialogInput, string?> trySave)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(trySave);

        var dialog = new NetworkCacheScopeDialog(localization, request, trySave)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
