using Foundry.Core.Models.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>
/// Backs the OS Recovery page with the current authoring toggle and explanatory text.
/// </summary>
public sealed partial class OsRecoveryConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationLocalizationService localizationService;
    private readonly IFoundryConfigurationStateService configurationStateService;
    private bool isUpdatingFromState;

    public OsRecoveryConfigurationViewModel(
        IApplicationLocalizationService localizationService,
        IFoundryConfigurationStateService configurationStateService)
    {
        this.localizationService = localizationService;
        this.configurationStateService = configurationStateService;
        RefreshLocalizedText();
        RefreshConfigurationState();
        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
    }

    [ObservableProperty]
    public partial string PageTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PageDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EnableHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EnableDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EnableToggleText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WhyHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WhyDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WhenHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WhenDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WinRePathHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WinRePathDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LimitationsHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LimitationsDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FlowHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FlowDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FlowImageAutomationName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOsRecoveryEnabled { get; set; }

    public string DocumentationUrl => string.Empty;

    /// <inheritdoc />
    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RefreshLocalizedText();
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        RefreshConfigurationState();
    }

    partial void OnIsOsRecoveryEnabledChanged(bool value)
    {
        if (isUpdatingFromState)
        {
            return;
        }

        configurationStateService.UpdateOsRecovery(new OsRecoverySettings
        {
            IsEnabled = value
        });
    }

    private void RefreshConfigurationState()
    {
        isUpdatingFromState = true;
        IsOsRecoveryEnabled = configurationStateService.Current.OsRecovery.IsEnabled;
        isUpdatingFromState = false;
    }

    private void RefreshLocalizedText()
    {
        PageTitle = localizationService.GetString("OsRecoveryPage_Title.Text");
        PageDescription = localizationService.GetString("OsRecovery.PageDescription");
        EnableHeader = localizationService.GetString("OsRecovery.EnableHeader");
        EnableDescription = localizationService.GetString("OsRecovery.EnableDescription");
        EnableToggleText = localizationService.GetString("OsRecovery.EnableToggleText");
        WhyHeader = localizationService.GetString("OsRecovery.WhyHeader");
        WhyDescription = localizationService.GetString("OsRecovery.WhyDescription");
        WhenHeader = localizationService.GetString("OsRecovery.WhenHeader");
        WhenDescription = localizationService.GetString("OsRecovery.WhenDescription");
        WinRePathHeader = localizationService.GetString("OsRecovery.WinRePathHeader");
        WinRePathDescription = localizationService.GetString("OsRecovery.WinRePathDescription");
        LimitationsHeader = localizationService.GetString("OsRecovery.LimitationsHeader");
        LimitationsDescription = localizationService.GetString("OsRecovery.LimitationsDescription");
        FlowHeader = localizationService.GetString("OsRecovery.FlowHeader");
        FlowDescription = localizationService.GetString("OsRecovery.FlowDescription");
        FlowImageAutomationName = localizationService.GetString("OsRecovery.FlowImageAutomationName");
    }
}
