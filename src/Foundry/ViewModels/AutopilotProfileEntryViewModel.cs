// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>
/// Represents an imported Autopilot profile as displayed and persisted by the Foundry UI.
/// </summary>
public sealed partial class AutopilotProfileEntryViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a profile entry.
    /// </summary>
    /// <param name="id">The stable Autopilot profile identifier.</param>
    /// <param name="displayName">The profile display name.</param>
    /// <param name="folderName">The folder-safe profile name used when staging the JSON file.</param>
    /// <param name="source">The import source shown to the user.</param>
    /// <param name="importedAtUtc">The UTC timestamp when the profile was imported.</param>
    /// <param name="jsonContent">The offline Autopilot profile JSON content.</param>
    public AutopilotProfileEntryViewModel(
        string id,
        string displayName,
        string folderName,
        string source,
        DateTimeOffset importedAtUtc,
        string jsonContent)
    {
        Id = id;
        DisplayName = displayName;
        FolderName = folderName;
        Source = source;
        ImportedAtUtc = importedAtUtc;
        JsonContent = jsonContent;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string FolderName { get; }

    public string Source { get; }

    public DateTimeOffset ImportedAtUtc { get; }

    public string JsonContent { get; }

    /// <summary>
    /// Gets the localized import timestamp shown in the profile grid.
    /// </summary>
    public string ImportedAtDisplay => ImportedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// Gets or sets the localized marker shown in the profile grid for the profile staged by default; other rows are empty.
    /// </summary>
    [ObservableProperty]
    public partial string DefaultText { get; set; } = string.Empty;

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
