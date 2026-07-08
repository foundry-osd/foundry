// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
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
        if (sender is FrameworkElement { Tag: PowerShellGalleryModule module })
        {
            ViewModel.AddGalleryModule(module);
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
        if (sender is FrameworkElement { Tag: string folder })
        {
            ViewModel.RemoveRootFolder(folder);
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

        BootBehaviorCard.Header = localizationService.GetString("BootImage.BootBehavior.Header");
        BootBehaviorCard.Description = localizationService.GetString("BootImage.BootBehavior.Description");
        FirewallToggle.OnContent = localizationService.GetString("BootImage.Firewall.On");
        FirewallToggle.OffContent = localizationService.GetString("BootImage.Firewall.Off");
        KeepWimCard.Header = localizationService.GetString("BootImage.KeepWim.Header");
        KeepWimCard.Description = localizationService.GetString("BootImage.KeepWim.Description");
        TroubleshootingConsoleCard.Header = localizationService.GetString("BootImage.TroubleshootingConsole.Header");
        TroubleshootingConsoleCard.Description = localizationService.GetString("BootImage.TroubleshootingConsole.Description");

        OptionalComponentsCard.Header = localizationService.GetString("BootImage.OptionalComponents.Header");
        OptionalComponentsCard.Description = localizationService.GetString("BootImage.OptionalComponents.Description");

        PowerShell7Card.Header = localizationService.GetString("BootImage.PowerShell7.Header");
        PowerShell7Card.Description = localizationService.GetString("BootImage.PowerShell7.Description");
        PowerShell7VersionCard.Header = localizationService.GetString("BootImage.PowerShell7.VersionHeader");

        ModulesCard.Header = localizationService.GetString("BootImage.Modules.Header");
        ModulesCard.Description = localizationService.GetString("BootImage.Modules.Description");
        ModuleSearchBox.PlaceholderText = localizationService.GetString("BootImage.Modules.SearchPlaceholder");
        SearchModulesButton.Content = localizationService.GetString("BootImage.Modules.SearchButton");
        AddLocalModuleButton.Content = localizationService.GetString("BootImage.Modules.AddLocalButton");

        RootFoldersCard.Header = localizationService.GetString("BootImage.RootFolders.Header");
        RootFoldersCard.Description = localizationService.GetString("BootImage.RootFolders.Description");
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
