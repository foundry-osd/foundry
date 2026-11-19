// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect;
using Foundry.Deploy;
using Foundry.WinPE.SystemPreparation;

namespace Foundry.WinPE.Ui;

/// <summary>Runs <see cref="ConnectPhase"/> in the host's WPF application, in the shell its options carry.</summary>
internal sealed class WpfConnectPhaseRunner : IConnectPhaseRunner
{
    /// <inheritdoc />
    public Task<ConnectPhaseResult> RunAsync(ConnectPhaseOptions options, CancellationToken cancellationToken) =>
        ConnectPhase.RunAsync(options, cancellationToken);
}

/// <summary>
/// Runs <see cref="DeployPhase"/> in the host's WPF application, in the shell its options carry, after
/// <paramref name="beforeDeploy"/> switched the host's log file and remote diagnostics application to Deploy.
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
internal sealed class WinPeSystemPreparationStep(WinPeSystemPreparation preparation) : ISystemPreparationStep
{
    /// <inheritdoc />
    /// <remarks>A failed clock sync is not an exception: the preparation stays unusable and the time zone step still runs.</remarks>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() => preparation.PrepareSystemAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        return preparation.IsClockUsable;
    }
}
