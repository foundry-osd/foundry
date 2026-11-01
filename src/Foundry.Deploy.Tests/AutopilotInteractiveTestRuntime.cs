// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Localization;
using Foundry.Utilities.Globalization;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Theme;

namespace Foundry.Deploy.Tests;

/// <summary>Builds Foundry.AutopilotInteractive runtime archives and the UI services the Autopilot step reads.</summary>
internal static class AutopilotInteractiveTestRuntime
{
    /// <summary>Writes a runtime archive under <paramref name="root"/> and returns its path and SHA256.</summary>
    public static (string Path, string Sha256) CreateArchive(string root, bool includeExecutable = true)
    {
        string content = Path.Combine(root, "autopilot-interactive-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(content);
        if (includeExecutable) File.WriteAllText(Path.Combine(content, "Foundry.AutopilotInteractive.exe"), "app");
        File.WriteAllText(Path.Combine(content, "Foundry.AutopilotInteractive.dll"), "library");
        string archive = Path.Combine(root, "autopilot-interactive-" + Guid.NewGuid().ToString("N") + ".zip");
        ZipFile.CreateFromDirectory(content, archive);
        Directory.Delete(content, recursive: true);
        return (archive, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))));
    }

    /// <summary>Creates a media-forwarded origin for a fresh archive, so the Autopilot step installs without network.</summary>
    public static AutopilotInteractiveRuntimeOrigin CreateLocalOrigin(string root)
    {
        (string path, string sha256) = CreateArchive(root);
        return new AutopilotInteractiveRuntimeOrigin(path, sha256, IsLocalArchive: true);
    }

    /// <summary>Theme service that reports a fixed theme.</summary>
    public sealed class FixedTheme(ThemeMode theme) : IThemeService
    {
        public ThemeMode CurrentTheme { get; private set; } = theme;

        public void SetTheme(ThemeMode theme) => CurrentTheme = theme;
    }

    /// <summary>Localization service that reports a fixed culture without changing the test thread culture.</summary>
    public sealed class FixedCulture(string cultureName) : ILocalizationService
    {
        public CultureInfo CurrentCulture { get; } = CultureInfo.GetCultureInfo(cultureName);

        public LocalizedStrings Strings => throw new NotSupportedException();

        public event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged { add { } remove { } }

        public void SetCulture(CultureInfo culture) => throw new NotSupportedException();

        public string GetString(string key) => key;

        public string Format(string key, params object[] args) => key;

        public IReadOnlyList<SupportedCultureOption> CreateSupportedCultureOptions() => [];
    }

    /// <summary>Keyboard layout switcher that reports a fixed active layout and refuses to switch.</summary>
    public sealed class FixedKeyboardLayout(string activeLayoutId) : IKeyboardLayoutSwitcher
    {
        public IReadOnlyList<KeyboardLayout> GetLayouts() => [];

        public string GetActiveLayoutId() => activeLayoutId;

        public KeyboardLayoutSwitchResult Apply(KeyboardLayout layout) => throw new NotSupportedException();

        public KeyboardLayoutSwitchResult? RestoreHandoff() => null;
    }
}
