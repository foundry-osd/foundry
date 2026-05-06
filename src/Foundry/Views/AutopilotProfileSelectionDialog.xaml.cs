using System.ComponentModel;

namespace Foundry.Views;

public sealed partial class AutopilotProfileSelectionDialog : ContentDialog
{
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
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }
}
