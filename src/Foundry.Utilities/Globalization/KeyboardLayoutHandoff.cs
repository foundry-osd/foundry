// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Persists the keyboard layout chosen in one WinPE application so the next application can re-apply it at startup.
/// </summary>
public static class KeyboardLayoutHandoff
{
    /// <summary>
    /// Default hand-off file on the WinPE RAM drive, shared by Foundry Connect and Foundry Deploy.
    /// </summary>
    public const string DefaultPath = @"X:\Foundry\Runtime\keyboard-layout.txt";

    /// <summary>
    /// Writes a KLID to the hand-off file, creating its directory when needed.
    /// </summary>
    /// <param name="path">Hand-off file path.</param>
    /// <param name="layoutId">KLID to persist.</param>
    /// <exception cref="ArgumentException">The KLID is not valid.</exception>
    public static void Write(string path, string layoutId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!KeyboardLayoutCatalog.TryNormalizeId(layoutId, out string id))
        {
            throw new ArgumentException("The keyboard layout identifier is not valid.", nameof(layoutId));
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, id);
    }

    /// <summary>
    /// Reads the KLID from the hand-off file.
    /// </summary>
    /// <param name="path">Hand-off file path.</param>
    /// <param name="layoutId">Normalized KLID when the file holds a valid one; otherwise an empty string.</param>
    /// <returns><see langword="true"/> when the file exists and holds a valid KLID.</returns>
    public static bool TryRead(string path, out string layoutId)
    {
        layoutId = string.Empty;
        try
        {
            return File.Exists(path) && KeyboardLayoutCatalog.TryNormalizeId(File.ReadAllText(path), out layoutId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            layoutId = string.Empty;
            return false;
        }
    }
}
