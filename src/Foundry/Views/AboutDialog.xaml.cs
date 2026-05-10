using Microsoft.UI.Xaml.Media.Animation;

namespace Foundry.Views;

public sealed partial class AboutDialog : ContentDialog
{
    private const double DialogChromeWidth = 64;
    private const double FallbackContentWidth = 920;
    private const double FallbackContentHeight = 600;
    private int previousSelectedIndex;

    public AboutDialog(AboutUsSettingViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        ApplyContentLayout();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public AboutUsSettingViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        previousSelectedIndex = 0;
        SectionSelector.SelectedItem = SectionSelector.Items[0];
        if (ContentFrame.Content is null)
        {
            NavigateToSection(0);
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

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        NavigateToSection(sender.Items.IndexOf(sender.SelectedItem));
    }

    private void NavigateToSection(int index)
    {
        if (index < 0)
        {
            return;
        }

        Type pageType = index switch
        {
            0 => typeof(AboutOverviewDialogPage),
            1 => typeof(AboutLicensesDialogPage),
            2 => typeof(AboutContributorsDialogPage),
            3 => typeof(AboutReleaseNotesDialogPage),
            _ => typeof(AboutOverviewDialogPage)
        };

        SlideNavigationTransitionEffect effect = index >= previousSelectedIndex
            ? SlideNavigationTransitionEffect.FromRight
            : SlideNavigationTransitionEffect.FromLeft;

        ContentFrame.Navigate(
            pageType,
            ViewModel,
            new SlideNavigationTransitionInfo { Effect = effect });

        previousSelectedIndex = index;
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
