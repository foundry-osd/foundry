using System.ComponentModel;
using System.Windows;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.HandleWindowClosing();
        }

        base.OnClosing(e);
    }
}
