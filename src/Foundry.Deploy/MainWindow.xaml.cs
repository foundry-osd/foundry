using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Foundry.Deploy.Validation;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataObject.AddPastingHandler(TargetComputerNameTextBox, TargetComputerNameTextBox_OnPaste);
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    private void WizardTabControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TabControl tabControl || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(tabControl, source) is TabItem)
        {
            e.Handled = true;
        }
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

    protected override void OnClosed(EventArgs e)
    {
        DataObject.RemovePastingHandler(TargetComputerNameTextBox, TargetComputerNameTextBox_OnPaste);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }
}
