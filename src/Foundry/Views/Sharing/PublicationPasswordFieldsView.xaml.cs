// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Windows.ApplicationModel.DataTransfer;

namespace Foundry.Views;

/// <summary>New publication password, confirmation, generate and copy; the view owns the clipboard.</summary>
public sealed partial class PublicationPasswordFieldsView : UserControl
{
    public static readonly DependencyProperty FieldsProperty = DependencyProperty.Register(
        nameof(Fields), typeof(PublicationPasswordFields), typeof(PublicationPasswordFieldsView), new PropertyMetadata(null, OnFieldsChanged));

    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable), typeof(bool), typeof(PublicationPasswordFieldsView), new PropertyMetadata(true));

    public PublicationPasswordFieldsView()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets the password fields shown.</summary>
    public PublicationPasswordFields Fields
    {
        get => (PublicationPasswordFields)GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    /// <summary>Gets or sets whether the fields can be edited.</summary>
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    private PasswordRevealMode RevealMode(bool isRevealed) => isRevealed ? PasswordRevealMode.Visible : PasswordRevealMode.Peek;

    private bool CanCopy(bool isEditable, bool canCopy) => isEditable && canCopy;

    private static void OnFieldsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((PublicationPasswordFieldsView)sender).Bindings.Update();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(Fields.Password);
        Clipboard.SetContent(package);
        Fields.MarkCopied();
    }
}
