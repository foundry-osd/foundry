// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Services.Localization;

namespace Foundry.Views;

/// <summary>Hosts native profile dialogs and clears password controls after each interaction.</summary>
public sealed partial class DeploymentProfilesControl : UserControl
{
    private readonly IApplicationLocalizationService localization = App.GetService<IApplicationLocalizationService>();
    private readonly IFilePickerService picker = App.GetService<IFilePickerService>();
    public DeploymentProfilesViewModel ViewModel { get; } = App.GetService<DeploymentProfilesViewModel>();

    public DeploymentProfilesControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowDialogAsync = ShowDialogAsync;
        ViewModel.Attach();
        localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localization.LanguageChanged -= OnLanguageChanged;
        ViewModel.Detach();
        ViewModel.ShowDialogAsync = null;
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) UpdateLanguage();
        else DispatcherQueue.TryEnqueue(UpdateLanguage);
    }

    private void UpdateLanguage() { ViewModel.Refresh(); Bindings.Update(); }

    private async void AutomaticSyncToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || !toggle.IsLoaded || !ViewModel.CanInteract || toggle.IsOn == ViewModel.SyncEnabled) return;
        await ViewModel.ToggleSyncCommand.ExecuteAsync(null);
        toggle.IsOn = ViewModel.SyncEnabled;
    }

    private async void RememberPasswordsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || !toggle.IsLoaded || !ViewModel.CanInteract || toggle.IsOn == ViewModel.RememberSecrets) return;
        await ViewModel.ToggleRememberCommand.ExecuteAsync(null);
        toggle.IsOn = ViewModel.RememberSecrets;
    }

    private async Task<ProfileDialogResponse?> ShowDialogAsync(ProfileDialogRequest request)
    {
        string T(string key) => localization.GetString(key);
        var content = new StackPanel { Spacing = 12, MinWidth = 280 };
        if (request.Message is not null) content.Children.Add(new TextBlock { Text = request.Message, TextWrapping = TextWrapping.Wrap });
        var name = new TextBox { Header = T("Profiles.Name"), Text = request.Name ?? string.Empty, MaxLength = 120 };
        var password = new PasswordBox { Header = T("Profiles.Passphrase"), MaxLength = 1024, PasswordRevealMode = PasswordRevealMode.Hidden };
        var confirmation = new PasswordBox { Header = T("Profiles.ConfirmPassphrase"), MaxLength = 1024, PasswordRevealMode = PasswordRevealMode.Hidden };
        var include = new CheckBox { Content = T("Profiles.IncludeSecrets"), IsChecked = false };
        var remember = new CheckBox { Content = T("Profiles.Remember"), IsChecked = false };
        var sharedKey = new CheckBox { Content = T("Profiles.RememberKey"), IsChecked = false };
        var deleteShared = new CheckBox { Content = T("Profiles.DeleteShared"), IsChecked = false };
        var joinShared = new RadioButton { GroupName = "SynchronizationSetup", IsChecked = !ViewModel.HasActive };
        var share = new TextBox { Header = T("Profiles.SharedFolder"), PlaceholderText = @"\\server\share\profile", MaxLength = 1024, FlowDirection = FlowDirection.LeftToRight };
        if (request.SynchronizationSetupOption)
        {
            StackPanel ChoiceContent(string heading, string description)
            {
                var choice = new StackPanel { Spacing = 4 };
                choice.Children.Add(new TextBlock { Text = T(heading), TextWrapping = TextWrapping.Wrap });
                choice.Children.Add(new TextBlock
                {
                    Text = T(description),
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["FoundryCaptionTextBlockStyle"]
                });
                return choice;
            }

            var createShared = new RadioButton
            {
                Content = ChoiceContent("Profiles.CreateShared", "Profiles.CreateSharedDescription"),
                GroupName = "SynchronizationSetup",
                IsChecked = ViewModel.HasActive,
                IsEnabled = ViewModel.HasActive
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(createShared, T("Profiles.CreateShared"));
            joinShared.Content = ChoiceContent("Profiles.JoinShared", "Profiles.ConnectDescription");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(joinShared, T("Profiles.JoinShared"));
            content.Children.Add(createShared);
            content.Children.Add(joinShared);
        }
        if (request.Name is not null) content.Children.Add(name);
        if (request.Passphrase) content.Children.Add(password);
        if (request.ConfirmPassphrase) content.Children.Add(confirmation);
        if (request.IncludeSecretsOption) content.Children.Add(include);
        if (request.RememberOption) content.Children.Add(remember);
        if (request.SharedKeyOption) content.Children.Add(sharedKey);
        if (request.RememberOption || request.SharedKeyOption)
            content.Children.Add(new TextBlock { Text = T("Profiles.LocalPrivacy"), TextWrapping = TextWrapping.Wrap });
        if (request.SharePathOption)
        {
            content.Children.Add(share);
            var browse = new Button { Content = T("Common.Browse") };
            browse.Click += async (_, _) =>
            {
                try
                {
                    string? path = await picker.PickFolderAsync(new(T("Profiles.SharedFolder")));
                    if (path is not null) share.Text = path;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
                    InvalidOperationException or System.Runtime.InteropServices.COMException)
                { share.PlaceholderText = T("Profiles.Failed"); }
            };
            content.Children.Add(browse);
            content.Children.Add(new TextBlock { Text = T("Profiles.SmbWarning"), TextWrapping = TextWrapping.Wrap });
        }
        if (request.DeleteSharedOption) content.Children.Add(deleteShared);
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
        content.Children.Add(validation);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = T(request.Title),
            PrimaryButtonText = T("Profiles.Continue"),
            CloseButtonText = T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { Content = content, MaxHeight = 500 }
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            bool invalid = request.Name is not null && string.IsNullOrWhiteSpace(name.Text) ||
                request.Passphrase && string.IsNullOrEmpty(password.Password) ||
                request.ConfirmPassphrase && password.Password != confirmation.Password ||
                request.SharePathOption && !share.Text.Trim().StartsWith(@"\\", StringComparison.Ordinal);
            args.Cancel = invalid;
            validation.Text = invalid ? T("Profiles.Validation") : string.Empty;
        };
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            return new(name.Text, password.Password, include.IsChecked == true, remember.IsChecked == true,
                sharedKey.IsChecked == true, share.Text, deleteShared.IsChecked == true, joinShared.IsChecked == true);
        }
        finally { password.Password = string.Empty; confirmation.Password = string.Empty; }
    }
}
