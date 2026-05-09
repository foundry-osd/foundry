using Microsoft.Web.WebView2.Core;

namespace Foundry.Views;

public sealed partial class AboutReleaseNotesDialogPage : Page
{
    public AboutReleaseNotesDialogPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        DataContext = e.Parameter;
        base.OnNavigatedTo(e);
    }

    public void CloseWebView()
    {
        ReleaseNotesWebView.Close();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        CloseWebView();
    }

    private void ReleaseNotesWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        ReleaseNotesLoadingPanel.Visibility = Visibility.Visible;
        ReleaseNotesErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void ReleaseNotesWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
        ReleaseNotesErrorPanel.Visibility = args.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
    }
}
