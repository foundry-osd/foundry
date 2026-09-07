// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text.Json;

namespace Foundry.Core.Services.Media;

/// <summary>
/// Serializes media and ADK work across processes and records ownership before native work begins.
/// An abandoned journal blocks subsequent operations until its native resources are reconciled.
/// </summary>
public sealed class MediaOperationLease : IDisposable
{
    private static readonly List<MediaOperationLease> retainedLeases = [];
    private readonly FileStream gate;
    private readonly string journalPath;
    private readonly string operationKind;
    private bool recoveryRequired;
    private bool disposed;

    private MediaOperationLease(string root, string kind, FileStream gate, Guid operationId)
    {
        this.gate = gate;
        operationKind = kind;
        OperationId = operationId;
        WorkingDirectoryPath = Path.Combine(root, OperationId.ToString("N"));
        journalPath = Path.Combine(root, $"{OperationId:N}.operation.json");
        if (Directory.Exists(WorkingDirectoryPath) || File.Exists(WorkingDirectoryPath))
        {
            throw new IOException("The operation workspace already exists and cannot be reused.");
        }
        WriteJournal("Active", []);
        Directory.CreateDirectory(WorkingDirectoryPath);
    }

    public Guid OperationId { get; }
    public string WorkingDirectoryPath { get; }
    public bool RecoveryRequired => recoveryRequired;

    public static MediaOperationLease Acquire(string workspaceRoot, string operationKind = "Media", Guid? operationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        if (operationId == Guid.Empty) throw new ArgumentException("An operation ID must be nonempty.", nameof(operationId));
        string root = Path.GetFullPath(workspaceRoot);
        ValidateOrdinaryPath(root);
        Directory.CreateDirectory(root);
        ValidateOrdinaryPath(root);
        string lockPath = Path.Combine(root, "media-operation.lock");
        ValidateOrdinaryPath(lockPath);
        var gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            string? abandoned = Directory.EnumerateFiles(root, "*.operation.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (abandoned is not null)
            {
                throw new IOException($"A previous media or ADK operation requires recovery. Review its journal: {abandoned}");
            }

            return new MediaOperationLease(root, operationKind, gate, operationId ?? Guid.NewGuid());
        }
        catch
        {
            gate.Dispose();
            throw;
        }
    }

    /// <summary>Preserves the journal, workspace and machine gate when native ownership is unresolved.</summary>
    public void RetainForRecovery(IEnumerable<string>? retainedPaths = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!recoveryRequired)
        {
            recoveryRequired = true;
            lock (retainedLeases)
            {
                retainedLeases.Add(this);
            }
        }

        WriteJournal("RecoveryRequired", retainedPaths?.ToArray() ?? [WorkingDirectoryPath]);
    }

    /// <summary>Deletes only this lease's workspace after the caller has confirmed native cleanup.</summary>
    public void DeleteOwnedWorkspace()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (recoveryRequired)
        {
            throw new InvalidOperationException("An operation requiring recovery cannot delete its workspace.");
        }

        ValidateOrdinaryPath(WorkingDirectoryPath);
        if (!Directory.Exists(WorkingDirectoryPath))
        {
            return;
        }

        ValidateTree(WorkingDirectoryPath);
        NormalizeOwnedAttributes(WorkingDirectoryPath);
        Directory.Delete(WorkingDirectoryPath, recursive: true);
    }

    public void Dispose()
    {
        if (disposed || recoveryRequired)
        {
            return;
        }

        try
        {
            File.Delete(journalPath);
        }
        finally
        {
            disposed = true;
            gate.Dispose();
        }
    }

    private void WriteJournal(string state, IReadOnlyList<string> paths)
    {
        using Process process = Process.GetCurrentProcess();
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new
        {
            OperationId,
            OperationKind = operationKind,
            ProcessId = process.Id,
            ProcessStartUtc = process.StartTime.ToUniversalTime(),
            State = state,
            WorkingDirectoryPath,
            RetainedPaths = paths
        });
        using var stream = new FileStream(journalPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateTree(string directory)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"The owned workspace contains a reparse point: {entry}");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                ValidateTree(entry);
            }
        }
    }

    private static void NormalizeOwnedAttributes(string directory)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"The owned workspace contains a reparse point: {entry}");
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                NormalizeOwnedAttributes(entry);
            }
            else if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    private static void ValidateOrdinaryPath(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"An operation path must not traverse a reparse point: {current}");
            }
        }
    }
}
