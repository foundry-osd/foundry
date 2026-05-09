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
        if (ReleaseNotesWebView.CoreWebView2 is not null)
        {
            ReleaseNotesWebView.CoreWebView2.DOMContentLoaded -= CoreWebView2_DOMContentLoaded;
        }

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

    private void ReleaseNotesWebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (args.Exception is null && sender.CoreWebView2 is not null)
        {
            sender.CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;
        }
    }

    private void CoreWebView2_DOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
    {
        ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
    }
}
