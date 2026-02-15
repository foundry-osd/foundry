using System.Windows.Controls;
using Foundry.ViewModels;

namespace Foundry.Views;

public partial class AdvancedPage : Page
{
    public AdvancedPage(AdvancedPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
