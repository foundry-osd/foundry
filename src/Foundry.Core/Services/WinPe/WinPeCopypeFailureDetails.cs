// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Fixed identifiers for the ADK copype.cmd step that failed. They are exported remotely as
/// <see cref="WinPeDiagnostic.FailureDetail"/>, so every value must stay a bounded constant and never
/// carry script output, paths, or localized text.
/// </summary>
public static class WinPeCopypeFailureDetails
{
    public const string ArchitectureNotFound = "architecture_not_found";
    public const string FirmwareFilesNotFound = "firmware_files_not_found";
    public const string SourceWimMissing = "source_wim_missing";
    public const string DestinationExists = "destination_exists";
    public const string DestinationCreateFailed = "destination_create_failed";
    public const string DirectoryCreateFailed = "directory_create_failed";
    public const string MediaCopyFailed = "media_copy_failed";
    public const string WimCopyFailed = "wim_copy_failed";
    public const string WimMountFailed = "wim_mount_failed";
    public const string BootFileCopyFailed = "boot_file_copy_failed";
    public const string BootSectorFileCopyFailed = "boot_sector_file_copy_failed";
    public const string WimUnmountFailed = "wim_unmount_failed";
    public const string InvalidArguments = "invalid_arguments";

    /// <summary>The script produced output without any known error line.</summary>
    public const string Unrecognized = "unrecognized";

    /// <summary>Nothing was captured, for example when the ADK environment script fails before copype runs.</summary>
    public const string NoOutput = "no_output";

    // The script echoes these lines in English on every system; the tool errors around them are localized.
    // Order matters: "Unable to copy boot files" (media folder) must be tested before its prefix
    // "Unable to copy boot file" (boot manager), and the unmount cleanup error last so it never hides
    // the step that failed first.
    private static readonly (string Marker, string Detail)[] ErrorLineMarkers =
    [
        ("The following processor architecture was not found", ArchitectureNotFound),
        ("The following path for firmware files was not found", FirmwareFilesNotFound),
        ("WinPE WIM file does not exist", SourceWimMissing),
        ("Destination directory exists", DestinationExists),
        ("Unable to create destination", DestinationCreateFailed),
        ("Unable to create directory", DirectoryCreateFailed),
        ("Unable to copy boot files", MediaCopyFailed),
        ("Unable to copy WinPE WIM", WimCopyFailed),
        ("Failed to mount the WinPE WIM file", WimMountFailed),
        ("Unable to copy boot file", BootFileCopyFailed),
        ("Unable to copy boot sector file", BootSectorFileCopyFailed),
        ("still mounted", WimUnmountFailed),
        ("Creates working directories for WinPE image customization", InvalidArguments)
    ];

    internal static string Classify(string standardOutput, string standardError)
    {
        foreach ((string marker, string detail) in ErrorLineMarkers)
        {
            if (standardOutput.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
                standardError.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return detail;
            }
        }

        return string.IsNullOrWhiteSpace(standardOutput) && string.IsNullOrWhiteSpace(standardError)
            ? NoOutput
            : Unrecognized;
    }
}
