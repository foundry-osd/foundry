using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public partial class CustomizationSettingsViewModel : LocalizedViewModelBase
{
    public CustomizationSettingsViewModel(ILocalizationService localizationService)
        : base(localizationService)
    {
    }

    [ObservableProperty]
    public partial bool IsMachineNamingEnabled { get; set; }

    [ObservableProperty]
    public partial string MachineNamePrefix { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool MachineNameAutoGenerate { get; set; }

    [ObservableProperty]
    public partial bool AllowManualSuffixEdit { get; set; } = true;

    public CustomizationSettings BuildSettings()
    {
        return new CustomizationSettings
        {
            MachineNaming = new MachineNamingSettings
            {
                IsEnabled = IsMachineNamingEnabled,
                Prefix = IsMachineNamingEnabled && !string.IsNullOrWhiteSpace(MachineNamePrefix)
                    ? MachineNamePrefix.Trim()
                    : null,
                AutoGenerateName = IsMachineNamingEnabled && MachineNameAutoGenerate,
                AllowManualSuffixEdit = !IsMachineNamingEnabled || AllowManualSuffixEdit
            }
        };
    }

    public void ApplySettings(CustomizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IsMachineNamingEnabled = settings.MachineNaming.IsEnabled;
        MachineNamePrefix = settings.MachineNaming.Prefix ?? string.Empty;
        MachineNameAutoGenerate = settings.MachineNaming.AutoGenerateName;
        AllowManualSuffixEdit = settings.MachineNaming.AllowManualSuffixEdit;
    }
}
