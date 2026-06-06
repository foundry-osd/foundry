using System.Collections.ObjectModel;
using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml;
using Serilog;

namespace Foundry.ViewModels;

/// <summary>
/// Backs the general media settings page and persists WinPE architecture, language, and driver-source choices.
/// </summary>
public sealed partial class GeneralConfigurationViewModel : ObservableObject, IDisposable
{
    private const string AutomaticSelectionValue = "";

    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly IAdkService adkService;
    private readonly IWinPeLanguageDiscoveryService winPeLanguageDiscoveryService;
    private readonly IFilePickerService filePickerService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isInitializing = true;
    private bool isApplyingTimeZoneState;
    private bool isRefreshingTimeZoneOptions;
    private bool isSavingLocalizationState;

    public GeneralConfigurationViewModel(
        IFoundryConfigurationStateService configurationStateService,
        IAdkService adkService,
        IWinPeLanguageDiscoveryService winPeLanguageDiscoveryService,
        IFilePickerService filePickerService,
        IApplicationLocalizationService localizationService,
        ILogger logger)
    {
        this.configurationStateService = configurationStateService;
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

        GeneralSettings general = configurationStateService.Current.General;
        SelectedArchitecture = SelectOption(Architectures, general.Architecture);
        UseCa2023Signature = general.UseCa2023;
        IncludeDellDrivers = general.IncludeDellDrivers;
        IncludeHpDrivers = general.IncludeHpDrivers;
        CustomDriverDirectoryPath = general.CustomDriverDirectoryPath ?? string.Empty;
        WinPeLanguageUnavailableDescription = string.Empty;
        RefreshTimeZones();
        ApplyLocalizationState(configurationStateService.Current.Localization);

        configurationStateService.StateChanged += OnConfigurationStateChanged;
        isInitializing = false;
    }

    /// <summary>
    /// Gets the WinPE architecture options supported by the media build workflow.
    /// </summary>
    public ObservableCollection<SelectionOption<WinPeArchitecture>> Architectures { get; }

    /// <summary>
    /// Gets the WinPE language packs discovered from the installed ADK.
    /// </summary>
    public ObservableCollection<string> AvailableWinPeLanguages { get; } = [];

    /// <summary>
    /// Gets the Windows time-zone options available for generated deployment media.
    /// </summary>
    public ObservableCollection<SelectionOption<string>> TimeZoneOptions { get; } = [];

    [ObservableProperty]
    public partial SelectionOption<WinPeArchitecture>? SelectedArchitecture { get; set; }

    [ObservableProperty]
    public partial bool UseCa2023Signature { get; set; }

    [ObservableProperty]
    public partial bool IncludeDellDrivers { get; set; }

    [ObservableProperty]
    public partial bool IncludeHpDrivers { get; set; }

    [ObservableProperty]
    public partial string CustomDriverDirectoryPath { get; set; }

