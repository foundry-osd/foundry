using System.Globalization;
using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>
/// Represents an imported Autopilot profile as displayed and persisted by the Foundry UI.
/// </summary>
/// <param name="Id">The stable Autopilot profile identifier.</param>
/// <param name="DisplayName">The profile display name.</param>
/// <param name="FolderName">The folder-safe profile name used when staging the JSON file.</param>
/// <param name="Source">The import source shown to the user.</param>
/// <param name="ImportedAtUtc">The UTC timestamp when the profile was imported.</param>
/// <param name="JsonContent">The offline Autopilot profile JSON content.</param>
public sealed record AutopilotProfileEntryViewModel(
    string Id,
    string DisplayName,
    string FolderName,
    string Source,
    DateTimeOffset ImportedAtUtc,
    string JsonContent)
{
    /// <summary>
    /// Gets the localized import timestamp shown in the profile grid.
    /// </summary>
    public string ImportedAtDisplay => ImportedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// Creates a profile entry from persisted configuration settings.
    /// </summary>
    /// <param name="settings">The persisted Autopilot profile settings.</param>
    /// <returns>A profile entry view model.</returns>
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

    /// <summary>
    /// Converts this profile entry back to persisted configuration settings.
    /// </summary>
    /// <returns>The persisted profile settings.</returns>
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
