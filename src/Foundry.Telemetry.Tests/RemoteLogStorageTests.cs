// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry.Tests;

public sealed class RemoteLogStorageTests
{
    [Fact]
    public void MatchingBootstrapCache_IsStableAcrossBootSessions()
    {
        RemoteDiagnosticsContext context = RemoteDiagnosticsTestData.Context();
        string first = RemoteLogStorage.ResolveRoot(@"X:\Logs\PendingLogs", context, @"R:\Logs\session-1");
        string second = RemoteLogStorage.ResolveRoot(@"X:\Logs\PendingLogs", context with { SessionId = "session-2" }, @"R:\Logs\session-2");
        Assert.Equal(@"R:\Logs\PendingLogs", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void UnrelatedInheritedCache_DoesNotRedirectTheOutbox()
    {
        Assert.Equal(@"X:\Logs\PendingLogs", RemoteLogStorage.ResolveRoot(@"X:\Logs\PendingLogs",
            RemoteDiagnosticsTestData.Context(), @"R:\Logs\another-session"));
    }
}
