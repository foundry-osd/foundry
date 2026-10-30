// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class RuntimePreferencesHandoffTests
{
    [Theory]
    [InlineData(RuntimeThemePreference.System, "fr-FR")]
    [InlineData(RuntimeThemePreference.Light, "ja-JP")]
    [InlineData(RuntimeThemePreference.Dark, "sr-Latn-RS")]
    public void Write_ThenTryRead_RoundTrips(RuntimeThemePreference theme, string culture)
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, "Runtime", RuntimePreferencesHandoff.FileName);

        try
        {
            RuntimePreferencesHandoff.Write(path, new RuntimePreferences(theme, culture));

            Assert.True(RuntimePreferencesHandoff.TryRead(path, out RuntimePreferences? preferences));
            Assert.Equal(new RuntimePreferences(theme, culture), preferences);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_UsesAStableJsonShapeAndCanonicalCulture()
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, RuntimePreferencesHandoff.FileName);

        try
        {
            RuntimePreferencesHandoff.Write(path, new RuntimePreferences(RuntimeThemePreference.Dark, "fr-fr"));

            Assert.Equal("{\"theme\":\"dark\",\"culture\":\"fr-FR\"}", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_ReplacesAnExistingFileAndLeavesNoTemporaryFile()
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, RuntimePreferencesHandoff.FileName);

        try
        {
            RuntimePreferencesHandoff.Write(path, new RuntimePreferences(RuntimeThemePreference.Light, "de-DE"));
            RuntimePreferencesHandoff.Write(path, new RuntimePreferences(RuntimeThemePreference.Dark, "it-IT"));

            Assert.True(RuntimePreferencesHandoff.TryRead(path, out RuntimePreferences? preferences));
            Assert.Equal(new RuntimePreferences(RuntimeThemePreference.Dark, "it-IT"), preferences);
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fr_FR!")]
    public void Write_RejectsAnUnusableCulture(string culture)
    {
        Assert.Throws<ArgumentException>(() =>
            RuntimePreferencesHandoff.Write(Path.Combine(Path.GetTempPath(), "unused.json"), new RuntimePreferences(RuntimeThemePreference.Light, culture)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("{\"theme\":\"dark\"}")]
    [InlineData("{\"culture\":\"fr-FR\"}")]
    [InlineData("{\"theme\":\"sepia\",\"culture\":\"fr-FR\"}")]
    [InlineData("{\"theme\":\"1\",\"culture\":\"fr-FR\"}")]
    [InlineData("{\"theme\":\"dark\",\"culture\":\"\"}")]
    [InlineData("{\"theme\":\"dark\",\"culture\":\"fr_FR!\"}")]
    [InlineData("{\"theme\":42,\"culture\":\"fr-FR\"}")]
    public void TryRead_InvalidContent_ReturnsFalse(string content)
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, RuntimePreferencesHandoff.FileName);

        try
        {
            File.WriteAllText(path, content);

            Assert.False(RuntimePreferencesHandoff.TryRead(path, out RuntimePreferences? preferences));
            Assert.Null(preferences);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRead_ThemeIsCaseInsensitive()
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, RuntimePreferencesHandoff.FileName);

        try
        {
            File.WriteAllText(path, "{\"theme\":\"Light\",\"culture\":\"de-DE\"}");

            Assert.True(RuntimePreferencesHandoff.TryRead(path, out RuntimePreferences? preferences));
            Assert.Equal(RuntimeThemePreference.Light, preferences.Theme);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsFalse()
    {
        Assert.False(RuntimePreferencesHandoff.TryRead(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"), out _));
    }

    [Fact]
    public void TryRead_OversizedFile_ReturnsFalse()
    {
        string directory = CreateDirectory();
        string path = Path.Combine(directory, RuntimePreferencesHandoff.FileName);

        try
        {
            File.WriteAllText(path, "{\"theme\":\"dark\",\"culture\":\"fr-FR\"}" + new string(' ', RuntimePreferencesHandoff.MaximumFileBytes));

            Assert.False(RuntimePreferencesHandoff.TryRead(path, out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DefaultPath_IsInTheWinPeRuntimeFolder()
    {
        Assert.Equal(@"X:\Foundry\Runtime\runtime-preferences.json", RuntimePreferencesHandoff.DefaultPath);
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Foundry.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
