// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class BootstrapTelemetryScopeTests
{
    [Theory]
    [InlineData("https://eu.i.posthog.com")]
    [InlineData("HTTPS://EU.I.POSTHOG.COM/")]
    [InlineData("https://eu.i.posthog.com:443/")]
    public void DefaultDestination_RetainsExistingJournalScope(string host)
    {
        Assert.Equal("22B4F042FC6670D76724B891A8758D01C209281566C0A68B28FAA485D95108E4", BootstrapTelemetryJournal.Scope(host, "public", "install"));
    }

    [Theory]
    [InlineData("https://User:Password@example.test/ingest?Key=Value#Fragment")]
    [InlineData("https://User:Password@example.test/Ingest?key=Value#Fragment")]
    [InlineData("https://User:Password@example.test/Ingest?Key=value#Fragment")]
    [InlineData("https://User:Password@example.test/Ingest?Key=Value#fragment")]
    [InlineData("https://user:Password@example.test/Ingest?Key=Value#Fragment")]
    [InlineData("https://User:password@example.test/Ingest?Key=Value#Fragment")]
    public void CaseSensitiveDestinationParts_KeepSeparateJournalScopes(string other)
    {
        const string original = "https://User:Password@example.test/Ingest?Key=Value#Fragment";
        Assert.NotEqual(BootstrapTelemetryJournal.Scope(original, "public", "install"),
            BootstrapTelemetryJournal.Scope(other, "public", "install"));
    }

    [Fact]
    public void HostCaseAndTrailingPathSlash_DoNotChangeDestinationScope()
    {
        Assert.Equal(BootstrapTelemetryJournal.Scope("https://example.test/Ingest?Key=Value", "public", "install"),
            BootstrapTelemetryJournal.Scope("HTTPS://EXAMPLE.TEST/Ingest/?Key=Value", "public", "install"));
    }
}
