using System.Windows.Controls;
using Foundry.ViewModels;
using Foundry.Views.Controls;

namespace Foundry.Views;

public partial class AdvancedPage : Page
{
    public AdkBanner Banner => AdkBannerControl;

    public AdvancedPage(AdvancedPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
