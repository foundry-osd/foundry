using Foundry.Services.Shell;
using Microsoft.UI.Xaml.Input;
using Windows.System;

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

    private void OpenAdkTile_Tapped(object sender, TappedRoutedEventArgs e)
    {
        NavigateToAdk();
    }

    private void ConfigureMediaTile_Tapped(object sender, TappedRoutedEventArgs e)
    {
        NavigateToGeneral();
    }

    private void ReviewAndStartTile_Tapped(object sender, TappedRoutedEventArgs e)
    {
        NavigateToStart();
    }

    private void OpenAdkTile_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        NavigateFromKeyboard(e, NavigateToAdk);
    }

    private void ConfigureMediaTile_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        NavigateFromKeyboard(e, NavigateToGeneral);
    }

    private void ReviewAndStartTile_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        NavigateFromKeyboard(e, NavigateToStart);
    }

    private static void NavigateFromKeyboard(KeyRoutedEventArgs e, Action navigate)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space))
        {
            return;
        }

        e.Handled = true;
        navigate();
    }

    private void NavigateToAdk()
    {
        App.Current.NavService.NavigateTo(typeof(AdkPage), ViewModel.AdkNavigationTitle);
    }

    private void NavigateToGeneral()
    {
        if (shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            NavigateToAdk();
            return;
        }

        App.Current.NavService.NavigateTo(typeof(GeneralConfigurationPage), ViewModel.GeneralNavigationTitle);
    }

    private void NavigateToStart()
    {
        if (shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            NavigateToAdk();
            return;
        }

        App.Current.NavService.NavigateTo(typeof(StartPage), ViewModel.StartNavigationTitle);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
