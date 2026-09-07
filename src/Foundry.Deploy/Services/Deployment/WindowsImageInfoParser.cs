// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundry.Deploy.Services.Deployment;

internal static class WindowsImageInfoParser
{
    public static WindowsImageInfo Parse(string output, int expectedIndex)
    {
        MatchCollection sections = Regex.Matches(output, @"^[\t ]*Index[\t ]*:", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (sections.Count != 1) throw Invalid("Index");
        // DISM prints its own Version before the image's Index and metadata.
        output = output[sections[0].Index..];
        int index = ReadNonnegativeInt(Required(output, "Index"), "Index");
        if (index < 1 || index != expectedIndex) throw Invalid("Index");
        string edition = ReadEdition(output);
        if (edition.Length == 0) throw Invalid("Edition");
        string architecture = NormalizeArchitecture(Required(output, "Architecture"));
        string versionText = Required(output, "Version");
        if (!Regex.IsMatch(versionText, @"^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$") ||
            !Version.TryParse(versionText, out Version? version)) throw Invalid("Version");
        int revision = ReadNonnegativeInt(Required(output, "ServicePack Build"), "ServicePack Build");
        if (version.Revision >= 0 && version.Revision != revision) throw Invalid("Version");
        version = new Version(version.Major, version.Minor, version.Build, revision);
        string size = Required(output, "Size");
        Match sizeMatch = Regex.Match(size, @"^(?<bytes>(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)) bytes$", RegexOptions.IgnoreCase);
        if (!sizeMatch.Success || !long.TryParse(sizeMatch.Groups["bytes"].Value.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.None, CultureInfo.InvariantCulture, out long expandedBytes) || expandedBytes <= 0) throw Invalid("Size");
        return new WindowsImageInfo(index, edition, architecture, version, ReadDefaultLanguage(output), expandedBytes);
    }

    internal static string ReadEdition(string output)
    {
        string? edition = Optional(output, "Edition", allowUndefined: true);
        string? editionId = Optional(output, "Edition ID", allowUndefined: true);
        if (edition is not null && editionId is not null && !edition.Equals(editionId, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Edition");
        return editionId ?? edition ?? string.Empty;
    }

    internal static string NormalizeArchitecture(string architecture) => architecture.Trim().ToUpperInvariant() switch
    {
        "X64" or "AMD64" => "x64",
        "X86" => "x86",
        "ARM64" => "arm64",
        _ => throw Invalid("Architecture")
    };

    private static string ReadDefaultLanguage(string output)
    {
        string[] lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int[] headers = Enumerable.Range(0, lines.Length)
            .Where(index => Regex.IsMatch(lines[index], @"^[\t ]*Languages[\t ]*:", RegexOptions.IgnoreCase)).ToArray();
        if (headers.Length != 1) throw Invalid("Languages");
        var defaults = new List<string>();
        for (int index = headers[0]; index < lines.Length; index++)
        {
            string line = index == headers[0] ? lines[index][(lines[index].IndexOf(':') + 1)..].Trim() : lines[index].Trim();
            if (line.Length == 0) continue;
            Match language = Regex.Match(line, @"^(?<language>[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})+)(?<default> \(Default\))?$", RegexOptions.IgnoreCase);
            if (!language.Success)
            {
                if (index == headers[0] || line.Contains("(Default)", StringComparison.OrdinalIgnoreCase)) throw Invalid("Languages");
                break;
            }
            if (language.Groups["default"].Success) defaults.Add(language.Groups["language"].Value);
        }
        return defaults.Count == 1 ? defaults[0] : throw Invalid("Languages");
    }

    private static string Required(string output, string name) => Optional(output, name) ?? throw Invalid(name);

    private static string? Optional(string output, string name, bool allowUndefined = false)
    {
        MatchCollection matches = Regex.Matches(output, $@"^[\t ]*{Regex.Escape(name)}[\t ]*:[\t ]*([^\r\n]*)\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (matches.Count > 1) throw Invalid(name);
        if (matches.Count == 0) return null;
        string value = matches[0].Groups[1].Value.Trim();
        if (allowUndefined && (value.Length == 0 || value == "<undefined>")) return null;
        return value.Length > 0 && value != "<undefined>" ? value : throw Invalid(name);
    }

    private static int ReadNonnegativeInt(string value, string name) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) ? result : throw Invalid(name);

    private static InvalidDataException Invalid(string field) => new($"Image metadata field '{field}' is missing, invalid, or ambiguous.");
}
