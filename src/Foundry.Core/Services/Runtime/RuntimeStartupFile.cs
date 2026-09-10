// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Runtime;

namespace Foundry.Core.Services.Runtime;

/// <summary>Bounds file exchange and rejects ambiguous or redirected protocol data.</summary>
internal static class RuntimeStartupFile
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static byte[] Read(string path, int maximumBytes = StartupProtocol.MaximumFileBytes)
    {
        ValidatePath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Startup protocol file exceeds its size limit.");
        var bytes = new byte[maximumBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = stream.Read(bytes.AsSpan(count));
            if (read == 0) break;
            count += read;
        }
        if (count > maximumBytes) throw new InvalidDataException("Startup protocol file exceeds its size limit.");
        return bytes[..count];
    }

    internal static void Write(string path, RuntimeStartupStatus status)
    {
        ValidatePath(path);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(status, JsonOptions);
        if (content.Length > StartupProtocol.MaximumFileBytes) throw new InvalidDataException("Startup protocol file exceeds its size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Startup protocol paths must be absolute local paths.", nameof(path));
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Startup protocol paths must not contain links.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    internal static bool HasUniqueProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) return false;
        }
        return true;
    }

    internal static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or InvalidOperationException;

    internal static bool IsSessionId(string? value) =>
        value is { Length: > 0 and <= 32 } && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    internal static bool IsIdentifier(string? value) =>
        value is { Length: 32 or 36 } && Guid.TryParseExact(value, value.Length == 32 ? "N" : "D", out Guid id) && id != Guid.Empty;

    internal static bool IsApplication(string? value) => value is "Foundry.Connect" or "Foundry.Deploy";

    internal static bool IsFailureCategory(string? value) => value is null ||
        (value is { Length: > 0 and <= 64 } && value.All(character => character is >= 'a' and <= 'z' or '_'));

    internal static int StageOrder(string? stage) => stage switch
    {
        StartupStage.ManagedStarted => 1,
        StartupStage.ConfigurationLoaded => 2,
        StartupStage.UiReady => 3,
        StartupStage.StartupFailed => 4,
        _ => 0
    };
}
