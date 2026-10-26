// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Windows.ApplicationModel.DataTransfer;

namespace Foundry.Views;

/// <summary>Shows <see cref="NewSecretFields"/>: the secret, its confirmation, generate and copy; the view owns the clipboard.</summary>
public sealed partial class NewSecretFieldsView : UserControl
{
    public static readonly DependencyProperty FieldsProperty = DependencyProperty.Register(
        nameof(Fields), typeof(NewSecretFields), typeof(NewSecretFieldsView), new PropertyMetadata(null, OnFieldsChanged));

    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable), typeof(bool), typeof(NewSecretFieldsView), new PropertyMetadata(true));

    public NewSecretFieldsView()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets the password fields shown.</summary>
    public NewSecretFields Fields
    {
        get => (NewSecretFields)GetValue(FieldsProperty);
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
        ((NewSecretFieldsView)sender).Bindings.Update();

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(Fields.Password);
        Clipboard.SetContent(package);
        Fields.MarkCopied();
    }
}
