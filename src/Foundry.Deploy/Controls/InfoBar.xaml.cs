// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;

namespace Foundry.Deploy.Controls;

/// <summary>Says how important the message of an <see cref="InfoBar"/> is, as the WinUI 3 InfoBar does.</summary>
public enum InfoBarSeverity
{
    /// <summary>Neutral information, such as an operation in progress.</summary>
    Informational,

    /// <summary>An operation succeeded.</summary>
    Success,

    /// <summary>Something needs attention but does not block the technician.</summary>
    Warning,

    /// <summary>Something failed or blocks the technician.</summary>
    Error
}

/// <summary>
/// A WPF card aligned on the WinUI 3 InfoBar: a filled severity glyph, an optional title, a message and an optional content
/// area below them (for example a progress bar). Colors come from the Fluent theme severity brushes, so the card follows the
/// light and dark themes.
/// </summary>
public partial class InfoBar : UserControl
{
    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity),
        typeof(InfoBarSeverity),
        typeof(InfoBar),
        new PropertyMetadata(InfoBarSeverity.Informational));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(InfoBar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(string),
        typeof(InfoBar),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body),
        typeof(object),
        typeof(InfoBar),
        new PropertyMetadata(null));

    public InfoBar()
    {
        InitializeComponent();
    }

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>Gets or sets the bold first line; an empty title is not shown.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Gets or sets optional content shown below the message, such as an indeterminate progress bar.</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }
}
