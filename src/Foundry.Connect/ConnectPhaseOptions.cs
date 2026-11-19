// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Shell;

namespace Foundry.Connect;

/// <summary>Hooks the Foundry.WinPE UI host gives the Foundry Connect phase.</summary>
public sealed class ConnectPhaseOptions
{
    /// <summary>
    /// Gets the shell that shows Connect's content and owns the window: the dialog owner, the page transition and the close
    /// policy. Connect shows its status page, then its content unless the network gate skips it, handles the shell's close
    /// requests while the phase runs, and keeps the last page shown after a successful end.
    /// </summary>
    public required IShellHost Shell { get; init; }

    /// <summary>
    /// Gets the readiness hook: called once, on the UI thread, when Connect's first content renders, which is its "Looking for
    /// the network" status page (the deployment password prompt and the other dialogs open over it, only after it rendered).
    /// It is not called when Connect fails before any content rendered, nor when <see cref="Revisit"/> is set.
    /// </summary>
    public required Action OnFirstContentRendered { get; init; }

    /// <summary>
    /// Gets the title of Connect's status page, or <see langword="null"/> to use Connect's own application name. The host
    /// sets it so the status pages of the whole startup share one title.
    /// </summary>
    public string? StatusPageTitle { get; init; }

    /// <summary>
    /// Gets the hook called once, on the UI thread, when Connect loaded its configuration and built its services, before its
    /// content is created, or <see langword="null"/>. It is not called when the configuration cannot be loaded, nor when
    /// <see cref="Revisit"/> is set.
    /// </summary>
    public Action? OnConfigurationLoaded { get; init; }

    /// <summary>
    /// Gets the hook called with the UI culture name (for example <c>fr-FR</c>) once, on the UI thread, when Connect's services
    /// are built, and again each time the operator changes the language, or <see langword="null"/>. It is not called when the
    /// configuration cannot be loaded.
    /// </summary>
    public Action<string>? OnCultureChanged { get; init; }

    /// <summary>
    /// Gets the theme a Connect page reopened from the deployment (<see cref="Revisit"/>) starts with, so it shows the
    /// deployment's current theme: a <c>Foundry.Wpf.Theme.ThemeMode</c> name such as <c>Dark</c>, or <see langword="null"/>
    /// (or an unknown name) to keep the default. The first Connect page ignores it.
    /// </summary>
    public string? ThemeMode { get; init; }

    /// <summary>
    /// Gets the UI culture a Connect page reopened from the deployment (<see cref="Revisit"/>) starts with, so it shows the
    /// deployment's current language: for example <c>fr-FR</c>, or <see langword="null"/> (or an unknown name) to keep the
    /// default. The first Connect page ignores it.
    /// </summary>
    public string? CultureName { get; init; }

    /// <summary>
    /// Gets the state handed to a Connect page reopened from the deployment's Network menu, or <see langword="null"/> for the
    /// first Connect page. When set, Connect neither shows its status page nor runs the network gate: it replays the previous
    /// provisioning (see <see cref="ConnectRevisit.PreviousProvisioning"/>), shows its page with "Back to deployment" and no
    /// countdown, sends no telemetry, never asks for the wired 802.1X password at startup, and leaves the close requests, the
    /// dispatcher's unhandled exceptions and the startup hooks to the deployment.
    /// </summary>
    public ConnectRevisit? Revisit { get; init; }
}
