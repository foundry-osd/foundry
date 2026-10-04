// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.PreOobe;

namespace Foundry.PostInstall.Execution;

/// <summary>A validated plan and the digest of its one owned serialized snapshot.</summary>
internal sealed record PreOobePlanSnapshot(PreOobeExecutionPlan Plan, string Hash);

/// <summary>Loads only the fixed installed-Windows plan, with a bounded read under a non-writable file share.</summary>
internal static class PreOobePlanLoader
{
    public static async Task<PreOobePlanSnapshot> LoadAsync(string root, CancellationToken token)
    {
        byte[] bytes = await ReadBoundedAsync(OwnedPaths.Resolve(root, "State/PreOobe/plan.json"), 8 * 1024 * 1024, token).ConfigureAwait(false);
        var plan = JsonSerializer.Deserialize<PreOobeExecutionPlan>(bytes, ExecutionJournal.JsonOptions)
            ?? throw new InvalidDataException("The plan is empty.");
        PreOobePlanValidator.ValidatePlan(plan);
        return new(plan, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public static void RequireMatching(PreOobePlanSnapshot snapshot, PreOobeExecutionPlan plan, string hash)
    {
        DomainJoinBinding.ValidateHash(hash);
        if (snapshot.Hash != hash || !JsonSerializer.SerializeToUtf8Bytes(snapshot.Plan, ExecutionJournal.JsonOptions).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(plan, ExecutionJournal.JsonOptions)))
            throw new InvalidDataException("The fixed plan snapshot does not match this operation.");
    }

    internal static async Task<byte[]> ReadBoundedAsync(string path, int maximum, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximum) throw new InvalidDataException("The owned file size is invalid.");
        byte[] bytes = new byte[(int)stream.Length];
        try { await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false); return bytes; }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }
}
