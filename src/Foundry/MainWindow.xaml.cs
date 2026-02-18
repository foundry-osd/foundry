using System.Windows;
using Foundry.ViewModels;

namespace Foundry;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => _ = viewModel.RefreshUsbCandidatesCommand.ExecuteAsync(null);
    }
}
