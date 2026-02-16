using System.Windows.Controls;
using Foundry.ViewModels;
using Foundry.Views.Controls;

namespace Foundry.Views;

public partial class StandardPage : Page
{
    public AdkBanner Banner => AdkBannerControl;

    public StandardPage(StandardPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
