// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;
using System.Text.RegularExpressions;
using Foundry.Core.Models.Configuration;

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
        "UUID",
        "WinPE",
        "ADK",
        "ISO",
        "USB",
        "Wi-Fi",
        "sources\\sxs",
        "KB",
        "MB",
        "GB",
        "TB"
    ];

    private static readonly Dictionary<string, string> FrenchByteUnits = new(StringComparer.Ordinal)
    {
        ["Ko"] = "KB",
        ["Mo"] = "MB",
        ["Go"] = "GB",
        ["To"] = "TB"
    };

    private const string UntranslatedValueAllowListFileName = "UntranslatedValueAllowList.txt";

    private static readonly string[] SpellingStems =
    [
        "author", "capital", "categor", "central", "custom", "emphas", "final", "general", "initial", "local",
        "maxim", "minim", "modern", "normal", "optim", "organ", "personal", "priorit", "real", "recogn", "sanit",
        "serial", "special", "standard", "summar", "synchron", "util", "virtual", "visual"
    ];

    private static readonly string[] SpellingSuffixes = ["e", "ed", "es", "ing", "ation", "ations", "ational", "er", "ers"];

    private static readonly Dictionary<string, string> BritishToAmericanSpelling = CreateBritishToAmericanSpelling();

    private static readonly string[] SupportedCultures = FoundrySupportedCultures.CreateCatalog()
        .CreateOptions(System.Globalization.CultureInfo.GetCultureInfo("en-US"), key => key)
        .Select(option => option.Code).ToArray();

    public static TheoryData<string, string> ResourceSets => new()
    {
        { "Foundry", ".resw" },
        { "Foundry.AutopilotInteractive", ".resx" },
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
            "AiComponentsPageHeader.Description",
            "AppRemovalPageHeader.Description",
            "AutopilotInteractiveHashUploadPageHeader.Description",
            "AutopilotJsonProfilePageHeader.Description",
            "AutopilotZeroTouchPageHeader.Description",
            "EthernetDot1xPageHeader.Description",
            "GeneralConfiguration.PageDescription",
            "MachineNamingPageHeader.Description",
            "NetworkCachePageHeader.Description",
            "OobePageHeader.Description",
            "OptionalFeaturesPageHeader.Description",
            "OsSelectionPageHeader.Description",
            "WifiPageHeader.Description",
            "StartMedia.PageDescription",
            "CustomImages.PxeWarning",
            "PostInstallation.PxeWarning",
            "CustomDrivers.PxeWarning"
        ];

        Assert.Empty(expectedKeys.Except(enUsKeys, StringComparer.Ordinal));
    }

    [Fact]
    public void FoundryResources_PreserveInvariantOobeTitles()
    {
        string stringsRoot = Path.Combine(FindSourceRoot(), "Foundry", "Strings");

        foreach (string culture in SupportedCultures)
        {
            string culturePath = Path.Combine(stringsRoot, culture, "Resources.resw");
            IReadOnlyDictionary<string, string> values = ReadResourceValues(culturePath);

            Assert.True(
                string.Equals("Out-of-Box Experience", values["OobePageHeader.Title"], StringComparison.Ordinal),
                $"{culture} must preserve OobePageHeader.Title as 'Out-of-Box Experience'.");
            Assert.True(
                string.Equals("OOBE", values["Nav_OobeKey.Title"], StringComparison.Ordinal),
                $"{culture} must preserve Nav_OobeKey.Title as 'OOBE'.");
        }
    }

    [Fact]
    public void FoundryResources_IncludeWindowsOptionalFeatureCatalogKeys()
    {
        string sourceRoot = FindSourceRoot();
        string enUsPath = Path.Combine(sourceRoot, "Foundry", "Strings", "en-US", "Resources.resw");
        SortedSet<string> enUsKeys = ReadResourceKeys(enUsPath);
        string[] expectedKeys = WindowsOptionalFeatureCatalog.Entries
            .SelectMany(entry => new[]
            {
                entry.DisplayNameResourceKey,
                entry.CategoryResourceKey,
                entry.WarningResourceKey
            })
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(expectedKeys.Except(enUsKeys, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ResourceSets))]
    public void LocalizedResourceFiles_MatchEnUsKeys(string projectName, string extension)
    {
        string sourceRoot = FindSourceRoot();
        string stringsRoot = Path.Combine(sourceRoot, projectName, "Strings");
        string enUsPath = Path.Combine(stringsRoot, "en-US", $"Resources{extension}");
        SortedSet<string> enUsKeys = ReadResourceKeys(enUsPath);

        foreach (string culture in SupportedCultures)
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
    public void LocalizedResourceFiles_MatchEnUsPlaceholders(string projectName, string extension)
    {
        string sourceRoot = FindSourceRoot();
        string stringsRoot = Path.Combine(sourceRoot, projectName, "Strings");
        string enUsPath = Path.Combine(stringsRoot, "en-US", $"Resources{extension}");
        IReadOnlyDictionary<string, string> enUsValues = ReadResourceValues(enUsPath);

        foreach (string culture in SupportedCultures)
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
    public void LocalizedResourceFiles_PreserveTechnicalTokens(string projectName, string extension)
    {
        string sourceRoot = FindSourceRoot();
        string stringsRoot = Path.Combine(sourceRoot, projectName, "Strings");
        string enUsPath = Path.Combine(stringsRoot, "en-US", $"Resources{extension}");
        IReadOnlyDictionary<string, string> enUsValues = ReadResourceValues(enUsPath);

        foreach (string culture in SupportedCultures)
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

                string[] actualTokens = ExtractTechnicalTokens(NormalizeLocalizedByteUnits(culture, cultureValues[key]));

                Assert.True(
                    expectedTokens.SequenceEqual(actualTokens, StringComparer.Ordinal),
                    $"{projectName} {culture} {key}: expected technical tokens [{string.Join(", ", expectedTokens)}], actual [{string.Join(", ", actualTokens)}]");
            }
        }
    }

    public static TheoryData<string> WpfResourceProjects => new()
    {
        "Foundry.AutopilotInteractive",
        "Foundry.Connect",
        "Foundry.Deploy"
    };

    [Theory]
    [MemberData(nameof(ResourceSets))]
    public void NonEnglishCultures_DoNotCopyEnglishValues_ExceptAllowList(string projectName, string extension)
    {
        string sourceRoot = FindSourceRoot();
        string stringsRoot = Path.Combine(sourceRoot, projectName, "Strings");
        IReadOnlyDictionary<string, string> enUsValues = ReadResourceValues(Path.Combine(stringsRoot, "en-US", $"Resources{extension}"));
        AllowListEntry[] allowList = ReadUntranslatedValueAllowList(sourceRoot, projectName);
        List<string> failures = [];

        foreach (string culture in SupportedCultures.Where(culture => !culture.StartsWith("en-", StringComparison.Ordinal)))
        {
            IReadOnlyDictionary<string, string> cultureValues = ReadResourceValues(Path.Combine(stringsRoot, culture, $"Resources{extension}"));

            foreach ((string key, string enUsValue) in enUsValues)
            {
                if (string.Equals(enUsValue, cultureValues[key], StringComparison.Ordinal)
                    && ContainsTranslatableText(enUsValue)
                    && !allowList.Any(entry => entry.Allows(key, culture)))
                {
                    failures.Add($"{culture} {key}: '{enUsValue}'");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{projectName} values are copied from en-US. Translate them or add them to {UntranslatedValueAllowListFileName}:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Theory]
    [MemberData(nameof(ResourceSets))]
    public void EnGb_MatchesEnUsExceptSpelling(string projectName, string extension)
    {
        string stringsRoot = Path.Combine(FindSourceRoot(), projectName, "Strings");
        IReadOnlyDictionary<string, string> enUsValues = ReadResourceValues(Path.Combine(stringsRoot, "en-US", $"Resources{extension}"));
        IReadOnlyDictionary<string, string> enGbValues = ReadResourceValues(Path.Combine(stringsRoot, "en-GB", $"Resources{extension}"));

        string[] failures = enUsValues
            .Where(entry => !string.Equals(
                NormalizeEnglishSpelling(entry.Value),
                NormalizeEnglishSpelling(enGbValues[entry.Key]),
                StringComparison.Ordinal))
            .Select(entry => $"{entry.Key}: en-US '{entry.Value}', en-GB '{enGbValues[entry.Key]}'")
            .ToArray();

        Assert.True(
            failures.Length == 0,
            $"{projectName} en-GB differs from en-US beyond spelling:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Theory]
    [MemberData(nameof(WpfResourceProjects))]
    public void AccessKeys_HaveNoLeadingSpace(string projectName)
    {
        string stringsRoot = Path.Combine(FindSourceRoot(), projectName, "Strings");
        IReadOnlyDictionary<string, string> enUsValues = ReadResourceValues(Path.Combine(stringsRoot, "en-US", "Resources.resx"));
        string[] accessKeyResourceKeys = enUsValues
            .Where(entry => Regex.IsMatch(entry.Value, @"_[\p{L}\p{N}]"))
            .Select(entry => entry.Key)
            .ToArray();
        List<string> failures = [];

        foreach (string culture in SupportedCultures)
        {
            IReadOnlyDictionary<string, string> cultureValues = ReadResourceValues(Path.Combine(stringsRoot, culture, "Resources.resx"));

            foreach (string key in accessKeyResourceKeys)
            {
                string value = cultureValues[key];
                if (value.Count(character => character == '_') != 1 || !Regex.IsMatch(value, @"_[\p{L}\p{N}]"))
                {
                    failures.Add($"{culture} {key}: '{value}'");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{projectName} access keys need exactly one '_' directly before a letter:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
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

    [Theory]
    [InlineData("Step {0} of {1}", "Sur {1} étapes, étape {0}", true)]
    [InlineData("{0} and {0}", "{0}", false)]
    [InlineData("{0} and {1}", "{0} and {2}", false)]
    [InlineData("Size: {0:N2}", "Taille : {0}", false)]
    public void PlaceholderComparison_PreservesArgumentsWithoutRequiringTheirOrder(
        string reference,
        string translation,
        bool expectedMatch)
    {
        Assert.Equal(
            expectedMatch,
            ExtractPlaceholders(reference).SequenceEqual(ExtractPlaceholders(translation), StringComparer.Ordinal));
    }
    private static bool ContainsTranslatableText(string value)
    {
        return Regex.IsMatch(Regex.Replace(value, @"\{\d+(?::[^}]*)?\}", string.Empty), @"\p{L}");
    }

    private static AllowListEntry[] ReadUntranslatedValueAllowList(string sourceRoot, string projectName)
    {
        string path = Path.Combine(sourceRoot, "Foundry.Localization.Tests", UntranslatedValueAllowListFileName);

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => string.Equals(fields[0], projectName, StringComparison.Ordinal))
            .Select(fields => new AllowListEntry(fields[1], fields.Skip(2).ToHashSet(StringComparer.Ordinal)))
            .ToArray();
    }

    /// <summary>
    /// Rewrites known British spellings to American spellings so en-GB can be compared with en-US.
    /// </summary>
    private static string NormalizeEnglishSpelling(string value)
    {
        string normalized = Regex.Replace(
            value,
            @"\bfull stops?\b",
            match => MatchCase(match.Value, match.Value.EndsWith('s') ? "periods" : "period"),
            RegexOptions.IgnoreCase);

        return Regex.Replace(
            normalized,
            @"\p{L}+",
            match => BritishToAmericanSpelling.TryGetValue(match.Value.ToLowerInvariant(), out string? american)
                ? MatchCase(match.Value, american)
                : match.Value);
    }

    private static string MatchCase(string source, string replacement)
    {
        if (source.All(character => !char.IsLetter(character) || char.IsUpper(character)))
        {
            return replacement.ToUpperInvariant();
        }

        return char.IsUpper(source[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }

    private static Dictionary<string, string> CreateBritishToAmericanSpelling()
    {
        Dictionary<string, string> spelling = new(StringComparer.Ordinal)
        {
            ["analyse"] = "analyze",
            ["analysed"] = "analyzed",
            ["analysing"] = "analyzing",
            ["behaviour"] = "behavior",
            ["behaviours"] = "behaviors",
            ["cancelled"] = "canceled",
            ["cancelling"] = "canceling",
            ["catalogue"] = "catalog",
            ["catalogues"] = "catalogs",
            ["centre"] = "center",
            ["centres"] = "centers",
            ["colour"] = "color",
            ["coloured"] = "colored",
            ["colours"] = "colors",
            ["enrol"] = "enroll",
            ["enrolment"] = "enrollment",
            ["enrolments"] = "enrollments",
            ["enrols"] = "enrolls",
            ["favourite"] = "favorite",
            ["favourites"] = "favorites",
            ["licence"] = "license",
            ["licences"] = "licenses",
            ["programme"] = "program"
        };

        foreach (string stem in SpellingStems)
        {
            foreach (string suffix in SpellingSuffixes)
            {
                spelling[$"{stem}is{suffix}"] = $"{stem}iz{suffix}";
            }
        }

        return spelling;
    }

    private sealed record AllowListEntry(string KeyPattern, IReadOnlySet<string> Cultures)
    {
        public bool Allows(string key, string culture)
        {
            bool keyMatches = KeyPattern.EndsWith('*')
                ? key.StartsWith(KeyPattern[..^1], StringComparison.Ordinal)
                : string.Equals(KeyPattern, key, StringComparison.Ordinal);

            return keyMatches && (Cultures.Count == 0 || Cultures.Contains(culture));
        }
    }

    private static string[] ExtractPlaceholders(string value)
    {
        return Regex
            .Matches(value, @"\{\d+(?::[^}]*)?\}")
            .Select(match => match.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Maps the French byte-unit symbols (Ko, Mo, Go, To) to their protected English tokens so French cultures can
    /// localize units without failing the technical-token check.
    /// </summary>
    private static string NormalizeLocalizedByteUnits(string culture, string value)
    {
        return culture.StartsWith("fr-", StringComparison.Ordinal)
            ? Regex.Replace(value, @"(?<!\p{L})(?:Ko|Mo|Go|To)(?!\p{L})", match => FrenchByteUnits[match.Value])
            : value;
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
