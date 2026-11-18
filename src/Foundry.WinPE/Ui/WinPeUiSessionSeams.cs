// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Deploy;
using Foundry.Wpf.Shell;

namespace Foundry.WinPE.Ui;

/// <summary>Runs the Foundry Connect phase for <see cref="WinPeUiSession"/>.</summary>
internal interface IConnectPhaseRunner
{
    /// <summary>Runs the phase on the UI thread and completes when it ends.</summary>
    /// <param name="onConfigurationLoaded">Called once Connect loaded its configuration and built its services.</param>
    /// <param name="onFirstContentRendered">Called once Connect's first content (its page or the password prompt) rendered.</param>
    /// <param name="cancellationToken">Ends a running phase as a user abort.</param>
    Task<ConnectPhaseOutcome> RunAsync(Action onConfigurationLoaded, Action onFirstContentRendered, CancellationToken cancellationToken);
}

/// <summary>What the Foundry Connect phase hands to the rest of the session.</summary>
/// <param name="ExitCode">A <c>ChildRuntimeExitCode</c> value; only 0 continues the session.</param>
/// <param name="DeploymentKey">The deployment key unlocked in Connect, owned by the session, or <see langword="null"/>.</param>
/// <param name="ThemeMode">The theme Connect ended with.</param>
/// <param name="CultureName">The UI culture Connect ended with.</param>
/// <param name="Window">Connect's window, left open and disabled until Deploy rendered, or <see langword="null"/>.</param>
internal sealed record ConnectPhaseOutcome(int ExitCode, DeploymentKeyTransfer? DeploymentKey, string ThemeMode, string CultureName,
    IHandOverWindow? Window);

/// <summary>A window kept open while the next phase starts, so the screen never goes blank between phases.</summary>
internal interface IHandOverWindow
{
    /// <summary>Closes the window; called once, on the UI thread.</summary>
    void Close();
}

/// <summary>Prepares the clock and time zone between the phases.</summary>
internal interface ISystemPreparationStep
{
    /// <summary>Runs the preparation off the UI thread; a failure is the caller's to tolerate.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>Runs the Foundry Deploy phase for <see cref="WinPeUiSession"/>.</summary>
internal interface IDeployPhaseRunner
{
    /// <summary>Runs the phase on the UI thread and returns its exit code.</summary>
    /// <param name="createOptions">Builds the phase options around the shell the runner shows the phase in.</param>
    /// <param name="cancellationToken">Asks the phase to end.</param>
    Task<int> RunAsync(Func<IShellHost, DeployPhaseOptions> createOptions, CancellationToken cancellationToken);
}
