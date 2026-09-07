// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Common;
using Foundry.Services.Localization;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Foundry.Views;

public sealed partial class ReleaseNotesDialogPage : Page
{
    private static readonly ILogger Logger = Log.ForContext<ReleaseNotesDialogPage>();
    private readonly IApplicationLocalizationService localizationService;
    private bool isClosed;

    public ReleaseNotesDialogPage()
    {
        localizationService = App.GetService<IApplicationLocalizationService>();
        InitializeComponent();
        LocalizationRoot.BindToMainRoot(this);
        ApplyLocalizedText();
        localizationService.LanguageChanged += OnLanguageChanged;
        Unloaded += OnUnloaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not Uri releaseUri || !IsReleaseNotesUri(releaseUri))
        {
            ShowReleaseNotesError();
            return;
        }

        OpenReleaseNotesLink.NavigateUri = releaseUri;
        await InitializeReleaseNotesWebViewAsync(releaseUri);
    }

    public void CloseWebView()
    {
        if (isClosed)
        {
            return;
        }

        isClosed = true;
        localizationService.LanguageChanged -= OnLanguageChanged;
        ReleaseNotesWebView.NavigationStarting -= ReleaseNotesWebView_NavigationStarting;
        ReleaseNotesWebView.NavigationCompleted -= ReleaseNotesWebView_NavigationCompleted;
        ReleaseNotesWebView.CoreWebView2Initialized -= ReleaseNotesWebView_CoreWebView2Initialized;

        if (ReleaseNotesWebView.CoreWebView2 is not null)
        {
            ReleaseNotesWebView.CoreWebView2.DOMContentLoaded -= CoreWebView2_DOMContentLoaded;
        }

        ReleaseNotesWebView.Close();
    }

    private void ApplyLocalizedText()
    {
        ReleaseNotesLoadingText.Text = localizationService.GetString("AboutDialog.ReleaseNotesLoading");
        ReleaseNotesErrorText.Text = localizationService.GetString("AboutDialog.ReleaseNotesError");
        OpenReleaseNotesLink.Content = localizationService.GetString("AboutDialog.OpenReleaseNotes");
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!isClosed)
            {
                ApplyLocalizedText();
            }
        });
    }

    private static bool IsReleaseNotesUri(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        CloseWebView();
    }

    private void ReleaseNotesWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (isClosed || !Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri) || !IsReleaseNotesUri(uri))
        {
            args.Cancel = true;
            ShowReleaseNotesError();
            return;
        }

        ReleaseNotesLoadingPanel.Visibility = Visibility.Visible;
        ReleaseNotesErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void ReleaseNotesWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (isClosed)
        {
            return;
        }

        ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
        ReleaseNotesErrorPanel.Visibility = args.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ReleaseNotesWebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (!isClosed && args.Exception is null && sender.CoreWebView2 is not null)
        {
            sender.CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;
        }
    }

    private void CoreWebView2_DOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
    {
        if (isClosed)
        {
            return;
        }

        ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
    }

    private async Task InitializeReleaseNotesWebViewAsync(Uri releaseUri)
    {
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

            ReleaseNotesWebView.Source = releaseUri;
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
        if (isClosed)
        {
            return;
        }

        ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
        ReleaseNotesErrorPanel.Visibility = Visibility.Visible;
    }
}
