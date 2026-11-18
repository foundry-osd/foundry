// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Shell;

namespace Foundry.Deploy;

/// <summary>What the Foundry.WinPE UI host gives the Foundry Deploy phase: the Connect hand-over and the host hooks.</summary>
public sealed class DeployPhaseOptions
{
    /// <summary>
    /// Gets the shell that shows Deploy's content. The phase sets <see cref="System.Windows.Application.MainWindow"/> to its
    /// window, handles its close requests while the phase runs and detaches its handler when the phase ends; the window stays
    /// open, the host closes it.
    /// </summary>
    public required IShellHost Shell { get; init; }

    /// <summary>
    /// Gets the deployment key unlocked in the Connect phase, or <see langword="null"/>. The access gate tries it, after its
    /// key check, before the first password prompt; the phase disposes it on every path, used or not.
    /// </summary>
    public DeploymentKeyTransfer? InitialDeploymentKey { get; init; }

    /// <summary>
    /// Gets the theme Connect ended with, a <c>Foundry.Wpf.Theme.ThemeMode</c> name, or <see langword="null"/> for the default.
    /// </summary>
    public string? ThemeMode { get; init; }

    /// <summary>Gets the UI culture Connect ended with, for example <c>fr-FR</c>, or <see langword="null"/> for the default.</summary>
    public string? CultureName { get; init; }

    /// <summary>
    /// Gets the hook called once, on the UI thread, when Deploy's first content renders: its page in the shell, or a startup dialog
    /// (deployment password, network cache) if one renders first.
    /// </summary>
    public required Action OnFirstContentRendered { get; init; }

    /// <summary>
    /// Gets the hook called once, synchronously and before the target disk layout step runs, when a live deployment reaches
    /// the first step that can wipe the target disk. Never called in a dry run.
    /// </summary>
    public required Action OnDestructiveStepStarted { get; init; }
}
