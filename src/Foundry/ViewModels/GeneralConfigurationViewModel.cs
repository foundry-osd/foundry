using System.Collections.ObjectModel;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Serilog;

namespace Foundry.ViewModels;

public sealed partial class GeneralConfigurationViewModel : ObservableObject
{
    private readonly IAppSettingsService appSettingsService;
    private readonly IAdkService adkService;
    private readonly IWinPeLanguageDiscoveryService winPeLanguageDiscoveryService;
    private readonly IFilePickerService filePickerService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isInitializing = true;

    public GeneralConfigurationViewModel(
        IAppSettingsService appSettingsService,
        IAdkService adkService,
        IWinPeLanguageDiscoveryService winPeLanguageDiscoveryService,
        IFilePickerService filePickerService,
        IApplicationLocalizationService localizationService,
        ILogger logger)
    {
        this.appSettingsService = appSettingsService;
        this.adkService = adkService;
        this.winPeLanguageDiscoveryService = winPeLanguageDiscoveryService;
        this.filePickerService = filePickerService;
        this.localizationService = localizationService;
        this.logger = logger.ForContext<GeneralConfigurationViewModel>();

        Architectures =
        [
            new(WinPeArchitecture.X64, "x64"),
            new(WinPeArchitecture.Arm64, "arm64")
        ];
        PartitionStyles =
        [
            new(UsbPartitionStyle.Gpt, "GPT"),
            new(UsbPartitionStyle.Mbr, "MBR")
        ];

        IsoOutputPath = appSettingsService.Current.Media.IsoOutputPath;
        SelectedArchitecture = SelectOption(Architectures, ParseEnum(appSettingsService.Current.Media.Architecture, WinPeArchitecture.X64));
        UseCa2023Signature = appSettingsService.Current.Media.UseCa2023Signature;
        SelectedPartitionStyle = SelectOption(PartitionStyles, ParseEnum(appSettingsService.Current.Media.UsbPartitionStyle, UsbPartitionStyle.Gpt));
        IncludeDellDrivers = appSettingsService.Current.Media.IncludeDellDrivers;
        IncludeHpDrivers = appSettingsService.Current.Media.IncludeHpDrivers;
        CustomDriverDirectoryPath = appSettingsService.Current.Media.CustomDriverDirectoryPath ?? string.Empty;
        RefreshLocalizedOptions();
        isInitializing = false;
    }

    public ObservableCollection<SelectionOption<WinPeArchitecture>> Architectures { get; }
    public ObservableCollection<SelectionOption<UsbPartitionStyle>> PartitionStyles { get; }
    public ObservableCollection<SelectionOption<UsbFormatMode>> FormatModes { get; } = [];
    public ObservableCollection<string> AvailableWinPeLanguages { get; } = [];

    [ObservableProperty]
    public partial string IsoOutputPath { get; set; }

    [ObservableProperty]
    public partial SelectionOption<WinPeArchitecture>? SelectedArchitecture { get; set; }

    [ObservableProperty]
    public partial bool UseCa2023Signature { get; set; }

    [ObservableProperty]
    public partial SelectionOption<UsbPartitionStyle>? SelectedPartitionStyle { get; set; }

    [ObservableProperty]
    public partial SelectionOption<UsbFormatMode>? SelectedFormatMode { get; set; }

    [ObservableProperty]
    public partial bool IncludeDellDrivers { get; set; }

    [ObservableProperty]
    public partial bool IncludeHpDrivers { get; set; }

    [ObservableProperty]
    public partial string CustomDriverDirectoryPath { get; set; }

    [ObservableProperty]
    public partial string? SelectedWinPeLanguage { get; set; }

    [ObservableProperty]
    public partial bool HasWinPeLanguages { get; set; }

    [ObservableProperty]
    public partial string WinPeLanguageStatus { get; set; } = string.Empty;

