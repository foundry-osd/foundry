using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeHelperTests
{
    [Theory]
    [InlineData(" fr_FR ", "fr-fr")]
    [InlineData("EN-us", "en-us")]
    [InlineData("", "")]
    public void NormalizeLanguageCode_ReturnsLowercaseComparisonValue(string input, string expected)
    {
        string normalized = WinPeLanguageUtility.Normalize(input);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(" fr_FR ", "fr-FR")]
    [InlineData("EN-us", "en-US")]
    [InlineData("invalid-culture-code", "invalid-culture-code")]
    public void Canonicalize_ReturnsCultureInfoNameWhenCultureExists(string input, string expected)
    {
        string canonical = WinPeLanguageUtility.Canonicalize(input);

        Assert.Equal(expected, canonical);
    }

    [Fact]
    public void TryResolveInputLocale_WhenCultureExists_ReturnsCanonicalCodeAndLocale()
    {
        bool success = WinPeLanguageUtility.TryResolveInputLocale("fr-FR", out string canonicalLanguageCode, out string inputLocale);

        Assert.True(success);
        Assert.Equal("fr-FR", canonicalLanguageCode);
        Assert.Matches("^[0-9a-f]{4}:0000[0-9a-f]{4}$", inputLocale);
    }

    [Fact]
    public void TryResolveInputLocale_WhenCultureIsUnknown_ReturnsFalse()
    {
        bool success = WinPeLanguageUtility.TryResolveInputLocale("invalid-culture-code", out string canonicalLanguageCode, out string inputLocale);

        Assert.False(success);
        Assert.Equal("invalid-culture-code", canonicalLanguageCode);
        Assert.Equal(string.Empty, inputLocale);
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "amd64", "bootx64.efi", "win-x64", "x64")]
    [InlineData(WinPeArchitecture.Arm64, "arm64", "bootaa64.efi", "win-arm64", "arm64")]
    public void ArchitectureMappings_ReturnExpectedValues(
        WinPeArchitecture architecture,
        string expectedCopypeArchitecture,
        string expectedBootEfiName,
        string expectedRuntimeIdentifier,
        string expectedSevenZipFolder)
    {
        Assert.Equal(expectedCopypeArchitecture, architecture.ToCopypeArchitecture());
        Assert.Equal(expectedBootEfiName, architecture.ToBootEfiName());
        Assert.Equal(expectedRuntimeIdentifier, architecture.ToDotnetRuntimeIdentifier());
        Assert.Equal(expectedSevenZipFolder, architecture.ToSevenZipRuntimeFolder());
    }

    [Fact]
    public void SanitizePathSegment_ReplacesInvalidCharactersAndSpaces()
    {
        string sanitized = WinPeFileSystemHelper.SanitizePathSegment(" Folder Name<>:*? ");

        Assert.Equal("Folder_Name_____", sanitized);
    }

    [Fact]
    public void ContainsFileRecursive_ReturnsTrueWhenMatchExistsInSubdirectory()
    {
        using var tempDirectory = new TemporaryDirectory();
        string nestedDirectory = Path.Combine(tempDirectory.Path, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "driver.inf"), "content");

        bool found = WinPeFileSystemHelper.ContainsFileRecursive(tempDirectory.Path, "*.inf");

        Assert.True(found);
    }
}
