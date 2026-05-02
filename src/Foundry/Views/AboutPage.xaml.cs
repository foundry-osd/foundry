namespace Foundry.Views
{
    public sealed partial class AboutPage : Page
    {
        public AboutUsSettingViewModel ViewModel { get; }

        public AboutPage()
        {
            ViewModel = App.GetService<AboutUsSettingViewModel>();
            InitializeComponent();
        }
    }
}
