using Foundry.Models.Configuration;

namespace Foundry.ViewModels;

public sealed record AutopilotProfileEntry(
    string Id,
    string DisplayName,
    string FolderName,
    string JsonContent,
    string Source,
    DateTimeOffset ImportedAtUtc)
{
    public string ImportedAtDisplay => ImportedAtUtc.ToLocalTime().ToString("g");

    public static AutopilotProfileEntry FromSettings(AutopilotProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new AutopilotProfileEntry(
            settings.Id,
            settings.DisplayName,
            settings.FolderName,
            settings.JsonContent,
            settings.Source,
            settings.ImportedAtUtc);
    }

    public AutopilotProfileSettings ToSettings()
    {
        return new AutopilotProfileSettings
        {
            Id = Id,
            DisplayName = DisplayName,
            FolderName = FolderName,
            JsonContent = JsonContent,
            Source = Source,
            ImportedAtUtc = ImportedAtUtc
        };
    }
}
