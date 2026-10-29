// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.IO;
using System.Resources;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Lifetime;

public sealed class RuntimePreferencesPublisherTests
{
    [Theory]
    [InlineData(ThemeMode.System, RuntimeThemePreference.System)]
    [InlineData(ThemeMode.Light, RuntimeThemePreference.Light)]
    [InlineData(ThemeMode.Dark, RuntimeThemePreference.Dark)]
    public void ToRuntime_MapsEveryTheme(ThemeMode theme, RuntimeThemePreference expected)
    {
        Assert.Equal(expected, RuntimePreferencesPublisher.ToRuntime(theme));
    }

    [Fact]
    public void Publish_WritesTheCurrentThemeAndCulture()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        string directory = Path.Combine(Path.GetTempPath(), "Foundry.Connect.Tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, RuntimePreferencesHandoff.FileName);
        var localization = new ResourceLocalizationService(new ResourceManager("Foundry.Connect.Strings.Resources", typeof(App).Assembly));

        try
        {
            localization.SetCulture(CultureInfo.GetCultureInfo("de-DE"));
            var publisher = new RuntimePreferencesPublisher(new StubThemeService(ThemeMode.Dark), localization, path, new RecordingLogger<RuntimePreferencesPublisher>());

            publisher.Publish();

            Assert.True(RuntimePreferencesHandoff.TryRead(path, out RuntimePreferences? preferences));
            Assert.Equal(new RuntimePreferences(RuntimeThemePreference.Dark, "de-DE"), preferences);
        }
        finally
        {
            localization.SetCulture(original);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Publish_WhenTheFileCannotBeWritten_LogsOneWarningWithoutThrowing()
    {
        string blocker = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
        File.WriteAllText(blocker, "a file, not a folder");
        var logger = new RecordingLogger<RuntimePreferencesPublisher>();
        var localization = new ResourceLocalizationService(new ResourceManager("Foundry.Connect.Strings.Resources", typeof(App).Assembly));

        try
        {
            var publisher = new RuntimePreferencesPublisher(new StubThemeService(ThemeMode.Light), localization, Path.Combine(blocker, "sub", "prefs.json"), logger);

            publisher.Publish();

            Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    private sealed class StubThemeService(ThemeMode theme) : IThemeService
    {
        public ThemeMode CurrentTheme { get; private set; } = theme;

        public void SetTheme(ThemeMode value) => CurrentTheme = value;
    }
}
