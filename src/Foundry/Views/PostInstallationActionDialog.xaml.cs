// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class PostInstallationActionDialog : ContentDialog
{
    private const double WindowMargin = 48;
    private const double DialogChromeWidth = 48;
    private const double DialogChromeHeight = 160;
    private readonly XamlRoot hostRoot;

    public PostInstallationActionEditorViewModel ViewModel { get; }

    public PostInstallationActionDialog(PostInstallationActionEditorViewModel viewModel, XamlRoot xamlRoot)
    {
        ViewModel = viewModel;
        hostRoot = xamlRoot;
        XamlRoot = xamlRoot;
        InitializeComponent();
        ApplyContentLayout();
        Loaded += OnLoaded;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) =>
        args.Cancel = !ViewModel.TryBuild(out _);

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) => hostRoot.Changed += OnRootChanged;

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyContentLayout();

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        hostRoot.Changed -= OnRootChanged;
        Loaded -= OnLoaded;
        Opened -= OnOpened;
        Closed -= OnClosed;
    }

    /// <summary>Sizes in effective pixels and stacks the form when two readable columns no longer fit.</summary>
    private void ApplyContentLayout()
    {
        double preferredWidth = Resource(ViewModel.IsExecutable ? "FoundryDialogMaxWidth" : "FoundryWideInputMinWidth");
        double contentWidth = Math.Min(preferredWidth, Math.Max(0, hostRoot.Size.Width - WindowMargin - DialogChromeWidth));
        double dialogHeight = Math.Min(Resource("FoundryLargeDialogMaxHeight"), Math.Max(0, hostRoot.Size.Height - WindowMargin));
        bool twoColumns = ViewModel.IsExecutable && contentWidth >= Resource("FoundryLargeDialogMinWidth");

        Resources["ContentDialogMinWidth"] = contentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxWidth"] = contentWidth + DialogChromeWidth;
        Resources["ContentDialogMaxHeight"] = dialogHeight;
        DialogContentRoot.Width = contentWidth;
        DialogContentRoot.MaxHeight = Math.Max(0, dialogHeight - DialogChromeHeight);
        ExecutionColumn.Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SettingsGrid.ColumnSpacing = twoColumns ? Resource("FoundrySpace24") : 0;
        SettingsGrid.RowSpacing = twoColumns ? 0 : Resource("FoundrySpace24");
        Grid.SetColumn(ExecutionSettingsPanel, twoColumns ? 1 : 0);
        Grid.SetRow(ExecutionSettingsPanel, twoColumns ? 0 : 1);
    }

    private static double Resource(string key) => (double)Application.Current.Resources[key];
}
