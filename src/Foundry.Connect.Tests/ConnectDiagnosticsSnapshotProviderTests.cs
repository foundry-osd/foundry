// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Services.Diagnostics;

namespace Foundry.Connect.Tests;

public sealed class ConnectDiagnosticsSnapshotProviderTests
{
    [Fact]
    public async Task CaptureAsync_ReportsRuntimeAndNetworkStateWithoutSecrets()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(
                    hasInternetAccess: true,
                    connectedWifiSsid: "Foundry")));
        await context.ViewModel.InitializeAsync();
        var provider = new ConnectDiagnosticsSnapshotProvider(context.ViewModel);

        var snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);
        string rendered = string.Join(Environment.NewLine, snapshot.AdapterSummaries);

        Assert.Equal(FoundryConnectApplicationInfo.Version, snapshot.ApplicationVersion);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.RuntimeIdentifier));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ProcessArchitecture));
        Assert.Contains("Ethernet", rendered);
        Assert.DoesNotContain("passphrase", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certificate", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", rendered, StringComparison.OrdinalIgnoreCase);
        context.ViewModel.Dispose();
    }
}
