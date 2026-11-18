// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Connect;
using Foundry.Deploy;
using Foundry.WinPE.SystemPreparation;

namespace Foundry.WinPE.Ui;

/// <summary>Runs <see cref="ConnectPhase"/> in the host's WPF application.</summary>
internal sealed class WpfConnectPhaseRunner : IConnectPhaseRunner
{
    /// <inheritdoc />
    public async Task<ConnectPhaseOutcome> RunAsync(Action onConfigurationLoaded, Action onFirstWindowRendered,
        CancellationToken cancellationToken)
    {
        ConnectPhaseResult result = await ConnectPhase.RunAsync(new ConnectPhaseOptions
        {
            OnConfigurationLoaded = onConfigurationLoaded,
            OnFirstWindowRendered = onFirstWindowRendered
        }, cancellationToken);
        return new ConnectPhaseOutcome(result.ExitCode, result.DeploymentKey, result.ThemeMode, result.CultureName,
            result.MainWindow is null ? null : new HandOverWindow(result.MainWindow));
    }

    private sealed class HandOverWindow(Window window) : IHandOverWindow
    {
        public void Close() => window.Close();
    }
}

/// <summary>
/// Runs <see cref="DeployPhase"/> in the host's WPF application, after <paramref name="beforeDeploy"/> switched the host's log
/// file and remote diagnostics application to Deploy.
/// </summary>
internal sealed class WpfDeployPhaseRunner(Action beforeDeploy) : IDeployPhaseRunner
{
    /// <inheritdoc />
    public Task<int> RunAsync(DeployPhaseOptions options, CancellationToken cancellationToken)
    {
        beforeDeploy();
        return DeployPhase.RunAsync(options, cancellationToken);
    }
}

/// <summary>Runs the full clock synchronization, then the time zone selection, off the UI thread.</summary>
internal sealed class WinPeSystemPreparationStep(ISystemPreparation preparation) : ISystemPreparationStep
{
    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken) =>
        Task.Run(() => preparation.PrepareSystemAsync(cancellationToken), cancellationToken);
}
