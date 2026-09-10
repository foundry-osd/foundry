// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Models.Runtime;

namespace Foundry.Core.Services.Runtime;

/// <summary>Best-effort child acknowledgements; missing protocol environment leaves standalone startup unchanged.</summary>
public sealed class RuntimeStartupReporter
{
    private readonly object _gate = new();
    private readonly string _application;
    private string? _stage;

    private RuntimeStartupReporter(string path, string sessionId, string launchId, string application)
    {
        StatusPath = path;
        SessionId = sessionId;
        LaunchId = launchId;
        _application = application;
    }

    public string StatusPath { get; }
    public string SessionId { get; }
    public string LaunchId { get; }

    /// <summary>Returns null when no complete, supported launch identity was explicitly supplied by Bootstrap.</summary>
    public static RuntimeStartupReporter? FromEnvironment(string application) =>
        FromEnvironment(application, Environment.GetEnvironmentVariable);

    internal static RuntimeStartupReporter? FromEnvironment(string application, Func<string, string?> readEnvironment)
    {
        try
        {
            string? protocol = readEnvironment(StartupProtocol.ProtocolEnvironmentVariable);
            string? path = readEnvironment(StartupProtocol.StatusPathEnvironmentVariable);
            string? sessionId = readEnvironment(StartupProtocol.SessionIdEnvironmentVariable);
            string? launchId = readEnvironment(StartupProtocol.LaunchIdEnvironmentVariable);
            if (protocol != StartupProtocol.Version.ToString(CultureInfo.InvariantCulture) || path is null ||
                !RuntimeStartupFile.IsSessionId(sessionId) || !RuntimeStartupFile.IsIdentifier(launchId) ||
                !RuntimeStartupFile.IsApplication(application)) return null;
            RuntimeStartupFile.ValidatePath(path);
            return new RuntimeStartupReporter(path, sessionId!, launchId!, application);
        }
        catch (Exception exception) when (RuntimeStartupFile.IsExpectedFailure(exception)) { return null; }
    }

    /// <summary>Atomically publishes a forward stage; failures cannot disrupt the application's startup path.</summary>
    public bool Report(string stage, string? failureCategory = null, string? failureRecordId = null)
    {
        lock (_gate)
        {
            if (RuntimeStartupFile.StageOrder(stage) <= RuntimeStartupFile.StageOrder(_stage)) return false;
            var status = new RuntimeStartupStatus
            {
                ProtocolVersion = StartupProtocol.Version,
                SessionId = SessionId,
                LaunchId = LaunchId,
                Application = _application,
                ProcessId = Environment.ProcessId,
                Stage = stage,
                TimestampUtc = DateTimeOffset.UtcNow,
                FailureCategory = failureCategory,
                FailureRecordId = failureRecordId
            };
            if (!RuntimeStartupStatusReader.IsValid(status)) return false;
            try
            {
                RuntimeStartupFile.Write(StatusPath, status);
                _stage = stage;
                return true;
            }
            catch (Exception exception) when (RuntimeStartupFile.IsExpectedFailure(exception)) { return false; }
        }
    }
}
