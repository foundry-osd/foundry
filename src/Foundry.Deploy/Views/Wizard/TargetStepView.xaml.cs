using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Foundry.Deploy.Validation;

namespace Foundry.Deploy.Views.Wizard;

public partial class TargetStepView : UserControl
{
    private bool _isPasteHandlerAttached;

    public TargetStepView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isPasteHandlerAttached)
        {
            return;
        }

        DataObject.AddPastingHandler(TargetComputerNameTextBox, TargetComputerNameTextBox_OnPaste);
        _isPasteHandlerAttached = true;
    }

    private void TargetComputerNameTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox { IsReadOnly: true })
        {
            e.Handled = true;
            return;
        }

        if (!ComputerNameRules.IsAllowedText(e.Text))
        {
            e.Handled = true;
        }
    }

    private void TargetComputerNameTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            e.CancelCommand();
            return;
        }

        if (textBox.IsReadOnly)
        {
            e.CancelCommand();
            return;
        }

        string? pastedText = e.DataObject.GetDataPresent(DataFormats.UnicodeText)
            ? e.DataObject.GetData(DataFormats.UnicodeText) as string
            : e.DataObject.GetDataPresent(DataFormats.Text)
                ? e.DataObject.GetData(DataFormats.Text) as string
                : null;

        if (pastedText is null || !ComputerNameRules.IsAllowedText(pastedText))
        {
            e.CancelCommand();
            return;
        }

        string currentText = textBox.Text ?? string.Empty;
        int selectionStart = Math.Clamp(textBox.SelectionStart, 0, currentText.Length);
        int selectionLength = Math.Clamp(textBox.SelectionLength, 0, currentText.Length - selectionStart);
        string nextText = currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, pastedText);

        if (textBox.MaxLength > 0 && nextText.Length > textBox.MaxLength)
        {
            e.CancelCommand();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isPasteHandlerAttached)
        {
            return;
        }

        DataObject.RemovePastingHandler(TargetComputerNameTextBox, TargetComputerNameTextBox_OnPaste);
        _isPasteHandlerAttached = false;
    }
}
