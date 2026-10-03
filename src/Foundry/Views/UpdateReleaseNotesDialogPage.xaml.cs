// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Common;
using Foundry.Services.Updates;
using Microsoft.Web.WebView2.Core;
using Serilog;
using System.Net;

namespace Foundry.Views;

/// <summary>
/// Hosts the WebView2 instance used inside the update release notes dialog.
/// </summary>
public sealed partial class UpdateReleaseNotesDialogPage : Page
{
    private static readonly ILogger Logger = Log.ForContext<UpdateReleaseNotesDialogPage>();
    private ulong? currentNavigationId;
    private string? releaseNotesVersion;
    private bool isClosed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateReleaseNotesDialogPage"/> class.
    /// </summary>
    public UpdateReleaseNotesDialogPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        UpdateReleaseNotesDialog? dialog = e.Parameter as UpdateReleaseNotesDialog;
        DataContext = dialog?.ViewModel;
        base.OnNavigatedTo(e);
        await InitializeReleaseNotesWebViewAsync(dialog?.ReleaseNotes);
    }

    /// <summary>
    /// Releases the WebView2 instance and detaches browser event handlers before the dialog is disposed.
    /// </summary>
    public void CloseWebView()
    {
        isClosed = true;
        currentNavigationId = null;

        ReleaseNotesWebView.NavigationStarting -= ReleaseNotesWebView_NavigationStarting;
        ReleaseNotesWebView.NavigationCompleted -= ReleaseNotesWebView_NavigationCompleted;
        ReleaseNotesWebView.CoreWebView2Initialized -= ReleaseNotesWebView_CoreWebView2Initialized;

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
        if (isClosed)
        {
            args.Cancel = true;
            return;
        }

        if (!string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;

            if (currentNavigationId is null)
            {
                string uriScheme = Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri) ? uri.Scheme : "unknown";
                Logger.Warning(
                    "Blocked initial update release notes navigation. Version={Version}, NavigationId={NavigationId}, UriScheme={UriScheme}",
                    releaseNotesVersion,
                    args.NavigationId,
                    uriScheme);
                ShowReleaseNotesError();
            }

            if (currentNavigationId == args.NavigationId)
            {
                // A blocked redirect can retain the accepted document's navigation ID.
                currentNavigationId = null;
                ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
            }

            return;
        }

        currentNavigationId = args.NavigationId;
        ReleaseNotesLoadingPanel.Visibility = Visibility.Visible;
        ReleaseNotesErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void ReleaseNotesWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (isClosed || currentNavigationId != args.NavigationId)
        {
            return;
        }

        ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
        ReleaseNotesErrorPanel.Visibility = args.IsSuccess ? Visibility.Collapsed : Visibility.Visible;

        if (!args.IsSuccess)
        {
            Logger.Warning(
                "Failed to load update release notes document. Version={Version}, NavigationId={NavigationId}, WebErrorStatus={WebErrorStatus}",
                releaseNotesVersion,
                args.NavigationId,
                args.WebErrorStatus);
        }
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
        if (isClosed || currentNavigationId != args.NavigationId)
        {
            return;
        }

        ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
    }

    private async Task InitializeReleaseNotesWebViewAsync(ApplicationUpdateCheckResult? releaseNotes)
    {
        if (isClosed)
        {
            return;
        }

        releaseNotesVersion = releaseNotes?.Version;

        if (releaseNotes is null || (string.IsNullOrWhiteSpace(releaseNotes.NotesHtml) && string.IsNullOrWhiteSpace(releaseNotes.NotesMarkdown)))
        {
            ReleaseNotesLoadingPanel.Visibility = Visibility.Collapsed;
            ReleaseNotesWebView.Visibility = Visibility.Collapsed;
            ReleaseNotesUnavailablePanel.Visibility = Visibility.Visible;
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

            CoreWebView2Settings settings = ReleaseNotesWebView.CoreWebView2.Settings;
            settings.IsScriptEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsWebMessageEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            string content = !string.IsNullOrWhiteSpace(releaseNotes.NotesHtml)
                ? releaseNotes.NotesHtml
                : $"<pre>{WebUtility.HtmlEncode(releaseNotes.NotesMarkdown)}</pre>";
            ReleaseNotesWebView.NavigateToString(
                "<!doctype html><html><head><meta charset=\"utf-8\">"
                + "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'\">"
                + "<style>body{font-family:system-ui,sans-serif;margin:24px;overflow-wrap:anywhere}pre{white-space:pre-wrap;font-family:inherit}</style>"
                + "</head><body>" + content + "</body></html>");
        }
        catch (Exception ex)
        {
            Logger.Warning(
                ex,
                "Failed to initialize update release notes WebView2. UserDataFolder={UserDataFolder}",
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
