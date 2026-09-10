// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Runtime;

namespace Foundry.Core.Services.Runtime;

/// <summary>Accepts only forward acknowledgements belonging to the expected child process and launch.</summary>
public sealed class RuntimeStartupStatusReader(string path, string sessionId, string launchId, string application, int processId)
{
    /// <summary>Gets the last accepted acknowledgement, retained when a read is invalid or incomplete.</summary>
    public RuntimeStartupStatus? LastStatus { get; private set; }

    /// <summary>Returns a new acknowledgement, or null for unchanged, stale, malformed, or backward records.</summary>
    public RuntimeStartupStatus? Read()
    {
        RuntimeStartupStatus? status = ReadValidated(path);
        if (status is null || status.SessionId != sessionId || status.LaunchId != launchId ||
            status.Application != application || status.ProcessId != processId ||
            RuntimeStartupFile.StageOrder(status.Stage) <= RuntimeStartupFile.StageOrder(LastStatus?.Stage))
            return null;
        LastStatus = status;
        return status;
    }

    /// <summary>
    /// Reads a bounded, structurally valid record. Recovery callers must separately establish launch and directory ownership.
    /// Wall-clock age is not used because WinPE can correct its clock during the session.
    /// </summary>
    public static RuntimeStartupStatus? ReadValidated(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(RuntimeStartupFile.Read(path));
            if (!RuntimeStartupFile.HasUniqueProperties(document.RootElement)) return null;
            RuntimeStartupStatus? status = document.Deserialize<RuntimeStartupStatus>(RuntimeStartupFile.JsonOptions);
            return status is not null && IsValid(status) ? status : null;
        }
        catch (Exception exception) when (RuntimeStartupFile.IsExpectedFailure(exception)) { return null; }
    }

    internal static bool IsValid(RuntimeStartupStatus status) =>
        status.ProtocolVersion == StartupProtocol.Version && RuntimeStartupFile.IsSessionId(status.SessionId) &&
        RuntimeStartupFile.IsIdentifier(status.LaunchId) && RuntimeStartupFile.IsApplication(status.Application) &&
        status.ProcessId > 0 && RuntimeStartupFile.StageOrder(status.Stage) > 0 &&
        status.TimestampUtc != default && status.TimestampUtc.Offset == TimeSpan.Zero &&
        RuntimeStartupFile.IsFailureCategory(status.FailureCategory) &&
        (status.FailureRecordId is null || RuntimeStartupFile.IsIdentifier(status.FailureRecordId)) &&
        (status.Stage == StartupStage.StartupFailed || (status.FailureCategory is null && status.FailureRecordId is null));
}
