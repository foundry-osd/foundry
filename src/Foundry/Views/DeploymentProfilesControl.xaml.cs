// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Foundry.Core.Services.Profiles;
using Foundry.Core.Services.Application;
using Foundry.Services.Application;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Foundry.Views;

/// <summary>Hosts native profile dialogs and clears password controls after each interaction.</summary>
public sealed partial class DeploymentProfilesControl : UserControl
{
    private readonly IApplicationLocalizationService localization = App.GetService<IApplicationLocalizationService>();
    private readonly IFilePickerService picker = App.GetService<IFilePickerService>();
    private long synchronizationTextCallbackToken;
    private string lastAnnouncedSynchronizationStatus = string.Empty;
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
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Attach();
        lastAnnouncedSynchronizationStatus = SynchronizationStatusText.Text;
        synchronizationTextCallbackToken = SynchronizationStatusText.RegisterPropertyChangedCallback(TextBlock.TextProperty, OnSynchronizationStatusTextChanged);
        localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localization.LanguageChanged -= OnLanguageChanged;
        ViewModel.Detach();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SynchronizationStatusText.UnregisterPropertyChangedCallback(TextBlock.TextProperty, synchronizationTextCallbackToken);
        ViewModel.ShowDialogAsync = null;
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) UpdateLanguage();
        else DispatcherQueue.TryEnqueue(UpdateLanguage);
    }

    private void UpdateLanguage() { ViewModel.Refresh(); Bindings.Update(); }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ViewModel.SynchronizationVisualState))
            VisualStateManager.GoToState(this, ViewModel.SynchronizationVisualState, false);
    }

    private void OnSynchronizationStatusTextChanged(DependencyObject sender, DependencyProperty property)
    {
        string status = SynchronizationStatusText.Text;
        if (!ViewModel.AnnounceSynchronizationStatus || string.IsNullOrEmpty(status) || status == lastAnnouncedSynchronizationStatus) return;
        lastAnnouncedSynchronizationStatus = status;
        FrameworkElementAutomationPeer.FromElement(SynchronizationStatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private async void AutomaticSyncToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || !toggle.IsLoaded || !ViewModel.CanInteract || toggle.IsOn == ViewModel.SyncEnabled) return;
        await ViewModel.ToggleSyncCommand.ExecuteAsync(null);
        toggle.IsOn = ViewModel.SyncEnabled;
    }

    private async void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.AddedItems.FirstOrDefault() is not LocalProfileDescriptor selected) return;
        await ViewModel.SelectProfileAsync(selected);
        ProfileSelector.SelectedItem = ViewModel.SelectedProfile;
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
        var content = new StackPanel { Spacing = 16 };
        if (request.Message is not null) content.Children.Add(new TextBlock { Text = request.Message, TextWrapping = TextWrapping.Wrap });
        var name = new TextBox { Header = T("Profiles.Name"), Text = request.Name ?? string.Empty, MaxLength = 120 };
        var password = new PasswordBox { Header = T(request.NamedSharedFolder || request.ConnectionAccess ? "Profiles.ConnectionPassword" : "Profiles.Passphrase"), MaxLength = 1024, PasswordRevealMode = PasswordRevealMode.Hidden };
        var confirmation = new PasswordBox { Header = T(request.NamedSharedFolder ? "Profiles.ConfirmConnectionPassword" : "Profiles.ConfirmPassphrase"), MaxLength = 1024, PasswordRevealMode = PasswordRevealMode.Hidden };
        var include = new CheckBox { Content = T("Profiles.IncludeSecrets"), IsChecked = request.IncludeSecrets };
        var remember = new CheckBox { Content = T("Profiles.Remember"), IsChecked = request.Remember };
        var sharedKey = new CheckBox { Content = T("Profiles.RememberKey"), IsChecked = request.RememberKey };
        var deleteShared = new CheckBox { Content = T("Profiles.DeleteShared"), IsChecked = false };
        var joinShared = new RadioButton { GroupName = "SynchronizationSetup", IsChecked = !ViewModel.HasActive };
        var share = new TextBox
        {
            Header = T("Profiles.SharedFolder"),
            Text = request.SharePath ?? string.Empty,
            PlaceholderText = request.NamedSharedFolder ? @"\\server\share" : @"\\server\share\Foundry\Configuration",
            MaxLength = 1024,
            FlowDirection = FlowDirection.LeftToRight
        };
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
        if (request.SharePathOption)
        {
            var folderRow = new Grid
            {
                ColumnSpacing = (double)Application.Current.Resources["FoundryInlineControlSpacing"],
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };
            var browse = new Button { Content = T("Common.Browse"), VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetColumn(browse, 1);
            folderRow.Children.Add(share);
            folderRow.Children.Add(browse);
            var folderContent = new StackPanel { Spacing = 4 };
            folderContent.Children.Add(folderRow);
            content.Children.Add(folderContent);
            if (request.NamedSharedFolder)
            {
                var destination = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FlowDirection = FlowDirection.LeftToRight,
                    IsTextSelectionEnabled = true,
                    Style = (Style)Application.Current.Resources["FoundryCaptionTextBlockStyle"]
                };
                folderContent.Children.Add(destination);
                void UpdateDestination()
                {
                    try { destination.Text = SharedProfileLocation.Resolve(share.Text.Trim(), name.Text); }
                    catch (ArgumentException) { destination.Text = string.Empty; }
                    destination.Visibility = string.IsNullOrEmpty(destination.Text) ? Visibility.Collapsed : Visibility.Visible;
                }
                name.TextChanged += (_, _) => UpdateDestination();
                share.TextChanged += (_, _) => UpdateDestination();
                UpdateDestination();
            }
            browse.Click += async (_, _) =>
            {
                try
                {
                    string? path = await picker.PickFolderAsync(new(T("Profiles.SharedFolder")));
                    if (path is not null) share.Text = path;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
                    InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    Serilog.Log.ForContext<DeploymentProfilesControl>().Warning(exception, "Shared folder selection failed.");
                    share.PlaceholderText = T("Profiles.Failed");
                }
            };
            if (!request.NamedSharedFolder)
                content.Children.Add(new TextBlock { Text = T("Profiles.SmbWarning"), TextWrapping = TextWrapping.Wrap });
        }
        var validation = new TextBlock { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
        if (request.Passphrase)
        {
            var passwordContent = new StackPanel { Spacing = 4 };
            passwordContent.Children.Add(password);
            if (request.NamedSharedFolder)
                passwordContent.Children.Add(new TextBlock
                {
                    Text = T("Profiles.ConnectionPasswordHint"),
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["FoundryCaptionTextBlockStyle"]
                });
            if (!request.ConfirmPassphrase) passwordContent.Children.Add(validation);
            content.Children.Add(passwordContent);
        }
        if (request.ConfirmPassphrase)
        {
            var confirmationContent = new StackPanel { Spacing = 4 };
            confirmationContent.Children.Add(confirmation);
            confirmationContent.Children.Add(validation);
            content.Children.Add(confirmationContent);
        }
        var options = new StackPanel { Spacing = 8 };
        if (request.IncludeSecretsOption)
        {
            var inclusionContent = new StackPanel { Spacing = 4 };
            inclusionContent.Children.Add(include);
            if (request.NamedSharedFolder)
            {
                var warning = new TextBlock
                {
                    Text = T("Profiles.IncludedSecretsWarning"),
                    TextWrapping = TextWrapping.Wrap,
                    Visibility = include.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
                    Style = (Style)Application.Current.Resources["FoundryCaptionTextBlockStyle"]
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(warning, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
                include.Checked += (_, _) => warning.Visibility = Visibility.Visible;
                include.Unchecked += (_, _) => warning.Visibility = Visibility.Collapsed;
                inclusionContent.Children.Add(warning);
            }
            options.Children.Add(inclusionContent);
        }
        if (request.RememberOption) options.Children.Add(remember);
        if (request.SharedKeyOption) options.Children.Add(sharedKey);
        if (request.DeleteSharedOption) options.Children.Add(deleteShared);
        if (options.Children.Count > 0) content.Children.Add(options);
        if (!request.NamedSharedFolder && (request.RememberOption || request.SharedKeyOption))
            content.Children.Add(new TextBlock { Text = T("Profiles.LocalPrivacy"), TextWrapping = TextWrapping.Wrap });
        if (request.NamedSharedFolder)
            content.Children.Add(new HyperlinkButton
            {
                Content = T("Profiles.LearnMore"),
                NavigateUri = new Uri(FoundryApplicationInfo.DeploymentProfilesDocumentationUrl + "#create-a-shared-profile"),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left
            });

        if (!request.Passphrase) content.Children.Add(validation);
        var scroll = new ScrollViewer { Content = content, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var dialog = new ContentDialog
        {
            Style = ContentDialogStyleProvider.DefaultStyle,
            XamlRoot = XamlRoot,
            Title = T(request.Title),
            PrimaryButtonText = T(request.PrimaryButtonKey),
            CloseButtonText = T(request.CloseButtonKey),
            DefaultButton = request.PreferCancel ? ContentDialogButton.Close : ContentDialogButton.Primary,
            Content = scroll
        };
        XamlRoot root = XamlRoot;
        void UpdateSize()
        {
            double width = Math.Min((double)Application.Current.Resources["FoundryDialogMinWidth"], Math.Max(0, root.Size.Width - 48));
            dialog.Resources["ContentDialogMinWidth"] = width;
            dialog.Resources["ContentDialogMaxWidth"] = width;
            dialog.Resources["ContentDialogMaxHeight"] = Math.Max(0, root.Size.Height - 48);
            scroll.MaxHeight = Math.Min(500, Math.Max(0, root.Size.Height - 200));
        }
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateSize();
        string ValidationMessage()
        {
            if (request.Name is not null && string.IsNullOrWhiteSpace(name.Text) ||
                request.Passphrase && string.IsNullOrEmpty(password.Password) ||
                request.SharePathOption && !share.Text.Trim().StartsWith(@"\\", StringComparison.Ordinal))
                return T("Profiles.Validation");
            if (request.NamedSharedFolder && !SharedProfileLocation.IsValidName(name.Text))
                return T("Profiles.InvalidFolderName");
            if (request.ConfirmPassphrase && !string.Equals(password.Password, confirmation.Password, StringComparison.Ordinal))
                return T("Profiles.PasswordMismatch");
            return string.Empty;
        }
        void ShowValidation(string message)
        {
            validation.Text = message;
            validation.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
        bool attempted = false;
        void UpdateValidation()
        {
            string message = ValidationMessage();
            dialog.IsPrimaryButtonEnabled = !request.Passphrase || password.Password.Length > 0 &&
                (!request.ConfirmPassphrase || string.Equals(password.Password, confirmation.Password, StringComparison.Ordinal));
            bool mismatch = request.ConfirmPassphrase && confirmation.Password.Length > 0 &&
                !string.Equals(password.Password, confirmation.Password, StringComparison.Ordinal);
            ShowValidation(attempted ? message : mismatch ? T("Profiles.PasswordMismatch") : string.Empty);
        }
        name.TextChanged += (_, _) => UpdateValidation();
        share.TextChanged += (_, _) => UpdateValidation();
        password.PasswordChanged += (_, _) => UpdateValidation();
        confirmation.PasswordChanged += (_, _) => UpdateValidation();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            attempted = true;
            string message = ValidationMessage();
            args.Cancel = !string.IsNullOrEmpty(message);
            ShowValidation(message);
        };
        UpdateSize();
        UpdateValidation();
        root.Changed += OnRootChanged;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            return new(name.Text, password.Password, include.IsChecked == true, remember.IsChecked == true,
                sharedKey.IsChecked == true, share.Text, deleteShared.IsChecked == true, joinShared.IsChecked == true);
        }
        finally
        {
            root.Changed -= OnRootChanged;
            password.Password = string.Empty;
            confirmation.Password = string.Empty;
        }
    }
}
