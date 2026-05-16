using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private bool isRefreshingOobeOptions;

    public ObservableCollection<SelectionOption<OobeDiagnosticDataLevel>> OobeDiagnosticDataOptions { get; } = [];

    public ObservableCollection<SelectionOption<OobeLocationAccessMode>> OobeLocationAccessOptions { get; } = [];

    public bool IsOobeOptionsEnabled => IsOobeEnabled;

    [ObservableProperty]
    public partial string OobeHeader { get; set; }

    [ObservableProperty]
    public partial string OobeDescription { get; set; }

    [ObservableProperty]
    public partial string OobeEnableText { get; set; }

    [ObservableProperty]
    public partial string OobeSkipLicenseTermsLabel { get; set; }

    [ObservableProperty]
    public partial string OobeSkipLicenseTermsDescription { get; set; }

    [ObservableProperty]
    public partial string OobeDiagnosticDataLabel { get; set; }

    [ObservableProperty]
    public partial string OobeDiagnosticDataDescription { get; set; }

    [ObservableProperty]
    public partial string OobeHidePrivacySetupLabel { get; set; }

    [ObservableProperty]
    public partial string OobeHidePrivacySetupDescription { get; set; }

    [ObservableProperty]
    public partial string OobeTailoredExperiencesLabel { get; set; }

    [ObservableProperty]
    public partial string OobeTailoredExperiencesDescription { get; set; }

    [ObservableProperty]
    public partial string OobeAdvertisingIdLabel { get; set; }

    [ObservableProperty]
    public partial string OobeAdvertisingIdDescription { get; set; }

    [ObservableProperty]
    public partial string OobeOnlineSpeechRecognitionLabel { get; set; }

    [ObservableProperty]
    public partial string OobeOnlineSpeechRecognitionDescription { get; set; }

    [ObservableProperty]
    public partial string OobeInkingAndTypingDiagnosticsLabel { get; set; }

    [ObservableProperty]
    public partial string OobeInkingAndTypingDiagnosticsDescription { get; set; }

    [ObservableProperty]
    public partial string OobeLocationAccessLabel { get; set; }

    [ObservableProperty]
    public partial string OobeLocationAccessDescription { get; set; }

    [ObservableProperty]
    public partial bool IsOobeExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOobeOptionsEnabled))]
    public partial bool IsOobeEnabled { get; set; }

    [ObservableProperty]
    public partial bool SkipLicenseTerms { get; set; } = true;

    [ObservableProperty]
    public partial SelectionOption<OobeDiagnosticDataLevel>? SelectedOobeDiagnosticData { get; set; }

    [ObservableProperty]
    public partial bool HidePrivacySetup { get; set; } = true;

    [ObservableProperty]
    public partial bool AllowTailoredExperiences { get; set; }

    [ObservableProperty]
    public partial bool AllowAdvertisingId { get; set; }

    [ObservableProperty]
    public partial bool AllowOnlineSpeechRecognition { get; set; }

    [ObservableProperty]
    public partial bool AllowInkingAndTypingDiagnostics { get; set; }

    [ObservableProperty]
    public partial SelectionOption<OobeLocationAccessMode>? SelectedOobeLocationAccess { get; set; }

    partial void OnIsOobeEnabledChanged(bool value)
    {
        IsOobeExpanded = value;
        SaveState();
    }

    partial void OnSkipLicenseTermsChanged(bool value)
    {
        SaveState();
    }

    partial void OnSelectedOobeDiagnosticDataChanged(SelectionOption<OobeDiagnosticDataLevel>? value)
    {
        if (!isRefreshingOobeOptions)
        {
            SaveState();
        }
    }

    partial void OnHidePrivacySetupChanged(bool value)
    {
        SaveState();
    }

    partial void OnAllowTailoredExperiencesChanged(bool value)
    {
        SaveState();
    }

    partial void OnAllowAdvertisingIdChanged(bool value)
    {
        SaveState();
    }

    partial void OnAllowOnlineSpeechRecognitionChanged(bool value)
    {
        SaveState();
    }

    partial void OnAllowInkingAndTypingDiagnosticsChanged(bool value)
    {
        SaveState();
    }

    partial void OnSelectedOobeLocationAccessChanged(SelectionOption<OobeLocationAccessMode>? value)
    {
        if (!isRefreshingOobeOptions)
        {
            SaveState();
        }
    }

    private void ApplyOobeState(OobeSettings settings)
    {
        IsOobeEnabled = settings.IsEnabled;
        IsOobeExpanded = settings.IsEnabled;
        SkipLicenseTerms = settings.SkipLicenseTerms;
        SelectedOobeDiagnosticData = SelectOption(OobeDiagnosticDataOptions, settings.DiagnosticDataLevel);
        HidePrivacySetup = settings.HidePrivacySetup;
        AllowTailoredExperiences = settings.AllowTailoredExperiences;
        AllowAdvertisingId = settings.AllowAdvertisingId;
        AllowOnlineSpeechRecognition = settings.AllowOnlineSpeechRecognition;
        AllowInkingAndTypingDiagnostics = settings.AllowInkingAndTypingDiagnostics;
        SelectedOobeLocationAccess = SelectOption(OobeLocationAccessOptions, settings.LocationAccess);
    }

    private OobeSettings BuildOobeSettings()
    {
        return IsOobeEnabled
            ? new OobeSettings
            {
                IsEnabled = true,
                SkipLicenseTerms = SkipLicenseTerms,
                DiagnosticDataLevel = SelectedOobeDiagnosticData?.Value ?? OobeDiagnosticDataLevel.Required,
                HidePrivacySetup = HidePrivacySetup,
                AllowTailoredExperiences = AllowTailoredExperiences,
                AllowAdvertisingId = AllowAdvertisingId,
                AllowOnlineSpeechRecognition = AllowOnlineSpeechRecognition,
                AllowInkingAndTypingDiagnostics = AllowInkingAndTypingDiagnostics,
                LocationAccess = SelectedOobeLocationAccess?.Value ?? OobeLocationAccessMode.UserControlled
            }
            : new OobeSettings();
    }

    private void RefreshOobeLocalizedText()
    {
        OobeHeader = localizationService.GetString("Customization.OobeHeader");
        OobeDescription = localizationService.GetString("Customization.OobeDescription");
        OobeEnableText = localizationService.GetString("Customization.OobeEnableLabel");
        OobeSkipLicenseTermsLabel = localizationService.GetString("Customization.OobeSkipLicenseTermsLabel");
        OobeSkipLicenseTermsDescription = localizationService.GetString("Customization.OobeSkipLicenseTermsDescription");
        OobeDiagnosticDataLabel = localizationService.GetString("Customization.OobeDiagnosticDataLabel");
        OobeDiagnosticDataDescription = localizationService.GetString("Customization.OobeDiagnosticDataDescription");
        OobeHidePrivacySetupLabel = localizationService.GetString("Customization.OobeHidePrivacySetupLabel");
        OobeHidePrivacySetupDescription = localizationService.GetString("Customization.OobeHidePrivacySetupDescription");
        OobeTailoredExperiencesLabel = localizationService.GetString("Customization.OobeTailoredExperiencesLabel");
        OobeTailoredExperiencesDescription = localizationService.GetString("Customization.OobeTailoredExperiencesDescription");
        OobeAdvertisingIdLabel = localizationService.GetString("Customization.OobeAdvertisingIdLabel");
        OobeAdvertisingIdDescription = localizationService.GetString("Customization.OobeAdvertisingIdDescription");
        OobeOnlineSpeechRecognitionLabel = localizationService.GetString("Customization.OobeOnlineSpeechRecognitionLabel");
        OobeOnlineSpeechRecognitionDescription = localizationService.GetString("Customization.OobeOnlineSpeechRecognitionDescription");
        OobeInkingAndTypingDiagnosticsLabel = localizationService.GetString("Customization.OobeInkingAndTypingDiagnosticsLabel");
        OobeInkingAndTypingDiagnosticsDescription = localizationService.GetString("Customization.OobeInkingAndTypingDiagnosticsDescription");
        OobeLocationAccessLabel = localizationService.GetString("Customization.OobeLocationAccessLabel");
        OobeLocationAccessDescription = localizationService.GetString("Customization.OobeLocationAccessDescription");
        RefreshOobeOptions();
    }

    private void RefreshOobeOptions()
    {
        OobeDiagnosticDataLevel selectedDiagnosticData = SelectedOobeDiagnosticData?.Value ?? OobeDiagnosticDataLevel.Required;
        OobeLocationAccessMode selectedLocationAccess = SelectedOobeLocationAccess?.Value ?? OobeLocationAccessMode.UserControlled;

        isRefreshingOobeOptions = true;
        try
        {
            OobeDiagnosticDataOptions.Clear();
            OobeDiagnosticDataOptions.Add(new(OobeDiagnosticDataLevel.Required, localizationService.GetString("Customization.OobeDiagnosticDataRequired")));
            OobeDiagnosticDataOptions.Add(new(OobeDiagnosticDataLevel.Optional, localizationService.GetString("Customization.OobeDiagnosticDataOptional")));
            OobeDiagnosticDataOptions.Add(new(OobeDiagnosticDataLevel.Off, localizationService.GetString("Customization.OobeDiagnosticDataOff")));
            SelectedOobeDiagnosticData = SelectOption(OobeDiagnosticDataOptions, selectedDiagnosticData) ?? OobeDiagnosticDataOptions[0];

            OobeLocationAccessOptions.Clear();
            OobeLocationAccessOptions.Add(new(OobeLocationAccessMode.UserControlled, localizationService.GetString("Customization.OobeLocationUserControlled")));
            OobeLocationAccessOptions.Add(new(OobeLocationAccessMode.ForceOff, localizationService.GetString("Customization.OobeLocationForceOff")));
            SelectedOobeLocationAccess = SelectOption(OobeLocationAccessOptions, selectedLocationAccess) ?? OobeLocationAccessOptions[0];
        }
        finally
        {
            isRefreshingOobeOptions = false;
        }
    }

    private static SelectionOption<T>? SelectOption<T>(IEnumerable<SelectionOption<T>> options, T value)
        where T : struct, Enum
    {
        return options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }
}
