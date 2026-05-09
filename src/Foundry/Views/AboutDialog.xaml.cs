using Microsoft.Web.WebView2.Core;

namespace Foundry.Views;

public sealed partial class AboutDialog : ContentDialog
{
    private const double DialogChromeWidth = 96;
    private const double FallbackContentWidth = 920;
    private const double FallbackContentHeight = 600;

    public AboutDialog(AboutUsSettingViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Title = ViewModel.Title;
        ApplyContentLayout();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public AboutUsSettingViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SectionSelector.SelectedItem = SectionSelector.Items[0];
        ShowSection(0);
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        ReleaseNotesWebView.Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ShowSection(sender.Items.IndexOf(sender.SelectedItem));
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

    private void ShowSection(int index)
    {
        AboutSection.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        LicensesSection.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ContributorsSection.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        ReleaseNotesSection.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
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
