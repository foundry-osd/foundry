// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentStartupRecoveryTests
{
    [Fact]
    public async Task ExistingJournal_SkipsOfflineHiveRead()
    {
        using var fixture = new Fixture();
        new DeploymentRecoveryJournal(fixture.Root).Write(fixture.Diagnostic);
        var names = new Names();
        Assert.Equal("FALLBACK", await Resolve(names, fixture.Root));
        Assert.Equal(0, names.Calls);
    }

    [Fact]
    public async Task UnresolvedHive_IsPersistedBeforeReturningFallback()
    {
        using var fixture = new Fixture();
        var failure = new InvalidOperationException("synthetic hive cleanup failure");
        failure.Data["FoundryRecoveryDiagnostic"] = fixture.Diagnostic;
        var names = new Names { Failure = failure };
        Assert.Equal("FALLBACK", await Resolve(names, fixture.Root));
        Assert.Equal(fixture.Diagnostic, new DeploymentRecoveryJournal(fixture.Root).Read());
    }

    [Fact]
    public async Task Cancellation_IsNotReplacedByFallbackName()
    {
        using var fixture = new Fixture();
        var failure = new OperationCanceledException();
        Assert.Same(failure, await Record.ExceptionAsync(() => Resolve(new Names { Failure = failure }, fixture.Root)));
    }

    [Fact]
    public async Task RecoveryPersistenceFailure_PreventsFallback()
    {
        using var fixture = new Fixture();
        var failure = new InvalidOperationException("synthetic hive cleanup failure");
        failure.Data["FoundryRecoveryDiagnostic"] = fixture.Diagnostic;
        var names = new Names
        {
            Failure = failure,
            BeforeFailure = () => File.WriteAllText(Path.Combine(fixture.Root, "State"), "blocks journal directory")
        };
        Assert.NotNull(await Record.ExceptionAsync(() => Resolve(names, fixture.Root)));
    }

    private static Task<string> Resolve(Names names, string root) => DeploymentStartupCoordinator.ResolveComputerNameAsync(
        names, "FALLBACK", root, NullLogger.Instance);

    private sealed class Names : IOfflineWindowsComputerNameService
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public Action? BeforeFailure { get; init; }
        public Task<string?> TryGetOfflineComputerNameAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            BeforeFailure?.Invoke();
            return Failure is null ? Task.FromResult<string?>("EXISTING") : Task.FromException<string?>(Failure);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FoundryStartupRecoveryTests", Guid.NewGuid().ToString("N"));
        public RecoveryResourceDiagnostic Diagnostic => new(RecoveryResourceState.RecoveryRequired, "RegistryHive", "HKLM\\Foundry_test",
            Path.Combine(Root, "SYSTEM"), "hive_cleanup_unresolved");
        public Fixture() { Directory.CreateDirectory(Root); }
        public void Dispose() { Directory.Delete(Root, true); }
    }
}
