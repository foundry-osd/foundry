using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Models.Configuration;

namespace Foundry.ViewModels;

public partial class CustomizationSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isMachineNamingEnabled;

    [ObservableProperty]
    private string machineNamePrefix = string.Empty;

    [ObservableProperty]
    private bool machineNameAutoGenerate;

    [ObservableProperty]
    private bool allowManualSuffixEdit = true;

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
