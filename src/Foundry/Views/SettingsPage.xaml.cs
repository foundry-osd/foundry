using Microsoft.UI.Xaml.Controls;

namespace Foundry.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel viewModel ||
            sender is not ComboBox comboBox ||
            comboBox.SelectedValue is not string cultureCode)
        {
            return;
        }

        viewModel.SetCultureCommand.Execute(cultureCode);
    }
}
