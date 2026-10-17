// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;

namespace Foundry.Views;

/// <summary>Lists the Autopilot profiles downloaded from the tenant so the user can choose which to import.</summary>
public sealed partial class AutopilotProfileSelectionDialog : ContentDialog
{
    // The dialog pads its content by 24 px on each side; a larger allowance leaves the content narrower than the title.
    private const double DialogChromeWidth = 48;
    private const double FallbackMinimumContentWidth = 560;
    private const double FallbackMaximumContentWidth = 980;
    private const double SelectionColumnWidth = 48;
    private const double ColumnPaddingWidth = 96;
    private const double AverageCharacterWidth = 7.5;

    public AutopilotProfileSelectionDialog(AutopilotProfileSelectionDialogViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        TableViewDefaultSort.Attach(ProfilesTable, nameof(SelectableAutopilotProfileEntryViewModel.DisplayName));
        Title = ViewModel.Title;
        PrimaryButtonText = ViewModel.ImportText;
        CloseButtonText = ViewModel.CancelText;
        IsPrimaryButtonEnabled = ViewModel.HasSelectedProfiles;
        ApplyContentWidth();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    public AutopilotProfileSelectionDialogViewModel ViewModel { get; }

    // Every downloaded profile starts selected, as the view model assumes.
    private void OnProfilesTableLoaded(object sender, RoutedEventArgs e) => ProfilesTable.SelectAll();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.ReplaceSelection(ProfilesTable.SelectedItems.OfType<SelectableAutopilotProfileEntryViewModel>());

    private void OnSelectAllClick(object sender, RoutedEventArgs e) => ProfilesTable.SelectAll();

    private void OnClearClick(object sender, RoutedEventArgs e) => ProfilesTable.DeselectAll();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(AutopilotProfileSelectionDialogViewModel.HasSelectedProfiles), StringComparison.Ordinal))
        {
            IsPrimaryButtonEnabled = ViewModel.HasSelectedProfiles;
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }

    /// <summary>
    /// Fits the width to the longest names within the bounds shared by the other pickers. The height needs no
    /// calculation: the table has a fixed maximum height and scrolls beyond it.
    /// </summary>
    private void ApplyContentWidth()
    {
        double minimumContentWidth = GetApplicationDoubleResource("FoundryDialogMinWidth", FallbackMinimumContentWidth);
        double maximumContentWidth = GetApplicationDoubleResource("FoundryLargeDialogMaxWidth", FallbackMaximumContentWidth);
        double availableWindowWidth = Math.Max(minimumContentWidth, App.MainWindow.AppWindow.Size.Width * 0.72);
        int longestNameLength = ViewModel.Profiles.Select(profile => profile.DisplayName.Length).DefaultIfEmpty(0).Max();
        int longestFolderLength = ViewModel.Profiles.Select(profile => profile.FolderName.Length).DefaultIfEmpty(0).Max();
        double desiredWidth = SelectionColumnWidth + ColumnPaddingWidth +
            ((longestNameLength + longestFolderLength) * AverageCharacterWidth);
        double contentWidth = Math.Clamp(desiredWidth, minimumContentWidth, Math.Min(maximumContentWidth, availableWindowWidth));

        Resources["ContentDialogMinWidth"] = minimumContentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxWidth"] = contentWidth + DialogChromeWidth;
        DialogContentRoot.Width = contentWidth;
    }

    private static double GetApplicationDoubleResource(string key, double fallback)
    {
        return App.Current.Resources.TryGetValue(key, out object value) && value is double doubleValue
            ? doubleValue
            : fallback;
    }
}
