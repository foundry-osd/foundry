// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Updates;

namespace Foundry.Views;

/// <summary>
/// Displays release notes captured with an update target in a modal WebView2 dialog.
/// </summary>
public sealed partial class UpdateReleaseNotesDialog : ContentDialog
{
    private const double DialogChromeWidth = 64;
    private const double FallbackContentWidth = 920;
    private const double FallbackContentHeight = 600;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateReleaseNotesDialog"/> class.
    /// </summary>
    /// <param name="viewModel">The update settings view model that provides localized strings.</param>
    /// <param name="releaseNotes">The target snapshot whose notes remain fixed for the lifetime of this dialog.</param>
    public UpdateReleaseNotesDialog(AppUpdateSettingViewModel viewModel, ApplicationUpdateCheckResult releaseNotes)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ReleaseNotes = releaseNotes ?? throw new ArgumentNullException(nameof(releaseNotes));
        InitializeComponent();
        DataContext = ViewModel;
        ApplyContentLayout();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// Gets the update settings view model that provides release notes dialog state.
    /// </summary>
    public AppUpdateSettingViewModel ViewModel { get; }

    /// <summary>
    /// Gets the captured target, independent of later update checks or progress publications.
    /// </summary>
    public ApplicationUpdateCheckResult ReleaseNotes { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.Content is null)
        {
            ContentFrame.Navigate(typeof(UpdateReleaseNotesDialogPage), this);
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void ApplyContentLayout()
    {
        double maximumContentWidth = GetApplicationDoubleResource("FoundryLargeDialogMaxWidth", FallbackContentWidth);
        double maximumDialogHeight = GetApplicationDoubleResource("FoundryLargeDialogMaxHeight", FallbackContentHeight);
        double availableWindowWidth = Math.Max(720, App.MainWindow.AppWindow.Size.Width * 0.72);
        double availableWindowHeight = Math.Max(520, App.MainWindow.AppWindow.Size.Height * 0.72);

        double contentWidth = Math.Min(maximumContentWidth, availableWindowWidth);
        double contentHeight = Math.Min(maximumDialogHeight, availableWindowHeight);

        Resources["ContentDialogMinWidth"] = contentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxWidth"] = contentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxHeight"] = contentHeight + DialogChromeWidth;
        DialogContentRoot.Width = contentWidth;
        DialogContentRoot.Height = contentHeight;
    }

    private static double GetApplicationDoubleResource(string key, double fallback)
    {
        return App.Current.Resources.TryGetValue(key, out object value) && value is double doubleValue
            ? doubleValue
            : fallback;
    }
}
