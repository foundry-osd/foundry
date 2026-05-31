using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public sealed partial class LocalizationConfigurationViewModel : ObservableObject, IDisposable
{
    private const string AutomaticSelectionValue = "";

    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly IApplicationLocalizationService localizationService;
    private bool isApplyingState = true;
    private bool isRefreshingOptions;
    private bool isSavingState;

    public LocalizationConfigurationViewModel(
        IFoundryConfigurationStateService configurationStateService,
        IApplicationLocalizationService localizationService)
    {
        this.configurationStateService = configurationStateService;
        this.localizationService = localizationService;

        RefreshLocalizedText();
        RefreshTimeZones();
        ApplyState(configurationStateService.Current.Localization);

        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
        isApplyingState = false;
    }

    public ObservableCollection<SelectionOption<string>> TimeZoneOptions { get; } = [];

    [ObservableProperty]
    public partial string PageTitle { get; set; }

    [ObservableProperty]
    public partial string TimeZoneHeader { get; set; }

    [ObservableProperty]
    public partial string AutomaticOptionText { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedTimeZone { get; set; }

    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
    }

    partial void OnSelectedTimeZoneChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    private void ApplyState(LocalizationSettings settings)
    {
        isApplyingState = true;
        try
        {
            SelectedTimeZone = SelectStringOption(TimeZoneOptions, settings.DefaultTimeZoneId) ?? TimeZoneOptions[0];
        }
        finally
        {
            isApplyingState = false;
        }
    }

    private void SaveState()
    {
        if (isApplyingState || isRefreshingOptions)
        {
            return;
        }

        string? defaultTimeZoneId = string.IsNullOrWhiteSpace(SelectedTimeZone?.Value)
            ? null
            : SelectedTimeZone.Value.Trim();

        isSavingState = true;
        try
        {
            configurationStateService.UpdateLocalization(new LocalizationSettings
            {
                DefaultTimeZoneId = defaultTimeZoneId
            });
        }
        finally
        {
            isSavingState = false;
        }
    }

    private void RefreshLocalizedText()
    {
        PageTitle = localizationService.GetString("LocalizationPage_Title.Text");
        TimeZoneHeader = localizationService.GetString("Localization.TimeZone.Header");
        AutomaticOptionText = localizationService.GetString("Localization.AutomaticOption");
    }

    private void RefreshTimeZones()
    {
        string? selectedValue = SelectedTimeZone?.Value;

        isRefreshingOptions = true;
        try
        {
            TimeZoneOptions.Clear();
            TimeZoneOptions.Add(new(AutomaticSelectionValue, AutomaticOptionText));
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
            isRefreshingOptions = false;
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RefreshLocalizedText();
        RefreshTimeZones();
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (isSavingState)
        {
            return;
        }

        ApplyState(configurationStateService.Current.Localization);
    }

    private static SelectionOption<string>? SelectStringOption(IEnumerable<SelectionOption<string>> options, string? value)
    {
        return options.FirstOrDefault(option =>
            string.Equals(option.Value, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
