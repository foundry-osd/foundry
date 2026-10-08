// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Foundry.Utilities.Processes;

/// <summary>
/// Creates command-line arguments for encoded PowerShell scripts and reads the failures they report.
/// </summary>
public static partial class PowerShellCommand
{
    private const string ClixmlHeader = "#< CLIXML";
    private const int MaximumErrorTextLength = 4096;
    private const string Truncated = "<truncated>";

    /// <summary>
    /// Encodes a script as UTF-16LE Base64 and returns independent PowerShell argument tokens.
    /// </summary>
    /// <param name="script">The PowerShell script to execute.</param>
    public static IReadOnlyList<string> CreateEncodedArguments(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return
        [
            "-EncodedCommand",
            encodedScript
        ];
    }

    /// <summary>
    /// Describes a failed PowerShell execution as a summary with its exit code, followed by the error text it reported.
    /// </summary>
    /// <param name="summary">A sentence stating what failed.</param>
    /// <param name="execution">The completed PowerShell process.</param>
    internal static string DescribeFailure(string summary, ProcessExecutionResult execution)
    {
        string description = $"{summary} ExitCode={execution.ExitCode}.";
        string errorText = ReadErrorText(execution.StandardError);
        return errorText.Length == 0
            ? description
            : description + Environment.NewLine + errorText;
    }

    /// <summary>
    /// Converts the redirected standard error of a PowerShell host into bounded plain text.
    /// </summary>
    /// <remarks>
    /// Windows PowerShell serializes redirected error records as CLIXML when it runs an encoded command. Only the
    /// error records are kept, so progress records cannot hide the failure. Any other content is returned as written.
    /// The beginning of the text is retained because the first error is the one that explains the rest.
    /// </remarks>
    internal static string ReadErrorText(string? standardError)
    {
        string text = standardError?.Trim() ?? string.Empty;
        if (text.StartsWith(ClixmlHeader, StringComparison.Ordinal))
        {
            text = string.Concat(ClixmlErrorPattern().Matches(text).Select(DecodeClixmlText));
        }

        text = string.Join(
            Environment.NewLine,
            text.Split(["\r\n", "\n"], StringSplitOptions.None).Select(static line => line.TrimEnd())).Trim();

        return text.Length <= MaximumErrorTextLength
            ? text
            : text[..MaximumErrorTextLength] + Environment.NewLine + Truncated;
    }

    private static string DecodeClixmlText(Match errorRecord)
    {
        return ClixmlEscapePattern().Replace(
            WebUtility.HtmlDecode(errorRecord.Groups[1].Value),
            static escape => ((char)Convert.ToInt32(escape.Groups[1].Value, 16)).ToString());
    }

    [GeneratedRegex("<S S=\"Error\">(.*?)</S>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ClixmlErrorPattern();

    // CLIXML writes control characters, such as line breaks, as _xHHHH_ UTF-16 code units.
    [GeneratedRegex("_x([0-9A-Fa-f]{4})_", RegexOptions.CultureInvariant)]
    private static partial Regex ClixmlEscapePattern();
}
