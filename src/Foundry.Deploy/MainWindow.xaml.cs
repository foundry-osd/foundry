using System.Windows;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
