using Foundry.Common;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Foundry.Views;

public sealed partial class AboutReleaseNotesDialogPage : Page
{
    private static readonly ILogger Logger = Log.ForContext<AboutReleaseNotesDialogPage>();
    private bool isClosed;

    public AboutReleaseNotesDialogPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        DataContext = e.Parameter;
        base.OnNavigatedTo(e);
        await InitializeReleaseNotesWebViewAsync(e.Parameter as AboutUsSettingViewModel);
    }

    public void CloseWebView()
    {
        isClosed = true;

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

    private async Task InitializeReleaseNotesWebViewAsync(AboutUsSettingViewModel? viewModel)
    {
        if (viewModel is null)
        {
            ShowReleaseNotesError();
            return;
        }

        try
        {
            Directory.CreateDirectory(Constants.WebView2UserDataDirectoryPath);
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                null,
                Constants.WebView2UserDataDirectoryPath,
                null);

            if (isClosed)
            {
                return;
            }

            await ReleaseNotesWebView.EnsureCoreWebView2Async(environment);

            if (isClosed)
            {
                return;
            }

            ReleaseNotesWebView.Source = viewModel.ReleasesUri;
        }
        catch (Exception ex)
        {
            Logger.Warning(
                ex,
                "Failed to initialize release notes WebView2. UserDataFolder={UserDataFolder}",
                Constants.WebView2UserDataDirectoryPath);
            ShowReleaseNotesError();
        }
    }

    private void ShowReleaseNotesError()
    {
        ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
        ReleaseNotesErrorPanel.Visibility = Visibility.Visible;
    }
}
