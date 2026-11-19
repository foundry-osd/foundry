// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Services.Network;

/// <summary>
/// Runs again what may have failed for lack of network once the Foundry Connect page returned to Deploy; the rules live in
/// <see cref="MainWindowViewModel.RefreshAfterNetworkReturnAsync"/>, which owns that state.
/// </summary>
/// <param name="viewModel">Resolves the view model on the first return: the view model depends on the navigator that runs this refresh.</param>
internal sealed class NetworkReturnRefresh(Func<MainWindowViewModel> viewModel) : INetworkReturnRefresh
{
    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => viewModel().RefreshAfterNetworkReturnAsync(cancellationToken);
}
