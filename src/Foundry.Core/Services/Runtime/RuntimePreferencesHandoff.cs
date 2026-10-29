// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Foundry.Utilities.Globalization;

namespace Foundry.Core.Services.Runtime;

/// <summary>Theme chosen in Foundry Connect, as handed to Foundry Deploy.</summary>
public enum RuntimeThemePreference
{
    /// <summary>Follows the Windows theme.</summary>
    System,

    /// <summary>Always uses the light theme.</summary>
    Light,

    /// <summary>Always uses the dark theme.</summary>
    Dark
}

/// <summary>Theme and UI culture handed from one WinPE application to the next.</summary>
/// <param name="Theme">Theme the previous application was running with.</param>
/// <param name="CultureName">Known UI culture name, for example <c>fr-FR</c>.</param>
public sealed record RuntimePreferences(RuntimeThemePreference Theme, string CultureName);

/// <summary>
/// Hands the theme and the UI language chosen in Foundry Connect to Foundry Deploy through a small file on the WinPE RAM drive.
/// </summary>
/// <remarks>
/// The file holds no personal data. Connect writes it once, just before its successful exit; Deploy reads it once before its
/// first window is created. A missing, truncated or foreign file is ignored by the reader, which never throws. The file is
/// overwritten by each successful Connect exit and never deleted, so a later Deploy start reads the last hand-off.
/// </remarks>
public static class RuntimePreferencesHandoff
{
    /// <summary>Name of the hand-off file inside the runtime directory.</summary>
    public const string FileName = "runtime-preferences.json";

    /// <summary>Largest file the reader accepts.</summary>
    public const int MaximumFileBytes = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Default hand-off file on the WinPE RAM drive, shared by Foundry Connect and Foundry Deploy.</summary>
    public static string DefaultPath { get; } = Path.Combine(WinPeWorkspaceLayout.RuntimeDirectory, FileName);

    /// <summary>
    /// Writes the preferences, creating the directory when needed. The file is replaced atomically so a reader never sees a
    /// partial document.
    /// </summary>
    /// <param name="path">Hand-off file path.</param>
    /// <param name="preferences">Preferences to hand over.</param>
    /// <exception cref="ArgumentException">The culture is empty or not a known culture.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    public static void Write(string path, RuntimePreferences preferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!TryCanonicalizeCulture(preferences.CultureName, out string culture))
        {
            throw new ArgumentException("The culture is not a known culture.", nameof(preferences));
        }

        byte[] content = JsonSerializer.SerializeToUtf8Bytes(new Document(ToText(preferences.Theme), culture), JsonOptions);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The write outcome is what matters; a leftover temporary file on the RAM drive is harmless.
            }
        }
    }

    /// <summary>
    /// Reads the preferences. A missing, oversized, malformed or unknown-valued file yields <see langword="false"/>.
    /// </summary>
    /// <param name="path">Hand-off file path.</param>
    /// <param name="preferences">The preferences when the file is valid; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the file holds a known theme and a known culture.</returns>
    public static bool TryRead(string path, [NotNullWhen(true)] out RuntimePreferences? preferences)
    {
        preferences = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            byte[] content = ReadBounded(path);
            if (content.Length > MaximumFileBytes)
            {
                return false;
            }

            Document? document = JsonSerializer.Deserialize<Document>(content, JsonOptions);
            if (document is null || !TryParseTheme(document.Theme, out RuntimeThemePreference theme) || !TryCanonicalizeCulture(document.Culture, out string culture))
            {
                return false;
            }

            preferences = new RuntimePreferences(theme, culture);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[MaximumFileBytes + 1];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = stream.Read(buffer, count, buffer.Length - count);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return buffer[..count];
    }

    private static bool TryCanonicalizeCulture(string? value, out string canonical)
    {
        canonical = CultureCode.Canonicalize(value);
        if (canonical.Length == 0)
        {
            return false;
        }

        try
        {
            canonical = CultureInfo.GetCultureInfo(canonical, predefinedOnly: true).Name;
            return canonical.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string ToText(RuntimeThemePreference theme) => theme switch
    {
        RuntimeThemePreference.System => "system",
        RuntimeThemePreference.Light => "light",
        RuntimeThemePreference.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "The theme is not a known theme.")
    };

    private static bool TryParseTheme(string? value, out RuntimeThemePreference theme)
    {
        theme = RuntimeThemePreference.System;
        if (string.Equals(value, "system", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
        {
            theme = RuntimeThemePreference.Light;
            return true;
        }

        if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
        {
            theme = RuntimeThemePreference.Dark;
            return true;
        }

        return false;
    }

    private sealed record Document(string? Theme, string? Culture);
}
