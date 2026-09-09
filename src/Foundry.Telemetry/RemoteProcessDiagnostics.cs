// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Processes;
using System.IO;
using System.Text.RegularExpressions;

namespace Foundry.Telemetry;

/// <summary>
/// Builds remote process failure fields without exporting raw command lines or environment variables.
/// </summary>
public static partial class RemoteProcessDiagnostics
{
    private static readonly HashSet<string> ReviewedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "dism.exe", "7z.exe", "7za.exe", "bcdboot.exe", "bootsect.exe", "diskpart.exe"
    };

    /// <summary>
    /// Includes bounded redacted output only for reviewed deployment tools. Other processes retain
    /// their tool, exit code, and duration; their arbitrary output remains in local logs.
    /// </summary>
    public static IReadOnlyDictionary<string, object> CreateProperties(ProcessExecutionResult result, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(result);
        string tool = Path.GetFileName(result.FileName).ToLowerInvariant();
        var properties = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["ToolName"] = RemoteDiagnosticText.Sanitize(tool, 128),
            ["ExitCode"] = result.ExitCode,
            ["ProcessDurationMs"] = Math.Round(duration.TotalMilliseconds),
            ["ProcessOutputOmitted"] = !ReviewedTools.Contains(tool)
        };

        if (ReviewedTools.Contains(tool))
        {
            string stdout = SelectDiagnosticOutput(tool, result.StandardOutput);
            string stderr = SelectDiagnosticOutput(tool, result.StandardError);
            properties["ProcessStdout"] = RemoteDiagnosticText.SanitizeOutput(stdout);
            properties["ProcessStderr"] = RemoteDiagnosticText.SanitizeOutput(stderr);
            properties["ProcessOutputFiltered"] = stdout != result.StandardOutput || stderr != result.StandardError;
            properties["ProcessOutputOmitted"] = string.IsNullOrEmpty(stdout) && string.IsNullOrEmpty(stderr) &&
                (!string.IsNullOrEmpty(result.StandardOutput) || !string.IsNullOrEmpty(result.StandardError));
        }

        if (tool == "dism.exe")
        {
            // Remove quoted values before matching fixed command switches, never their arguments.
            string arguments = QuotedArgumentPattern().Replace(result.Arguments, string.Empty);
            Match operation = DismOperationPattern().Match(arguments);
            if (operation.Success)
            {
                properties["ProcessOperation"] = operation.Groups[1].Value;
            }
        }

        return properties;
    }

    private static string SelectDiagnosticOutput(string tool, string output)
    {
        Regex pattern = tool switch
        {
            "dism.exe" => DismErrorPattern(),
            "7z.exe" or "7za.exe" => ArchiveErrorPattern(),
            "diskpart.exe" => DiskPartErrorPattern(),
            _ => BootErrorPattern()
        };
        return string.Join('\n', pattern.Matches(output).Select(static match => match.Value.Trim()));
    }

    [GeneratedRegex("\"[^\"]*\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedArgumentPattern();

    [GeneratedRegex("(?:^|\\s)/(Get-ImageInfo|Apply-Image|Mount-Image|Unmount-Image|Export-Image|Get-Features|Enable-Feature|Disable-Feature|Add-Driver|Add-Package|Cleanup-Image|Set-AllIntl|Set-InputLocale)(?=\\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DismOperationPattern();

    // Only error paragraphs are exported: image names, archive members, machine headers,
    // and volume inventories are not diagnostic text and may contain identifying values.
    [GeneratedRegex("^Error:[ \\t]*(?:0x[0-9a-f]+|[0-9]+)[^\\r\\n]*(?:\\r?\\n(?:[ \\t]*\\r?\\n)?[^\\r\\n]+(?:\\r?\\n[^\\r\\n]+)*)?", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DismErrorPattern();

    [GeneratedRegex("^(?:ERROR:\\s*(?:Data Error|CRC Failed|Wrong password|Can not open (?:the )?file as (?:an? )?archive|Cannot open (?:the )?file as (?:an? )?archive|Unsupported Method|Unexpected end of (?:archive|data)|Headers Error)|(?:Archives with Errors|Sub items Errors|Open Errors|Errors):\\s*[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveErrorPattern();

    [GeneratedRegex("^(?:DiskPart has encountered an error:[^\\r\\n]*|Virtual Disk Service error:[ \\t]*\\r?\\n[^\\r\\n]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DiskPartErrorPattern();

    [GeneratedRegex("^(?:BFSVC Error:|Failure |Error:|ERROR:|Could not |Failed |Access is denied)[^\\r\\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BootErrorPattern();
}
