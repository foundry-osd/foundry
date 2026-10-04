// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.PreOobe;

namespace Foundry.PostInstall.Execution;

public sealed class JournalState
{
    public int SchemaVersion { get; set; } = 1;
    public string OperationId { get; set; } = string.Empty;
    public string AttemptId { get; set; } = string.Empty;
    public string PlanHash { get; set; } = string.Empty;
    public long Generation { get; set; }
    public int Cursor { get; set; }
    public int Substep { get; set; }
    public string Status { get; set; } = "Pending";
    public string? BootIdentity { get; set; }
    public int RestartCount { get; set; }
    public bool DeferredRestart { get; set; }
    public string? DomainRestartBootIdentity { get; set; }
    public long? DomainReceiptGeneration { get; set; }
    public Dictionary<string, PreOobeActionResult> Actions { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> PayloadDispositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? UnsafePayloadBootIdentity { get; set; }
    public string? UnsafeActionId { get; set; }
    public bool HasWarnings { get; set; }
    public string? CompletionStatus { get; set; }
}

public class ExecutionJournal
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 32
    };
    public string Path { get; }
    private readonly string _stateRoot;

    public ExecutionJournal(string root)
    {
        _stateRoot = OwnedPaths.Resolve(root, @"State\PreOobe");
        Path = System.IO.Path.Combine(_stateRoot, "execution-result.json");
    }

    public IDisposable AcquireLease() => new FileStream(System.IO.Path.Combine(_stateRoot, "runner.lease"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    public static JournalState CreateSeed(PreOobeExecutionPlan plan, string hash) => new()
    {
        OperationId = plan.OperationId,
        AttemptId = plan.AttemptId,
        PlanHash = hash
    };

    public void Seed(PreOobeExecutionPlan plan, string hash)
    {
        Directory.CreateDirectory(_stateRoot);
        using var file = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(JsonSerializer.SerializeToUtf8Bytes(CreateSeed(plan, hash), JsonOptions));
        file.Flush(true);
    }

    public JournalState Read()
    {
        if (!File.Exists(Path) || new FileInfo(Path).Length > 8 * 1024 * 1024)
            throw new InvalidDataException("The seeded journal is missing or invalid.");
        return JsonSerializer.Deserialize<JournalState>(File.ReadAllBytes(Path), JsonOptions)
            ?? throw new InvalidDataException("The journal is empty.");
    }

    public virtual void Write(JournalState state)
    {
        state.Generation = checked(state.Generation + 1);
        string temporary = System.IO.Path.Combine(_stateRoot, ".journal-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
                file.Flush(true);
            }
            // Missing primary state is never recreated or rolled back from an older generation.
            File.Replace(temporary, Path, null);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }
}
