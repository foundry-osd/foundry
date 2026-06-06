using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Foundry.Localization.Tests;

public sealed class ResourceKeyParityTests
{
    private static readonly string[] ProtectedTechnicalTokens =
    [
        "MakeWinPEMedia",
        "wpeutil.exe",
        "Windows ADK",
        "Windows PE",
        "PCA 2011",
        "PCA 2023",
        "802.1X",
        ".inf",
        "Autopilot",
        "Intune",
        "Graph",
        "JSON",
        "WinPE",
        "ADK",
        "ISO",
        "USB",
        "Wi-Fi",
        "KB",
        "MB",
        "GB",
        "TB"
    ];

    private static readonly string[] AdkCultures =
    [
        "ar-SA",
        "bg-BG",
        "cs-CZ",
        "da-DK",
        "de-DE",
        "el-GR",
        "en-GB",
        "en-US",
        "es-ES",
        "es-MX",
        "et-EE",
        "fi-FI",
        "fr-CA",
        "fr-FR",
        "he-IL",
        "hr-HR",
        "hu-HU",
        "it-IT",
        "ja-JP",
        "ko-KR",
        "lt-LT",
        "lv-LV",
        "nb-NO",
        "nl-NL",
        "pl-PL",
        "pt-BR",
        "pt-PT",
        "ro-RO",
        "ru-RU",
        "sk-SK",
        "sl-SI",
        "sr-Latn-RS",
        "sv-SE",
        "th-TH",
        "tr-TR",
        "uk-UA",
        "zh-CN",
        "zh-TW"
    ];

    public static TheoryData<string, string> ResourceSets => new()
    {
        { "Foundry", ".resw" },
        { "Foundry.Connect", ".resx" },
        { "Foundry.Deploy", ".resx" }
    };

    [Fact]
    public void FoundryResources_IncludePageHeaderDescriptions()
    {
        string sourceRoot = FindSourceRoot();
        string enUsPath = Path.Combine(sourceRoot, "Foundry", "Strings", "en-US", "Resources.resw");
        SortedSet<string> enUsKeys = ReadResourceKeys(enUsPath);

        string[] expectedKeys =
        [
            "Adk.PageDescription",
            "Customization.PageDescription",
            "GeneralConfiguration.PageDescription",
            "Network.PageDescription",
            "StartMedia.PageDescription"
        ];

        Assert.Empty(expectedKeys.Except(enUsKeys, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ResourceSets))]
    public void AdkResourceFiles_MatchEnUsKeys(string projectName, string extension)
    {
        string sourceRoot = FindSourceRoot();
        string stringsRoot = Path.Combine(sourceRoot, projectName, "Strings");
        string enUsPath = Path.Combine(stringsRoot, "en-US", $"Resources{extension}");
        SortedSet<string> enUsKeys = ReadResourceKeys(enUsPath);

        foreach (string culture in AdkCultures)
        {
            string culturePath = Path.Combine(stringsRoot, culture, $"Resources{extension}");
            Assert.True(File.Exists(culturePath), $"Missing resource file: {culturePath}");

            SortedSet<string> cultureKeys = ReadResourceKeys(culturePath);

            Assert.Empty(enUsKeys.Except(cultureKeys, StringComparer.Ordinal));
            Assert.Empty(cultureKeys.Except(enUsKeys, StringComparer.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(ResourceSets))]
    public void AdkResourceFiles_MatchEnUsPlaceholders(string projectName, string extension)
    {
        string sourceRoot = FindSourceRoot();
        string stringsRoot = Path.Combine(sourceRoot, projectName, "Strings");
        string enUsPath = Path.Combine(stringsRoot, "en-US", $"Resources{extension}");
        IReadOnlyDictionary<string, string> enUsValues = ReadResourceValues(enUsPath);

        foreach (string culture in AdkCultures)
        {
            string culturePath = Path.Combine(stringsRoot, culture, $"Resources{extension}");
            IReadOnlyDictionary<string, string> cultureValues = ReadResourceValues(culturePath);

            foreach ((string key, string enUsValue) in enUsValues)
            {
                string[] expectedPlaceholders = ExtractPlaceholders(enUsValue);
                string[] actualPlaceholders = ExtractPlaceholders(cultureValues[key]);

                Assert.True(
                    expectedPlaceholders.SequenceEqual(actualPlaceholders, StringComparer.Ordinal),
                    $"{projectName} {culture} {key}: expected placeholders [{string.Join(", ", expectedPlaceholders)}], actual [{string.Join(", ", actualPlaceholders)}]");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ResourceSets))]
    public void AdkResourceFiles_PreserveTechnicalTokens(string projectName, string extension)
    {
        string sourceRoot = FindSourceRoot();
        string stringsRoot = Path.Combine(sourceRoot, projectName, "Strings");
        string enUsPath = Path.Combine(stringsRoot, "en-US", $"Resources{extension}");
        IReadOnlyDictionary<string, string> enUsValues = ReadResourceValues(enUsPath);

        foreach (string culture in AdkCultures)
        {
            string culturePath = Path.Combine(stringsRoot, culture, $"Resources{extension}");
            IReadOnlyDictionary<string, string> cultureValues = ReadResourceValues(culturePath);

            foreach ((string key, string enUsValue) in enUsValues)
            {
                string[] expectedTokens = ExtractTechnicalTokens(enUsValue);
                if (expectedTokens.Length == 0)
                {
                    continue;
                }

                string[] actualTokens = ExtractTechnicalTokens(cultureValues[key]);

                Assert.True(
                    expectedTokens.SequenceEqual(actualTokens, StringComparer.Ordinal),
                    $"{projectName} {culture} {key}: expected technical tokens [{string.Join(", ", expectedTokens)}], actual [{string.Join(", ", actualTokens)}]");
            }
        }
    }

    private static string FindSourceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Foundry.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Foundry source root.");
    }

    private static SortedSet<string> ReadResourceKeys(string path)
    {
        XDocument document = XDocument.Load(path);
        return new SortedSet<string>(
            document
                .Descendants("data")
                .Select(element => element.Attribute("name")?.Value)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> ReadResourceValues(string path)
    {
        XDocument document = XDocument.Load(path);
        return document
            .Descendants("data")
            .Select(element => new
            {
                Name = element.Attribute("name")?.Value,
                Value = element.Element("value")?.Value ?? string.Empty
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name!, item => item.Value, StringComparer.Ordinal);
    }

    private static string[] ExtractPlaceholders(string value)
    {
        return Regex
            .Matches(value, @"\{\d+(?::[^}]*)?\}")
            .Select(match => match.Value)
            .ToArray();
    }

    private static string[] ExtractTechnicalTokens(string value)
    {
        return ProtectedTechnicalTokens
            .Select(token => new
            {
                Token = token,
                Count = CountOccurrences(value, token)
            })
            .Where(item => item.Count > 0)
            .SelectMany(item => Enumerable.Repeat(item.Token, item.Count))
            .ToArray();
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int index = 0;

        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