    [RelayCommand]
    private async Task BrowseIsoOutputPathAsync()
    {
        string? path = await filePickerService.PickSaveFileAsync(
            new FileSavePickerRequest(
                localizationService.GetString("StartMedia.IsoPicker.Title"),
                "Foundry",
                [new(localizationService.GetString("StartMedia.IsoPicker.Filter"), [".iso"])],
                ".iso"));

        if (!string.IsNullOrWhiteSpace(path))
        {
            IsoOutputPath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseCustomDriverDirectoryAsync()
    {
        string? path = await filePickerService.PickFolderAsync(
            new FolderPickerRequest(localizationService.GetString("StartMedia.CustomDriversPicker.Title")));

        if (!string.IsNullOrWhiteSpace(path))
        {
            CustomDriverDirectoryPath = path;
        }
    }

    [RelayCommand]
    public Task RefreshWinPeLanguagesAsync()
    {
        RefreshWinPeLanguages();
        return Task.CompletedTask;
    }

    public void RefreshLocalizedOptions()
    {
        UsbFormatMode selectedValue = SelectedFormatMode?.Value
            ?? ParseEnum(appSettingsService.Current.Media.UsbFormatMode, UsbFormatMode.Quick);

        FormatModes.Clear();
        FormatModes.Add(new(UsbFormatMode.Quick, localizationService.GetString("StartMedia.FormatMode.Quick")));
        FormatModes.Add(new(UsbFormatMode.Complete, localizationService.GetString("StartMedia.FormatMode.Complete")));
        SelectedFormatMode = SelectOption(FormatModes, selectedValue);
    }

    public void RefreshWinPeLanguages()
    {
        AvailableWinPeLanguages.Clear();
        HasWinPeLanguages = false;

        if (!adkService.CurrentStatus.CanCreateMedia)
        {
            WinPeLanguageStatus = localizationService.GetString("GeneralSetting_WinPeLanguage.NotReady");
            SelectedWinPeLanguage = null;
            return;
        }

        WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        if (!toolsResult.IsSuccess || toolsResult.Value is null)
        {
            WinPeLanguageStatus = toolsResult.Error?.Message ?? localizationService.GetString("GeneralSetting_WinPeLanguage.NotReady");
            logger.Warning("WinPE language discovery skipped because ADK tools were not resolved. ErrorCode={ErrorCode}", toolsResult.Error?.Code);
            SelectedWinPeLanguage = null;
            return;
        }

        WinPeArchitecture architecture = SelectedArchitecture?.Value ?? WinPeArchitecture.X64;
        WinPeResult<IReadOnlyList<string>> result = winPeLanguageDiscoveryService.GetAvailableLanguages(
            new WinPeLanguageDiscoveryOptions
            {
                Architecture = architecture,
                Tools = toolsResult.Value
            });

        if (!result.IsSuccess || result.Value is null || result.Value.Count == 0)
        {
            WinPeLanguageStatus = result.Error?.Message ?? localizationService.GetString("GeneralSetting_WinPeLanguage.Empty");
            logger.Warning(
                "WinPE language discovery returned no languages. Architecture={Architecture}, ErrorCode={ErrorCode}",
                architecture,
                result.Error?.Code);
            SelectedWinPeLanguage = null;
            return;
        }

        string selected = SelectWinPeLanguage(result.Value, appSettingsService.Current.Media.WinPeLanguage, localizationService.CurrentLanguage);
        foreach (string language in result.Value)
        {
            AvailableWinPeLanguages.Add(language);
        }

        HasWinPeLanguages = true;
        WinPeLanguageStatus = string.Format(localizationService.GetString("GeneralSetting_WinPeLanguage.Status"), result.Value.Count);
        SelectedWinPeLanguage = selected;
        appSettingsService.Current.Media.WinPeLanguage = selected;
        appSettingsService.Save();
    }

    public void SetWinPeLanguage(string? selectedLanguage)
    {
        if (string.IsNullOrWhiteSpace(selectedLanguage))
        {
            return;
        }

        appSettingsService.Current.Media.WinPeLanguage = selectedLanguage;
        appSettingsService.Save();
        SelectedWinPeLanguage = selectedLanguage;
    }

    partial void OnIsoOutputPathChanged(string value)
    {
        if (isInitializing)
        {
            return;
        }

        appSettingsService.Current.Media.IsoOutputPath = value;
        Save();
    }

    partial void OnSelectedArchitectureChanged(SelectionOption<WinPeArchitecture>? value)
    {
        if (value is null || isInitializing)
        {
            return;
        }

        appSettingsService.Current.Media.Architecture = value.Value.ToString();
        if (value.Value == WinPeArchitecture.Arm64 && SelectedPartitionStyle?.Value == UsbPartitionStyle.Mbr)
        {
            SelectedPartitionStyle = SelectOption(PartitionStyles, UsbPartitionStyle.Gpt);
        }

        Save();
        RefreshWinPeLanguages();
    }

    partial void OnUseCa2023SignatureChanged(bool value)
    {
        if (isInitializing)
        {
            return;
        }

        appSettingsService.Current.Media.UseCa2023Signature = value;
        Save();
    }

    partial void OnSelectedPartitionStyleChanged(SelectionOption<UsbPartitionStyle>? value)
    {
        if (value is null || isInitializing)
        {
            return;
        }

        if (SelectedArchitecture?.Value == WinPeArchitecture.Arm64 && value.Value == UsbPartitionStyle.Mbr)
        {
            SelectedPartitionStyle = SelectOption(PartitionStyles, UsbPartitionStyle.Gpt);
            return;
        }

        appSettingsService.Current.Media.UsbPartitionStyle = value.Value.ToString();
        Save();
    }

    partial void OnSelectedFormatModeChanged(SelectionOption<UsbFormatMode>? value)
    {
        if (value is null || isInitializing)
        {
            return;
        }

        appSettingsService.Current.Media.UsbFormatMode = value.Value.ToString();
        Save();
    }

    partial void OnIncludeDellDriversChanged(bool value)
    {
        if (isInitializing)
        {
            return;
        }

        appSettingsService.Current.Media.IncludeDellDrivers = value;
        Save();
    }

    partial void OnIncludeHpDriversChanged(bool value)
    {
        if (isInitializing)
        {
            return;
        }

        appSettingsService.Current.Media.IncludeHpDrivers = value;
        Save();
    }

    partial void OnCustomDriverDirectoryPathChanged(string value)
    {
        if (isInitializing)
        {
            return;
        }

        appSettingsService.Current.Media.CustomDriverDirectoryPath = string.IsNullOrWhiteSpace(value) ? null : value;
        Save();
    }

    private void Save()
    {
        appSettingsService.Save();
    }

    private static string SelectWinPeLanguage(IReadOnlyList<string> languages, string? preferredLanguage, string currentLanguage)
    {
        string? exact = languages.FirstOrDefault(language => string.Equals(language, preferredLanguage, StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault(language => string.Equals(language, currentLanguage, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        string currentPrefix = currentLanguage.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        string? family = languages.FirstOrDefault(language => language.StartsWith(currentPrefix + "-", StringComparison.OrdinalIgnoreCase));

        return family ?? languages[0];
    }

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out T result) ? result : fallback;
    }

    private static SelectionOption<T>? SelectOption<T>(IEnumerable<SelectionOption<T>> options, T value)
    {
        return options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }
}
