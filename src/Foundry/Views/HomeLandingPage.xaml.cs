// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Shell;

namespace Foundry.Views;

public sealed partial class HomeLandingPage : Page
{
    private readonly IShellNavigationGuardService shellNavigationGuardService;

    public HomeLandingViewModel ViewModel { get; }

    public HomeLandingPage()
    {
        shellNavigationGuardService = App.GetService<IShellNavigationGuardService>();
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
        Type target = shellNavigationGuardService.State == ShellNavigationState.Ready
            ? typeof(GeneralConfigurationPage)
            : typeof(AdkPage);
        App.Current.NavigationService.NavigateTo(target);
    }

    private void ReviewAndStartButton_Click(object sender, RoutedEventArgs e)
    {
        Type target = shellNavigationGuardService.State == ShellNavigationState.Ready
            ? typeof(StartPage)
            : typeof(AdkPage);
        App.Current.NavigationService.NavigateTo(target);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
