// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foundry.Connect.Models.Diagnostics;
using Foundry.Connect.Services.Diagnostics;
using Foundry.Connect.ViewModels;
using Foundry.Connect.Views;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class ConnectDialogTests
{
    [AvaloniaFact]
    public async Task AboutDialog_IsOwnedAndClosesOnEscapeWithOwnerFocusRestored()
    {
        var ownerButton = new Button { Content = "Owner" };
        var owner = new Window { Content = ownerButton };
        owner.Show();
        ownerButton.Focus();
        var dialog = new AboutDialog
        {
            DataContext = new AboutDialogViewModel("About", "Connect", "1.0", "One", "Two", "Footer")
        };

        Task lifetime = dialog.ShowDialog(owner);
        dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        await lifetime;

        Assert.False(dialog.IsVisible);
        Assert.True(ownerButton.IsFocused);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task DiagnosticsDialog_CapturesOnOpenRefreshesAndShowsProviderFailure()
    {
        var provider = new QueueDiagnosticsProvider(
            CreateSnapshot("initial"),
            CreateSnapshot("refreshed"),
            new InvalidOperationException("provider failed"));
        var viewModel = new ConnectDiagnosticsDialogViewModel(provider);
        var owner = new Window();
        owner.Show();
        var dialog = new ConnectDiagnosticsDialog(viewModel);

        Task lifetime = dialog.ShowDialog(owner);
        await viewModel.Initialization;
        Assert.Contains("initial", viewModel.DisplayText);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("refreshed", viewModel.DisplayText);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasError);
        Assert.DoesNotContain("provider failed", viewModel.ErrorText, StringComparison.OrdinalIgnoreCase);

        dialog.Close();
        await lifetime;
        owner.Close();
    }

    private static ConnectDiagnosticsSnapshot CreateSnapshot(string adapter) => new(
        "1.0",
        "win-x64",
        "x64",
        "defaults",
        TimeSpan.FromSeconds(5),
        null,
        "Ready",
        "Ethernet",
        [adapter],
        null,
        DateTimeOffset.UtcNow);

    private sealed class QueueDiagnosticsProvider(params object[] results) : IConnectDiagnosticsSnapshotProvider
    {
        private readonly Queue<object> _results = new(results);

        public Task<ConnectDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            object result = _results.Dequeue();
            return result is Exception exception
                ? Task.FromException<ConnectDiagnosticsSnapshot>(exception)
                : Task.FromResult((ConnectDiagnosticsSnapshot)result);
        }
    }
}
