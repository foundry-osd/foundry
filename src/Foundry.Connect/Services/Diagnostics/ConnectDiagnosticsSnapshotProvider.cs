// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Connect.Models.Diagnostics;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Services.Diagnostics;

public sealed class ConnectDiagnosticsSnapshotProvider(MainWindowViewModel viewModel)
    : IConnectDiagnosticsSnapshotProvider
{
    public Task<ConnectDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string[] adapterSummaries =
        [
            $"{viewModel.EthernetAdapterName}: {viewModel.EthernetStatusText}; {viewModel.Strings["Ethernet.Ipv4Label"]}: {viewModel.EthernetIpAddress}; {viewModel.Strings["Ethernet.GatewayLabel"]}: {viewModel.EthernetGateway}",
            $"{viewModel.PrimaryStatusTitle}: {viewModel.PrimaryStatusDescription}",
            $"{viewModel.Strings["Wifi.Title"]}: {(viewModel.ShowProvisionedWifiContent ? viewModel.ProvisionedWifiStatusText : viewModel.WifiDiscoveryEmptyStateText)}"
        ];

        var snapshot = new ConnectDiagnosticsSnapshot(
            FoundryConnectApplicationInfo.Version,
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            viewModel.ConfigurationSourceText,
            TimeSpan.FromSeconds(FoundryConnectApplicationInfo.DefaultRefreshIntervalSeconds),
            viewModel.LastUpdatedAt,
            viewModel.PrimaryStatusTitle,
            string.IsNullOrWhiteSpace(viewModel.CurrentConnectionChipText) ? null : viewModel.CurrentConnectionChipText,
            adapterSummaries,
            viewModel.LastActionableError,
            DateTimeOffset.UtcNow);

        return Task.FromResult(snapshot);
    }
}
