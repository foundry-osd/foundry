// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.WinPE.Shell;

namespace Foundry.WinPE.Ui;

/// <summary>What the operator chose in the crash window.</summary>
internal enum UiCrashChoice
{
    /// <summary>The window was closed: the boot ends.</summary>
    Close,

    /// <summary>A new UI process starts, from the Connect phase.</summary>
    Relaunch
}

/// <summary>
/// Shows the localized crash screen of a UI process that stopped unexpectedly: a <see cref="MessagePage"/> in the shell window
/// with the message, the log folder, "Relaunch" when allowed, and "Close". It holds no decision logic beyond mapping the button
/// to a <see cref="UiCrashChoice"/>.
/// </summary>
/// <remarks>
/// The guardian is a console process: the window is shown without detaching the startnet console, which keeps showing the
/// boot progress. The window stays on top, so a console that owns the foreground cannot hide it. The texts follow the culture
/// the UI process recorded in its state file (the language the operator chose), not the Windows PE default.
/// </remarks>
internal static class UiCrashWindow
{
    /// <summary>
    /// Shows the shell window with the crash page on the calling STA thread, creating the process's WPF application when needed,
    /// returns when the window closes, and returns the operator's choice.
    /// </summary>
    /// <param name="relaunchAllowed">Whether "Relaunch" is offered: only when no destructive step had started.</param>
    /// <param name="logDirectory">The folder holding the session logs, or <see langword="null"/> when none is known.</param>
    /// <param name="culture">
    /// The culture the UI process recorded, or <see langword="null"/>. A supported Foundry culture becomes the thread's UI
    /// culture; anything else leaves it unchanged.
    /// </param>
    internal static UiCrashChoice Show(bool relaunchAllowed, string? logDirectory, string? culture)
    {
        CultureInfo? resolved = ResolveCulture(culture);
        if (resolved is not null)
        {
            CultureInfo.CurrentUICulture = resolved;
        }

        WinPeUiHost.ConfigureFluentBackdrop();
        WinPeDialogApplication.EnsureCreated();
        MessagePageModel model = CreateModel(relaunchAllowed, logDirectory, culture);
        MessageChoice choice = MessageWindow.ShowModal(model, topmost: true);
        return ToChoice(relaunchAllowed, choice);
    }

    /// <summary>
    /// Returns the supported Foundry culture named by <paramref name="culture"/>, or <see langword="null"/> when it is missing,
    /// malformed or not offered by Foundry.
    /// </summary>
    internal static CultureInfo? ResolveCulture(string? culture) => UiStateFile.TryGetSupportedCulture(culture);

    /// <summary>Builds the texts of the crash page in the culture of <paramref name="culture"/>, or the current UI culture.</summary>
    internal static MessagePageModel CreateModel(bool relaunchAllowed, string? logDirectory, string? culture)
    {
        CultureInfo resolved = ResolveCulture(culture) ?? CultureInfo.CurrentUICulture;
        string message = relaunchAllowed
            ? WinPeDialogApplication.Localize("UiCrash.MessageRelaunchAllowed",
                "Foundry stopped unexpectedly before the deployment changed the target disk. You can relaunch Foundry, which starts again from the network connection.",
                resolved)
            : WinPeDialogApplication.Localize("UiCrash.MessageRelaunchRefused",
                "Foundry stopped unexpectedly. The deployment may already have changed the target disk, so Foundry cannot be relaunched. Restart the computer to start a new deployment.",
                resolved);
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            message += Environment.NewLine + Environment.NewLine + string.Format(resolved,
                WinPeDialogApplication.Localize("UiCrash.LogFolder", "Logs: {0}", resolved), logDirectory);
        }

        string title = WinPeDialogApplication.Localize("UiCrash.Title", "Foundry stopped", resolved);
        string close = WinPeDialogApplication.Localize("UiCrash.Close", "Close", resolved);
        return relaunchAllowed
            ? new MessagePageModel(title, message, WinPeDialogApplication.Localize("UiCrash.Relaunch", "Relaunch", resolved), close)
            : new MessagePageModel(title, message, close);
    }

    /// <summary>
    /// Maps the operator's answer to the choice: only the primary button of a page that offers "Relaunch" relaunches; closing
    /// the window or pressing "Close" closes.
    /// </summary>
    /// <param name="relaunchAllowed">Whether the page offered "Relaunch" as its primary button.</param>
    /// <param name="choice">The button the operator pressed, or <see cref="MessageChoice.None"/> when the window was closed.</param>
    internal static UiCrashChoice ToChoice(bool relaunchAllowed, MessageChoice choice) =>
        relaunchAllowed && choice == MessageChoice.Primary ? UiCrashChoice.Relaunch : UiCrashChoice.Close;
}