    [ObservableProperty]
    public partial string? SelectedWinPeLanguage { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedTimeZone { get; set; }

    [NotifyPropertyChangedFor(nameof(WinPeLanguageUnavailableVisibility))]
    [ObservableProperty]
    public partial bool HasWinPeLanguages { get; set; }

    [ObservableProperty]
    public partial bool CanCreateMedia { get; set; }

    [ObservableProperty]
    public partial string WinPeLanguageUnavailableDescription { get; set; }

    /// <summary>
    /// Gets whether the WinPE language picker should be hidden because no language packs are available.
    /// </summary>
    public Visibility WinPeLanguageUnavailableVisibility => HasWinPeLanguages ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public void Dispose()
    {
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
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

    /// <summary>
    /// Refreshes whether media creation is currently allowed by the detected ADK state.
    /// </summary>
    public void RefreshAdkState()
    {
        CanCreateMedia = adkService.CurrentStatus.CanCreateMedia;
    }

    /// <summary>
    /// Reloads available WinPE languages from the ADK path matching the selected architecture.
    /// </summary>
    public void RefreshWinPeLanguages()
    {
        RefreshAdkState();
        AvailableWinPeLanguages.Clear();
        HasWinPeLanguages = false;

        if (!CanCreateMedia)
        {
            WinPeLanguageUnavailableDescription = localizationService.GetString("GeneralConfiguration.WinPeLanguage.AdkBlocked");
            SelectedWinPeLanguage = null;
            return;
        }

        WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        if (!toolsResult.IsSuccess || toolsResult.Value is null)
        {
            logger.Warning("WinPE language discovery skipped because ADK tools were not resolved. ErrorCode={ErrorCode}", toolsResult.Error?.Code);
            WinPeLanguageUnavailableDescription = localizationService.GetString("GeneralConfiguration.WinPeLanguage.Unavailable");
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
            logger.Warning(
                "WinPE language discovery returned no languages. Architecture={Architecture}, ErrorCode={ErrorCode}",
                architecture,
                result.Error?.Code);
            WinPeLanguageUnavailableDescription = localizationService.GetString("GeneralConfiguration.WinPeLanguage.Unavailable");
            SelectedWinPeLanguage = null;
            return;
        }

        List<string> languages = result.Value.Select(NormalizeCultureName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string selected = SelectWinPeLanguage(languages, configurationStateService.Current.General.WinPeLanguage, localizationService.CurrentLanguage);
        foreach (string language in languages)
        {
            AvailableWinPeLanguages.Add(language);
        }

        HasWinPeLanguages = true;
        WinPeLanguageUnavailableDescription = string.Empty;
        SelectedWinPeLanguage = selected;
        Save(configurationStateService.Current.General with { WinPeLanguage = selected });
    }

    /// <summary>
    /// Reloads localized Windows time-zone options and keeps the selected value when possible.
    /// </summary>
    public void RefreshTimeZones()
    {
        string? selectedValue = SelectedTimeZone?.Value;

        isRefreshingTimeZoneOptions = true;
        try
        {
            TimeZoneOptions.Clear();
            TimeZoneOptions.Add(new(AutomaticSelectionValue, localizationService.GetString("Common.AutomaticOption")));
            foreach (TimeZoneInfo timeZone in TimeZoneInfo.GetSystemTimeZones()
                         .OrderBy(timeZone => timeZone.BaseUtcOffset)
                         .ThenBy(timeZone => timeZone.DisplayName, StringComparer.CurrentCulture))
            {
                TimeZoneOptions.Add(new(timeZone.Id, $"{timeZone.DisplayName} ({timeZone.Id})"));
            }

            SelectedTimeZone = SelectStringOption(TimeZoneOptions, selectedValue) ?? TimeZoneOptions[0];
        }
        finally
        {
            isRefreshingTimeZoneOptions = false;
        }
    }

    /// <summary>
    /// Persists the selected WinPE language after normalizing it to a culture name.
    /// </summary>
    /// <param name="selectedLanguage">The selected WinPE language.</param>
    public void SetWinPeLanguage(string? selectedLanguage)
    {
        if (string.IsNullOrWhiteSpace(selectedLanguage))
        {
            return;
        }

        string normalizedLanguage = NormalizeCultureName(selectedLanguage);
        Save(configurationStateService.Current.General with { WinPeLanguage = normalizedLanguage });
        SelectedWinPeLanguage = normalizedLanguage;
    }

    partial void OnSelectedArchitectureChanged(SelectionOption<WinPeArchitecture>? value)
    {
        if (value is null || isInitializing)
        {
            return;
        }

        GeneralSettings general = EnsureUsbPartitionStyleAllowedForArchitecture(
            configurationStateService.Current.General with { Architecture = value.Value });
        Save(general);
        RefreshWinPeLanguages();
    }

    partial void OnUseCa2023SignatureChanged(bool value)
    {
        if (isInitializing)
        {
            return;
        }

        Save(configurationStateService.Current.General with { UseCa2023 = value });
    }

    partial void OnIncludeDellDriversChanged(bool value)
    {
        if (isInitializing)
        {
            return;
        }

        Save(configurationStateService.Current.General with { IncludeDellDrivers = value });
    }

    partial void OnIncludeHpDriversChanged(bool value)
    {
        if (isInitializing)
        {
            return;
        }

        Save(configurationStateService.Current.General with { IncludeHpDrivers = value });
    }

    partial void OnCustomDriverDirectoryPathChanged(string value)
    {
        if (isInitializing)
        {
            return;
        }

        Save(configurationStateService.Current.General with
        {
            CustomDriverDirectoryPath = string.IsNullOrWhiteSpace(value) ? null : value
        });
    }

    partial void OnSelectedTimeZoneChanged(SelectionOption<string>? value)
    {
        SaveLocalizationState();
    }

    private void Save(GeneralSettings settings)
    {
        configurationStateService.UpdateGeneral(settings);
    }

    private void ApplyLocalizationState(LocalizationSettings settings)
    {
        isApplyingTimeZoneState = true;
        try
        {
            SelectedTimeZone = SelectStringOption(TimeZoneOptions, settings.DefaultTimeZoneId) ?? TimeZoneOptions[0];
        }
        finally
        {
            isApplyingTimeZoneState = false;
        }
    }

    private void SaveLocalizationState()
    {
        if (isApplyingTimeZoneState || isRefreshingTimeZoneOptions)
        {
            return;
        }

        string? defaultTimeZoneId = string.IsNullOrWhiteSpace(SelectedTimeZone?.Value)
            ? null
            : SelectedTimeZone.Value.Trim();

        isSavingLocalizationState = true;
        try
        {
            configurationStateService.UpdateLocalization(new LocalizationSettings
            {
                DefaultTimeZoneId = defaultTimeZoneId
            });
        }
        finally
        {
            isSavingLocalizationState = false;
        }
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (isSavingLocalizationState)
        {
            return;
        }

        ApplyLocalizationState(configurationStateService.Current.Localization);
    }

    private static GeneralSettings EnsureUsbPartitionStyleAllowedForArchitecture(GeneralSettings settings)
    {
        if (settings.Architecture != WinPeArchitecture.Arm64 || settings.UsbPartitionStyle != UsbPartitionStyle.Mbr)
        {
            return settings;
        }

        return settings with { UsbPartitionStyle = UsbPartitionStyle.Gpt };
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

    private static string NormalizeCultureName(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return string.Empty;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language).Name;
        }
        catch (CultureNotFoundException)
        {
            return language;
        }
    }

    private static SelectionOption<T>? SelectOption<T>(IEnumerable<SelectionOption<T>> options, T value)
    {
        return options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }

    private static SelectionOption<string>? SelectStringOption(IEnumerable<SelectionOption<string>> options, string? value)
    {
        return options.FirstOrDefault(option =>
            string.Equals(option.Value, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
