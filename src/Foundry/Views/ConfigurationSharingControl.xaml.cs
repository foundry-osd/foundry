// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>Settings card for configuration sharing; shows the dialogs the view model requests.</summary>
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
        ViewModel.ShowDialogAsync = ShowDialogAsync;
        ViewModel.ConfirmStopFollowingAsync = ConfirmStopFollowingAsync;
        ViewModel.Attach();
        localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localization.LanguageChanged -= OnLanguageChanged;
        ViewModel.Detach();
        ViewModel.ShowDialogAsync = null;
        ViewModel.ConfirmStopFollowingAsync = null;
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) Refresh();
        else DispatcherQueue.TryEnqueue(Refresh);
    }

    private void Refresh()
    {
        ViewModel.RefreshTexts();
        Bindings.Update();
    }

    private async Task ShowDialogAsync(ObservableObject dialogViewModel)
    {
        ContentDialog dialog = dialogViewModel switch
        {
            ExportConfigurationDialogViewModel export => new ExportConfigurationDialog(export),
            ImportConfigurationDialogViewModel import => new ImportConfigurationDialog(import),
            PublishDialogViewModel publish => new PublishDialog(publish),
            FollowDialogViewModel follow => new FollowDialog(follow),
            TakeOverDialogViewModel takeOver => new TakeOverDialog(takeOver),
            AcceptPublisherDialogViewModel accept => new AcceptPublisherDialog(accept),
            ChangePublicationPasswordDialogViewModel change => new ChangePublicationPasswordDialog(change),
            EnterPublicationPasswordDialogViewModel enter => new EnterPublicationPasswordDialog(enter),
            _ => throw new ArgumentOutOfRangeException(nameof(dialogViewModel), dialogViewModel.GetType().Name, "No dialog shows this view model.")
        };
        dialog.XamlRoot = XamlRoot;
        Task initialization = dialogViewModel is ImportConfigurationDialogViewModel importing ? importing.InitializeAsync() : Task.CompletedTask;
        await dialog.ShowAsync();
        await initialization;
    }

    /// <summary>Asks to confirm "Stop following"; null when cancelled, otherwise whether the publication password is forgotten.</summary>
    private async Task<bool?> ConfirmStopFollowingAsync()
    {
        var dialog = new StopFollowingDialog(ViewModel, localization.GetString("Common.Cancel")) { XamlRoot = XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.ForgetPassword : null;
    }
}
