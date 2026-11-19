// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.WinPE.Shell;
using Foundry.WinPE.Tests.Support;
using Foundry.WinPE.Ui;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

public sealed class UiCrashWindowTests
{
    [Theory]
    [InlineData(true, (int)MessageChoice.Primary, (int)UiCrashChoice.Relaunch)]
    [InlineData(true, (int)MessageChoice.Secondary, (int)UiCrashChoice.Close)]
    [InlineData(true, (int)MessageChoice.None, (int)UiCrashChoice.Close)]
    [InlineData(false, (int)MessageChoice.Primary, (int)UiCrashChoice.Close)]
    [InlineData(false, (int)MessageChoice.None, (int)UiCrashChoice.Close)]
    public void OnlyTheRelaunchButtonOfAnAllowedWindowRelaunches(bool relaunchAllowed, int choice, int expected)
        => Assert.Equal((UiCrashChoice)expected, UiCrashWindow.ToChoice(relaunchAllowed, (MessageChoice)choice));

    [Fact]
    public void CreateModel_UsesTheStateCulture()
    {
        var strings = new ResourceManager("Foundry.WinPE.Strings.Resources", typeof(UiCrashWindow).Assembly);
        CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");

        MessagePageModel model = UiCrashWindow.CreateModel(relaunchAllowed: true, @"X:\Logs", "fr-FR");

        Assert.Equal(strings.GetString("UiCrash.Title", french), model.Title);
        Assert.NotEqual(strings.GetString("UiCrash.Title", CultureInfo.InvariantCulture), model.Title);
        Assert.Equal(strings.GetString("UiCrash.Relaunch", french), model.PrimaryText);
        Assert.Equal(strings.GetString("UiCrash.Close", french), model.SecondaryText);
        Assert.StartsWith(strings.GetString("UiCrash.MessageRelaunchAllowed", french), model.Message, StringComparison.Ordinal);
        Assert.EndsWith(@"X:\Logs", model.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a culture")]
    [InlineData("es-AR")]
    public void CreateModel_WithoutAValidCulture_KeepsTheCurrentUiCulture(string? culture)
    {
        var strings = new ResourceManager("Foundry.WinPE.Strings.Resources", typeof(UiCrashWindow).Assembly);

        MessagePageModel model = UiCrashWindow.CreateModel(relaunchAllowed: false, logDirectory: null, culture);

        Assert.Equal(strings.GetString("UiCrash.Title", CultureInfo.CurrentUICulture), model.Title);
        Assert.Equal(strings.GetString("UiCrash.Close", CultureInfo.CurrentUICulture), model.PrimaryText);
        Assert.Null(model.SecondaryText);
        Assert.Equal(strings.GetString("UiCrash.MessageRelaunchRefused", CultureInfo.CurrentUICulture), model.Message);
    }

    [Theory]
    [InlineData("fr-FR", "fr-FR")]
    [InlineData("FR-fr", "fr-FR")]
    [InlineData("xx-INVALID", null)]
    [InlineData(null, null)]
    public void ResolveCulture_ReturnsOnlyASupportedFoundryCulture(string? culture, string? expected) =>
        Assert.Equal(expected, UiCrashWindow.ResolveCulture(culture)?.Name);

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("not a culture")]
    public void WithUiCulture_RestoresThePreviousUiCulture(string culture)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            string? during = UiCrashWindow.WithUiCulture(culture, () => CultureInfo.CurrentUICulture.Name);

            Assert.Equal(culture == "fr-FR" ? "fr-FR" : "de-DE", during);
            Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void WithUiCulture_RestoresTheCultureWhenTheActionThrows()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Throws<InvalidOperationException>(() => UiCrashWindow.WithUiCulture<int>("fr-FR", () => throw new InvalidOperationException()));

            Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void EveryCultureFormatsTheLogFolder()
    {
        var strings = new ResourceManager("Foundry.WinPE.Strings.Resources", typeof(UiCrashWindow).Assembly);
        string[] cultures = Directory.GetDirectories(Path.Combine(RepositoryRoot.Find(), "src", "Foundry.WinPE", "Strings"))
            .Select(path => Path.GetFileName(path))
            .ToArray();

        Assert.True(cultures.Length > 30);
        Assert.All(cultures, name => Assert.Contains(@"X:\Foundry\Logs",
            string.Format(CultureInfo.InvariantCulture, strings.GetString("UiCrash.LogFolder", CultureInfo.GetCultureInfo(name))!, @"X:\Foundry\Logs")));
    }
}
