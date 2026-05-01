using Foundry.Core.Localization;

namespace Foundry.Views;

public sealed partial class GeneralSettingPage : Page
{
    private bool isInitializingLanguageSelection = true;

    public GeneralSettingViewModel ViewModel { get; }

    public GeneralSettingPage()
    {
        ViewModel = App.GetService<GeneralSettingViewModel>();
        InitializeComponent();
        isInitializingLanguageSelection = false;
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isInitializingLanguageSelection)
        {
            return;
        }

        if (e.AddedItems.FirstOrDefault() is SupportedCultureOption selectedLanguage)
        {
            await ViewModel.SetLanguageAsync(selectedLanguage);
        }
    }
}
