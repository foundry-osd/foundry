// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.WinPE.Shell;

/// <summary>The button the operator chose on a <see cref="MessagePage"/>.</summary>
internal enum MessageChoice
{
    /// <summary>No button was pressed: the window was closed instead.</summary>
    None,

    /// <summary>The primary (accent) button.</summary>
    Primary,

    /// <summary>The secondary button, only offered when <see cref="MessagePageModel.SecondaryText"/> is set.</summary>
    Secondary
}

/// <summary>
/// The texts and the answer of a <see cref="MessagePage"/>: a title, a message, a primary button and an optional secondary one.
/// The caller localizes the texts, so the page works in any process that has the standalone application resources.
/// </summary>
/// <param name="title">The heading of the page.</param>
/// <param name="message">The message body.</param>
/// <param name="primaryText">The caption of the primary button.</param>
/// <param name="secondaryText">The caption of the secondary button, or <see langword="null"/> for a page with one button.</param>
internal sealed class MessagePageModel(string title, string message, string primaryText, string? secondaryText = null)
{
    /// <summary>Gets the heading of the page.</summary>
    public string Title { get; } = title;

    /// <summary>Gets the message body.</summary>
    public string Message { get; } = message;

    /// <summary>Gets the caption of the primary button.</summary>
    public string PrimaryText { get; } = primaryText;

    /// <summary>Gets the caption of the secondary button, or <see langword="null"/> when the page has none.</summary>
    public string? SecondaryText { get; } = secondaryText;

    /// <summary>Gets the button the operator chose; <see cref="MessageChoice.None"/> until one is pressed.</summary>
    public MessageChoice Choice { get; private set; }

    /// <summary>Occurs once a button was pressed, after <see cref="Choice"/> is set.</summary>
    public event EventHandler? Chosen;

    /// <summary>Records the pressed button and raises <see cref="Chosen"/>; later presses are ignored.</summary>
    internal void Choose(MessageChoice choice)
    {
        if (Choice != MessageChoice.None || choice == MessageChoice.None)
        {
            return;
        }

        Choice = choice;
        Chosen?.Invoke(this, EventArgs.Empty);
    }
}
