namespace Foundry.Views;

/// <summary>
/// Displays modal download progress while an available update is being prepared for restart.
/// </summary>
public sealed partial class UpdateInstallProgressDialog : ContentDialog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateInstallProgressDialog"/> class.
    /// </summary>
    /// <param name="viewModel">The update settings view model that owns download progress state.</param>
    public UpdateInstallProgressDialog(AppUpdateSettingViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>
    /// Gets the update settings view model that owns progress and loading state.
    /// </summary>
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
