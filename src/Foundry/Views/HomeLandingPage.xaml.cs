// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class HomeLandingPage : Page
{
    public HomeLandingViewModel ViewModel { get; }

    public HomeLandingPage()
    {
        ViewModel = App.GetService<HomeLandingViewModel>();
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OpenAdkButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.NavigationService.NavigateTo(typeof(AdkPage));
    }

    private void ConfigureMediaButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.NavigationService.NavigateTo(typeof(GeneralConfigurationPage));
    }

    private void ReviewAndStartButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.NavigationService.NavigateTo(typeof(StartPage));
    }

    private async void OpenDocumentationButton_Click(object sender, RoutedEventArgs e)
    {
        await ((MainWindow)App.MainWindow).OpenDocumentationAsync();
    }

    private void StepperStep1_Requested(object sender, EventArgs e)
    {
        App.Current.NavigationService.NavigateTo(typeof(AdkPage));
    }

    private void StepperStep2_Requested(object sender, EventArgs e)
    {
        App.Current.NavigationService.NavigateTo(typeof(GeneralConfigurationPage));
    }

    private void StepperStep3_Requested(object sender, EventArgs e)
    {
        App.Current.NavigationService.NavigateTo(typeof(StartPage));
    }

    private void AdkCard_NavigationRequested(object sender, EventArgs e)
    {
        App.Current.NavigationService.NavigateTo(typeof(AdkPage));
    }

    private void ConfigCard_NavigationRequested(object sender, EventArgs e)
    {
        App.Current.NavigationService.NavigateTo(typeof(GeneralConfigurationPage));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
