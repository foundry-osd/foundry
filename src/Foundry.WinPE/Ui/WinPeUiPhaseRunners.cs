// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Connect;
using Foundry.Core.Models.Runtime;
using Foundry.Deploy;
using Foundry.WinPE.Shell;
using Foundry.WinPE.SystemPreparation;
using Foundry.Wpf.Shell;

namespace Foundry.WinPE.Ui;

/// <summary>
/// Runs <see cref="ConnectPhase"/> in the host's WPF application, in a shell window it creates and shows; the window is handed
/// over on success and closed otherwise. It is a stepping stone toward one window shared by both phases.
/// </summary>
internal sealed class WpfConnectPhaseRunner : IConnectPhaseRunner
{
    /// <inheritdoc />
    public async Task<ConnectPhaseOutcome> RunAsync(Action onConfigurationLoaded, Action onFirstContentRendered,
        CancellationToken cancellationToken)
    {
        var shell = new WinPeShellWindow();
        shell.Show();
        ConnectPhaseResult result;
        try
        {
            result = await ConnectPhase.RunAsync(new ConnectPhaseOptions
            {
                Shell = shell,
                OnConfigurationLoaded = onConfigurationLoaded,
                OnFirstContentRendered = onFirstContentRendered
            }, cancellationToken);
        }
        catch
        {
            Close(shell);
            throw;
        }

        if (result.ExitCode != (int)ChildRuntimeExitCode.Success)
        {
            Close(shell);
            return new ConnectPhaseOutcome(result.ExitCode, result.DeploymentKey, result.ThemeMode, result.CultureName, Window: null);
        }

        return new ConnectPhaseOutcome(result.ExitCode, result.DeploymentKey, result.ThemeMode, result.CultureName,
            new HandOverWindow(shell));
    }

    internal static void Close(WinPeShellWindow shell)
    {
        shell.AllowClose = true;
        try
        {
            shell.Close();
        }
        catch (InvalidOperationException)
        {
            // Already closing: the host's shutdown closes it.
        }
    }

    private sealed class HandOverWindow(WinPeShellWindow shell) : IHandOverWindow
    {
        public void Close() => WpfConnectPhaseRunner.Close(shell);
    }
}

/// <summary>
/// Runs <see cref="DeployPhase"/> in the host's WPF application, in a shell window it creates, shows and closes, after <paramref name="beforeDeploy"/> switched the host's log
/// file and remote diagnostics application to Deploy.
/// </summary>
internal sealed class WpfDeployPhaseRunner(Action beforeDeploy) : IDeployPhaseRunner
{
    /// <inheritdoc />
    public async Task<int> RunAsync(Func<IShellHost, DeployPhaseOptions> createOptions, CancellationToken cancellationToken)
    {
        beforeDeploy();
        var shell = new WinPeShellWindow();
        shell.Show();
        try
        {
            return await DeployPhase.RunAsync(createOptions(shell), cancellationToken);
        }
        finally
        {
            WpfConnectPhaseRunner.Close(shell);
        }
    }
}

/// <summary>Runs the full clock synchronization, then the time zone selection, off the UI thread.</summary>
internal sealed class WinPeSystemPreparationStep(ISystemPreparation preparation) : ISystemPreparationStep
{
    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken) =>
        Task.Run(() => preparation.PrepareSystemAsync(cancellationToken), cancellationToken);
}
