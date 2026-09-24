// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Foundry.Core.Services.WinPe;

/// <summary>Owns an authoring operation before Copype starts and until its final consumer finishes.</summary>
public sealed class WinPeWorkspaceLease : IDisposable
{
    internal const string LeaseFileName = ".lease";
    internal const string OwnershipFileName = "operation.json";
    private readonly FileStream lease;

    private WinPeWorkspaceLease(string operationDirectoryPath, FileStream lease)
    {
        OperationDirectoryPath = operationDirectoryPath;
        this.lease = lease;
    }

    /// <summary>Gets the operation root, which owns every mutable media-preparation artifact.</summary>
    public string OperationDirectoryPath { get; }

    /// <summary>Gets the not-yet-created path passed to Copype.</summary>
    public string WinPeDirectoryPath => Path.Combine(OperationDirectoryPath, "WinPe");

    /// <summary>Creates an identified operation and holds its lease before external work begins.</summary>
    public static WinPeWorkspaceLease Create(string workspaceRoot)
    {
        string id = Guid.NewGuid().ToString("N");
        string path = Path.Combine(Path.GetFullPath(workspaceRoot), id);
        Directory.CreateDirectory(path);
        var stream = new FileStream(Path.Combine(path, LeaseFileName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);
        try
        {
            File.WriteAllText(Path.Combine(path, OwnershipFileName), JsonSerializer.Serialize(new Ownership(id, "Foundry.OSD.Media")));
            return new WinPeWorkspaceLease(path, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static bool IsOwned(string operationPath)
    {
        string id = Path.GetFileName(operationPath);
        if (!Guid.TryParseExact(id, "N", out _)) return false;
        Ownership? ownership = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(Path.Combine(operationPath, OwnershipFileName)));
        return ownership is { Kind: "Foundry.OSD.Media" } && ownership.Id == id;
    }

    /// <summary>Releases operation ownership after the final image/USB/ISO consumer has finished.</summary>
    public void Dispose() => lease.Dispose();

    private sealed record Ownership(string Id, string Kind);
}
