using System.Windows.Controls;
using Foundry.ViewModels;

namespace Foundry.Views;

public partial class StandardPage : Page
{
    public StandardPage(StandardPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
