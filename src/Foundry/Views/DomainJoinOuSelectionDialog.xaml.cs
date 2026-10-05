// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;

namespace Foundry.Views;

/// <summary>Lists the organizational units found in the authoring computer's domain so the user can choose which to add.</summary>
public sealed partial class DomainJoinOuSelectionDialog : ContentDialog
{
    // The dialog pads its content by 24 px on each side; a larger allowance leaves the content narrower than the title.
    private const double DialogChromeWidth = 48;
    private const double FallbackMinimumContentWidth = 560;
    private const double FallbackMaximumContentWidth = 980;
    private const double SelectionColumnWidth = 48;
    private const double ColumnPaddingWidth = 96;
    private const double AverageCharacterWidth = 7.5;

    public DomainJoinOuSelectionDialog(DomainJoinOuSelectionDialogViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        Title = ViewModel.Title;
        PrimaryButtonText = ViewModel.AddText;
        CloseButtonText = ViewModel.CancelText;
        IsPrimaryButtonEnabled = ViewModel.HasSelection;
        ApplyContentWidth();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    public DomainJoinOuSelectionDialogViewModel ViewModel { get; }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.ReplaceSelection(OrganizationalUnitsTable.SelectedItems.OfType<DomainJoinOrganizationalUnitEntryViewModel>());

    private void OnSelectAllClick(object sender, RoutedEventArgs e) => OrganizationalUnitsTable.SelectAll();

    private void OnClearClick(object sender, RoutedEventArgs e) => OrganizationalUnitsTable.DeselectAll();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(DomainJoinOuSelectionDialogViewModel.HasSelection), StringComparison.Ordinal))
        {
            IsPrimaryButtonEnabled = ViewModel.HasSelection;
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
        int longestNameLength = ViewModel.OrganizationalUnits.Select(entry => entry.DisplayName.Length).DefaultIfEmpty(0).Max();
        int longestDistinguishedNameLength = ViewModel.OrganizationalUnits.Select(entry => entry.DistinguishedName.Length).DefaultIfEmpty(0).Max();
        double desiredWidth = SelectionColumnWidth + ColumnPaddingWidth +
            ((longestNameLength + longestDistinguishedNameLength) * AverageCharacterWidth);
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
