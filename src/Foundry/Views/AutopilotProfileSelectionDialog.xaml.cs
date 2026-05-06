using System.ComponentModel;

namespace Foundry.Views;

public sealed partial class AutopilotProfileSelectionDialog : ContentDialog
{
    private const double DialogHorizontalMargin = 360;
    private const double DialogVerticalMargin = 260;
    private const double MinimumDialogContentWidth = 520;
    private const double MaximumDialogContentWidth = 980;
    private const double MinimumDialogContentHeight = 360;
    private const double MaximumDialogContentHeight = 640;

    public AutopilotProfileSelectionDialog(AutopilotProfileSelectionDialogViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Title = ViewModel.Title;
        PrimaryButtonText = ViewModel.ImportText;
        CloseButtonText = ViewModel.CancelText;
        IsPrimaryButtonEnabled = ViewModel.HasSelectedProfiles;
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
        App.MainWindow.SizeChanged -= OnMainWindowSizeChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.MainWindow.SizeChanged -= OnMainWindowSizeChanged;
        App.MainWindow.SizeChanged += OnMainWindowSizeChanged;
        UpdateDialogSize();
    }

    private void OnMainWindowSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        UpdateDialogSize();
    }

    private void UpdateDialogSize()
    {
        double windowWidth = App.MainWindow.AppWindow.Size.Width;
        double windowHeight = App.MainWindow.AppWindow.Size.Height;
        double contentWidth = Math.Clamp(
            windowWidth - DialogHorizontalMargin,
            MinimumDialogContentWidth,
            MaximumDialogContentWidth);
        double contentHeight = Math.Clamp(
            windowHeight - DialogVerticalMargin,
            MinimumDialogContentHeight,
            MaximumDialogContentHeight);

        MaxWidth = contentWidth + 96;
        MaxHeight = contentHeight + 180;
        DialogContentRoot.Width = contentWidth;
        DialogContentRoot.MaxHeight = contentHeight;
    }
}
