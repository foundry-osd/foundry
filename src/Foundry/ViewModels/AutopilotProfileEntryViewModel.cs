using System.Globalization;
using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

public sealed record AutopilotProfileEntryViewModel(
    string Id,
    string DisplayName,
    string FolderName,
    string Source,
    DateTimeOffset ImportedAtUtc,
    string JsonContent)
{
    public string ImportedAtDisplay => ImportedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public static AutopilotProfileEntryViewModel FromSettings(AutopilotProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new AutopilotProfileEntryViewModel(
            settings.Id,
            settings.DisplayName,
            settings.FolderName,
            settings.Source,
            settings.ImportedAtUtc,
            settings.JsonContent);
    }

    public AutopilotProfileSettings ToSettings()
    {
        return new AutopilotProfileSettings
        {
            Id = Id,
            DisplayName = DisplayName,
            FolderName = FolderName,
            Source = Source,
            ImportedAtUtc = ImportedAtUtc,
            JsonContent = JsonContent
        };
    }
}
