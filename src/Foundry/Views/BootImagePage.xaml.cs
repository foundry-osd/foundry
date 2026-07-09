// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Localization;
using Foundry.ViewModels;
using Serilog;

namespace Foundry.Views;

public sealed partial class BootImagePage : Page
{
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger = Log.ForContext<BootImagePage>();

    public BootImageConfigurationViewModel ViewModel { get; }

    public BootImagePage()
    {
        localizationService = App.GetService<IApplicationLocalizationService>();
        ViewModel = App.GetService<BootImageConfigurationViewModel>();
        InitializeComponent();
        ApplyLocalizedText();
        localizationService.LanguageChanged += OnLanguageChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private void AddGalleryModuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BootImageModuleSearchResultViewModel result })
        {
            ViewModel.AddGalleryModule(result);
        }
    }

    private async void VersionComboBox_DropDownOpened(object sender, object e)
    {
        if (sender is FrameworkElement { DataContext: BootImageModuleSearchResultViewModel result })
        {
            await result.EnsureVersionsLoadedAsync();
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to open folder in Explorer. Path={Path}", path);
        }
    }

    private void RemoveModuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BootImageModuleViewModel module })
        {
            ViewModel.RemoveModule(module);
        }
    }

    private void RemoveRootFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BootImageAdditionalFolderViewModel folder })
        {
            ViewModel.RemoveRootFolder(folder);
        }
    }

    private void RemoveDriverFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BootImageDriverFolderViewModel folder })
        {
            ViewModel.RemoveDriverFolder(folder);
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyLocalizedText();
            return;
        }

        if (!DispatcherQueue.TryEnqueue(ApplyLocalizedText))
        {
            logger.Warning(
                "Failed to enqueue boot image localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                e.OldLanguage,
                e.NewLanguage);
        }
    }

    private void ApplyLocalizedText()
    {
        ViewModel.RefreshLocalizedText();

        FirewallCard.Header = localizationService.GetString("BootImage.Firewall.Header");
        FirewallCard.Description = localizationService.GetString("BootImage.Firewall.Description");
        FirewallToggle.OnContent = localizationService.GetString("BootImage.Firewall.On");
        FirewallToggle.OffContent = localizationService.GetString("BootImage.Firewall.Off");
        KeepWimCard.Header = localizationService.GetString("BootImage.KeepWim.Header");
        KeepWimCard.Description = localizationService.GetString("BootImage.KeepWim.Description");
        TroubleshootingConsoleCard.Header = localizationService.GetString("BootImage.TroubleshootingConsole.Header");
        TroubleshootingConsoleCard.Description = localizationService.GetString("BootImage.TroubleshootingConsole.Description");

        DriverPacksTitle.Text = localizationService.GetString("BootImage.Drivers.VendorsHeader");
        DriverPacksSubtitle.Text = localizationService.GetString("BootImage.Drivers.VendorsDescription");
        DriverFoldersTitle.Text = localizationService.GetString("BootImage.Drivers.FoldersHeader");
        DriverFoldersSubtitle.Text = localizationService.GetString("BootImage.Drivers.FoldersDescription");
        AddDriverFolderButton.Content = localizationService.GetString("BootImage.Drivers.AddButton");
        ContinueOnDriverErrorCard.Header = localizationService.GetString("BootImage.Drivers.ContinueOnErrorHeader");
        ContinueOnDriverErrorCard.Description = localizationService.GetString("BootImage.Drivers.ContinueOnErrorDescription");

        OptionalComponentsTitle.Text = localizationService.GetString("BootImage.OptionalComponents.Header");
        OptionalComponentsSubtitle.Text = localizationService.GetString("BootImage.OptionalComponents.Description");

        PowerShell7Card.Header = localizationService.GetString("BootImage.PowerShell7.Header");
        PowerShell7Card.Description = localizationService.GetString("BootImage.PowerShell7.Description");
        PowerShell7VersionCard.Header = localizationService.GetString("BootImage.PowerShell7.VersionHeader");
        PowerShell7DownloadCard.Header = localizationService.GetString("BootImage.PowerShell7.DownloadHeader");
        CopyDownloadUrlButtonText.Text = localizationService.GetString("BootImage.PowerShell7.CopyButton");

        GalleryTitle.Text = localizationService.GetString("BootImage.Modules.GalleryHeader");
        ModuleSearchBox.PlaceholderText = localizationService.GetString("BootImage.Modules.SearchPlaceholder");
        SearchModulesButton.Content = localizationService.GetString("BootImage.Modules.SearchButton");
        SelectedGalleryModulesTitle.Text = localizationService.GetString("BootImage.Modules.SelectedGalleryHeader");
        LocalTitle.Text = localizationService.GetString("BootImage.Modules.LocalHeader");
        LocalSubtitle.Text = localizationService.GetString("BootImage.Modules.LocalDescription");
        AddLocalModuleButton.Content = localizationService.GetString("BootImage.Modules.AddLocalButton");
        SelectedLocalModulesTitle.Text = localizationService.GetString("BootImage.Modules.SelectedLocalHeader");

        RootFoldersTitle.Text = localizationService.GetString("BootImage.RootFolders.Header");
        RootFoldersSubtitle.Text = localizationService.GetString("BootImage.RootFolders.Description");
        AddRootFolderButton.Content = localizationService.GetString("BootImage.RootFolders.AddButton");
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }
}
