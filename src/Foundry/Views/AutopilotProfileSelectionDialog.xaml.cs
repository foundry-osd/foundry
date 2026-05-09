using System.ComponentModel;

namespace Foundry.Views;

public sealed partial class AutopilotProfileSelectionDialog : ContentDialog
{
    private const double DialogChromeWidth = 96;
    private const double FallbackMinimumContentWidth = 560;
    private const double FallbackMaximumContentWidth = 980;
    private const double FallbackMaximumDialogHeight = 760;
    private const double SelectionColumnWidth = 48;
    private const double ColumnPaddingWidth = 96;
    private const double AverageCharacterWidth = 7.5;
    private const double ListRowHeight = 54;
    private const int MaximumVisibleRows = 8;

    public AutopilotProfileSelectionDialog(AutopilotProfileSelectionDialogViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Title = ViewModel.Title;
        PrimaryButtonText = ViewModel.ImportText;
        CloseButtonText = ViewModel.CancelText;
        IsPrimaryButtonEnabled = ViewModel.HasSelectedProfiles;
        ApplyContentLayout();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    public AutopilotProfileSelectionDialogViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(AutopilotProfileSelectionDialogViewModel.HasSelectedProfiles), StringComparison.Ordinal))
        {
            IsPrimaryButtonEnabled = ViewModel.HasSelectedProfiles;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(AutopilotProfileSelectionDialogViewModel.Title), StringComparison.Ordinal))
        {
            Title = ViewModel.Title;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(AutopilotProfileSelectionDialogViewModel.ImportText), StringComparison.Ordinal))
        {
            PrimaryButtonText = ViewModel.ImportText;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(AutopilotProfileSelectionDialogViewModel.CancelText), StringComparison.Ordinal))
        {
            CloseButtonText = ViewModel.CancelText;
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }

    private void ApplyContentLayout()
    {
        double minimumContentWidth = GetApplicationDoubleResource("FoundryDialogMinWidth", FallbackMinimumContentWidth);
        double maximumContentWidth = GetApplicationDoubleResource("FoundryLargeDialogMaxWidth", FallbackMaximumContentWidth);
        double maximumDialogHeight = GetApplicationDoubleResource("FoundryLargeDialogMaxHeight", FallbackMaximumDialogHeight);
        double availableWindowWidth = Math.Max(minimumContentWidth, App.MainWindow.AppWindow.Size.Width * 0.72);
        double contentWidth = Math.Clamp(
            CalculateDesiredContentWidth(),
            minimumContentWidth,
            Math.Min(maximumContentWidth, availableWindowWidth));
        int visibleRows = Math.Clamp(ViewModel.Profiles.Count, 1, MaximumVisibleRows);

        Resources["ContentDialogMinWidth"] = minimumContentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxWidth"] = contentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxHeight"] = maximumDialogHeight;
        DialogContentRoot.Width = contentWidth;
        ProfilesList.Height = visibleRows * ListRowHeight;
    }

    private static double GetApplicationDoubleResource(string key, double fallback)
    {
        return App.Current.Resources.TryGetValue(key, out object value) && value is double doubleValue
            ? doubleValue
            : fallback;
    }

    private double CalculateDesiredContentWidth()
    {
        int longestNameLength = ViewModel.Profiles
            .Select(profile => profile.DisplayName.Length)
            .DefaultIfEmpty(ViewModel.NameColumnHeader.Length)
            .Max();
        int longestFolderLength = ViewModel.Profiles
            .Select(profile => profile.FolderName.Length)
            .DefaultIfEmpty(ViewModel.FolderColumnHeader.Length)
            .Max();

        double nameWidth = Math.Min(longestNameLength * AverageCharacterWidth, 420);
        double folderWidth = Math.Min(longestFolderLength * AverageCharacterWidth, 460);
        return SelectionColumnWidth + nameWidth + folderWidth + ColumnPaddingWidth;
    }
}
