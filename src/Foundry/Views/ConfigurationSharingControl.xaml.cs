// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>Settings card for configuration sharing; shows the export and import dialogs the view model requests.</summary>
public sealed partial class ConfigurationSharingControl : UserControl
{
    private readonly IApplicationLocalizationService localization = App.GetService<IApplicationLocalizationService>();

    public ConfigurationSharingViewModel ViewModel { get; } = App.GetService<ConfigurationSharingViewModel>();

    public ConfigurationSharingControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowExportDialogAsync = ShowExportDialogAsync;
        ViewModel.ShowImportDialogAsync = ShowImportDialogAsync;
        localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localization.LanguageChanged -= OnLanguageChanged;
        ViewModel.ShowExportDialogAsync = null;
        ViewModel.ShowImportDialogAsync = null;
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) Bindings.Update();
        else DispatcherQueue.TryEnqueue(Bindings.Update);
    }

    private async Task ShowExportDialogAsync(ExportConfigurationDialogViewModel dialogViewModel)
    {
        var dialog = new ExportConfigurationDialog(dialogViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async Task ShowImportDialogAsync(ImportConfigurationDialogViewModel dialogViewModel)
    {
        var dialog = new ImportConfigurationDialog(dialogViewModel) { XamlRoot = XamlRoot };
        Task initialization = dialogViewModel.InitializeAsync();
        await dialog.ShowAsync();
        await initialization;
    }
}
