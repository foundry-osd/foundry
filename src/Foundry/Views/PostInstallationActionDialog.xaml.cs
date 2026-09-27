// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class PostInstallationActionDialog : ContentDialog
{
    private const double WindowMargin = 48;
    private const double DialogChromeWidth = 48;
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

    /// <summary>Keeps the native dialog buttons at their content width, grouped on the right.</summary>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("CommandSpace") is FrameworkElement commands)
            commands.HorizontalAlignment = HorizontalAlignment.Right;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = !ViewModel.TryBuild(out _);
        if (args.Cancel && !string.IsNullOrEmpty(ViewModel.InvalidField))
        {
            if (FindName(ViewModel.InvalidField + "Input") is Control control) control.Focus(FocusState.Programmatic);
            if (FindName(ViewModel.InvalidField + "Group") is FrameworkElement group) group.StartBringIntoView();
        }
    }

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
        double availableWidth = Math.Max(0, hostRoot.Size.Width - WindowMargin - DialogChromeWidth);
        double minimumWidth = Math.Min(Resource(ViewModel.IsExecutable ? "FoundryLargeDialogMinWidth" : "FoundryWideInputMinWidth"), availableWidth);
        double maximumWidth = Math.Min(Resource("FoundryDialogMaxWidth"), availableWidth);
        bool twoColumns = ViewModel.IsExecutable && minimumWidth >= Resource("FoundryLargeDialogMinWidth");

        Resources["ContentDialogMinWidth"] = minimumWidth + DialogChromeWidth;
        Resources["ContentDialogMaxWidth"] = maximumWidth + DialogChromeWidth;
        Resources["ContentDialogMaxHeight"] = Math.Max(0, hostRoot.Size.Height - WindowMargin);
        DialogContentRoot.MinWidth = minimumWidth;
        DialogContentRoot.MaxWidth = maximumWidth;
        ExecutionColumn.Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SettingsGrid.ColumnSpacing = twoColumns ? Resource("FoundrySpace24") : 0;
        SettingsGrid.RowSpacing = twoColumns ? 0 : Resource("FoundrySpace24");
        Grid.SetColumn(ExecutionSettingsPanel, twoColumns ? 1 : 0);
        Grid.SetRow(ExecutionSettingsPanel, twoColumns ? 0 : 1);
        Grid.SetColumn(InstallerTypeInput, twoColumns ? 1 : 0);
        Grid.SetRow(InstallerTypeInput, twoColumns ? 0 : 1);
        InstallerTypeInput.Margin = twoColumns
            ? new Thickness(Resource("FoundrySpace24"), 0, 0, 0)
            : new Thickness(0, Resource("FoundryContentGroupSpacing"), 0, 0);
    }

    private static double Resource(string key) => (double)Application.Current.Resources[key];
}
