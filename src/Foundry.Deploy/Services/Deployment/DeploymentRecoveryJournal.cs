// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Persists unresolved deployment resource ownership until explicit recovery.</summary>
internal sealed class DeploymentRecoveryJournal
{
    private const int MaximumBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<RecoveryResourceState>(allowIntegerValues: false) }
    };
    private readonly string _path;

    public DeploymentRecoveryJournal(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot)) throw new ArgumentException("An absolute deployment workspace is required.", nameof(workspaceRoot));
        _path = Path.Combine(Path.GetFullPath(workspaceRoot), "State", "deployment-recovery.json");
    }

    /// <summary>Returns unresolved ownership; an invalid existing journal blocks rather than acting as absence.</summary>
    public RecoveryResourceDiagnostic? Read()
    {
        ValidatePath(_path);
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length is < 1 or > MaximumBytes) throw new InvalidDataException();
            byte[] bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            if (stream.Length != length) throw new InvalidDataException();
            var diagnostic = JsonSerializer.Deserialize<RecoveryResourceDiagnostic>(bytes, Options);
            ValidateDiagnostic(diagnostic);
            return diagnostic;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new IOException($"Deployment recovery state could not be validated. Keep the workspace and review '{_path}' before continuing.");
        }
    }

    /// <summary>Publishes complete recovery evidence atomically without replacing another unresolved record.</summary>
    public void Write(RecoveryResourceDiagnostic diagnostic)
    {
        ValidateDiagnostic(diagnostic);
        RecoveryResourceDiagnostic? existing = Read();
        if (existing is not null)
        {
            if (existing == diagnostic) return;
            throw new IOException($"Existing deployment recovery evidence must be reconciled before replacing '{_path}'.");
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(diagnostic, Options);
        if (bytes.Length > MaximumBytes) throw new IOException("Deployment recovery evidence exceeds its supported size.");
        string directory = Path.GetDirectoryName(_path)!;
        ValidatePath(_path);
        Directory.CreateDirectory(directory);
        ValidatePath(_path);
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".recovery.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            ValidatePath(_path);
            File.Move(temporary, _path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ValidateDiagnostic(RecoveryResourceDiagnostic? diagnostic)
    {
        if (diagnostic is null || diagnostic.State is not (RecoveryResourceState.Mounted or RecoveryResourceState.Unknown or RecoveryResourceState.RecoveryRequired) ||
            !IsBounded(diagnostic.ResourceKind, 64) || !IsBounded(diagnostic.ResourcePath, 2048) || !IsBounded(diagnostic.Reason, 1024) ||
            (diagnostic.ImagePath is not null && !IsBounded(diagnostic.ImagePath, 2048)))
            throw new IOException("Deployment recovery evidence is incomplete or unsupported.");
    }

    private static bool IsBounded(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    /// <summary>Rejects existing reparse ancestors without following or enumerating a directory tree.</summary>
    internal static void ValidatePath(string path, Func<string, FileAttributes>? attributes = null)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if (((attributes ?? File.GetAttributes)(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Deployment recovery storage crosses a reparse point. Keep the workspace for manual recovery.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
