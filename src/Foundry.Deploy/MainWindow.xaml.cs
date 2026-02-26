using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dispose();
        }

        base.OnClosed(e);
    }
}
