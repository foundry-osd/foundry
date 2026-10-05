// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;

namespace Foundry.Views;

/// <summary>Lists the organizational units found in the authoring computer's domain so the user can choose which to add.</summary>
public sealed partial class DomainJoinOuSelectionDialog : ContentDialog
{
    private const double DialogChromeWidth = 96;
    private const double FallbackMinimumContentWidth = 560;
    private const double FallbackMaximumContentWidth = 980;
    private const double FallbackMaximumDialogHeight = 760;
    private const double SelectionColumnWidth = 48;
    private const double AverageCharacterWidth = 7.5;
    private const double ListRowHeight = 54;
    private const int MaximumVisibleRows = 8;

    public DomainJoinOuSelectionDialog(DomainJoinOuSelectionDialogViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        Title = ViewModel.Title;
        PrimaryButtonText = ViewModel.AddText;
        CloseButtonText = ViewModel.CancelText;
        IsPrimaryButtonEnabled = ViewModel.HasSelection;
        ApplyContentLayout();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    public DomainJoinOuSelectionDialogViewModel ViewModel { get; }

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

    /// <summary>Sizes the dialog to the longest distinguished name, within the bounds shared by the other pickers.</summary>
    private void ApplyContentLayout()
    {
        double minimumContentWidth = GetApplicationDoubleResource("FoundryDialogMinWidth", FallbackMinimumContentWidth);
        double maximumContentWidth = GetApplicationDoubleResource("FoundryLargeDialogMaxWidth", FallbackMaximumContentWidth);
        double maximumDialogHeight = GetApplicationDoubleResource("FoundryLargeDialogMaxHeight", FallbackMaximumDialogHeight);
        double availableWindowWidth = Math.Max(minimumContentWidth, App.MainWindow.AppWindow.Size.Width * 0.72);
        int longestLength = ViewModel.OrganizationalUnits
            .Select(entry => Math.Max(entry.DisplayName.Length, entry.DistinguishedName.Length))
            .DefaultIfEmpty(0)
            .Max();
        double contentWidth = Math.Clamp(
            SelectionColumnWidth + (longestLength * AverageCharacterWidth),
            minimumContentWidth,
            Math.Min(maximumContentWidth, availableWindowWidth));
        int visibleRows = Math.Clamp(ViewModel.OrganizationalUnits.Count, 1, MaximumVisibleRows);

        Resources["ContentDialogMinWidth"] = minimumContentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxWidth"] = contentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxHeight"] = maximumDialogHeight;
        DialogContentRoot.Width = contentWidth;
        OrganizationalUnitsList.Height = visibleRows * ListRowHeight;
    }

    private static double GetApplicationDoubleResource(string key, double fallback)
    {
        return App.Current.Resources.TryGetValue(key, out object value) && value is double doubleValue
            ? doubleValue
            : fallback;
    }
}
