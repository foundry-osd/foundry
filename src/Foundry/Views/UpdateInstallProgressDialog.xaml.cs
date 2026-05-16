namespace Foundry.Views;

public sealed partial class UpdateInstallProgressDialog : ContentDialog
{
    public UpdateInstallProgressDialog(AppUpdateSettingViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public AppUpdateSettingViewModel ViewModel { get; }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsLoading)
        {
            args.Cancel = true;
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Closing -= OnClosing;
        Closed -= OnClosed;
    }
}
