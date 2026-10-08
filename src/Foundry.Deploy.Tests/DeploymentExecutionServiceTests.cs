// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Foundry.Deploy.Services.DomainJoin;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentExecutionServiceTests
{
    [Theory]
    [InlineData("denied")]
    [InlineData("canceled")]
    [InlineData("failed")]
    [InlineData("success")]
    public async Task ExecuteAsync_DisposesDomainInputOnEveryExit(string exit)
    {
        using var cancellation = new CancellationTokenSource();
        if (exit == "canceled") cancellation.Cancel();
        using var keys = new DeploymentSecretKeySession();
        var orchestrator = new RecordingOrchestrator
        {
            Run = _ => exit == "failed" ? Task.FromException<DeploymentResult>(new InvalidOperationException("failure")) :
                Task.FromResult(new DeploymentResult { IsSuccess = true, Message = "Completed" })
        };
        var service = new DeploymentExecutionService(orchestrator, new FakeConfigurationService(exit == "denied"), keys,
            NullLogger<DeploymentExecutionService>.Instance);
        var input = new DomainJoinPreparedInput(new("corp.test", "CORP\\join"), "TEST-PC", null, "secret".AsSpan());
        ReadOnlyMemory<char> password = input.Password;
        DeploymentContext request = CreateContext() with
        {
            DomainJoinRequest = new(DomainJoinDeploymentDisposition.Ready),
            DomainJoinIntent = new("corp.test", "TEST-PC", null)
        };

        DeploymentExecutionRunResult result = await service.ExecuteAsync(request, input, cancellation.Token);

        Assert.Equal(exit == "success", result.IsSuccess);
        Assert.All(password.ToArray(), value => Assert.Equal('\0', value));
        Assert.DoesNotContain("secret", result.Message ?? "");
        Assert.DoesNotContain("CORP", result.Message ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsCallerTokenAndCancelledOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        var orchestrator = new RecordingOrchestrator
        {
            Result = new DeploymentResult { IsSuccess = false, IsCancelled = true, Message = "Cancelled", LogsDirectoryPath = "logs" }
        };
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentExecutionService(orchestrator, new FakeConfigurationService(false), session, NullLogger<DeploymentExecutionService>.Instance);

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext(), cancellation.Token);

        Assert.Equal(cancellation.Token, orchestrator.ReceivedToken);
        Assert.True(result.IsCancelled);
        Assert.False(result.IsSuccess);
        Assert.Equal("logs", result.LogsDirectoryPath);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCancelled_DoesNotRunOrchestrator()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var orchestrator = new RecordingOrchestrator();
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentExecutionService(orchestrator, new FakeConfigurationService(false), session, NullLogger<DeploymentExecutionService>.Instance);

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext(), cancellation.Token);

        Assert.True(result.IsCancelled);
        Assert.False(orchestrator.WasRun);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ExecuteAsync_ClassifiesEscapingCancellationAndTimeout(bool callerCancelled, bool timeout)
    {
        using var cancellation = new CancellationTokenSource();
        var orchestrator = new RecordingOrchestrator
        {
            Run = token =>
            {
                if (callerCancelled)
                {
                    cancellation.Cancel();
                }

                return Task.FromException<DeploymentResult>(timeout
                    ? new TimeoutException("Transfer timed out.")
                    : new OperationCanceledException(token));
            }
        };
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentExecutionService(orchestrator, new FakeConfigurationService(false), session, NullLogger<DeploymentExecutionService>.Instance);

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext(), cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(callerCancelled && !timeout, result.IsCancelled);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProtectedSessionIsLocked_DoesNotRunOrchestrator()
    {
        var orchestrator = new RecordingOrchestrator();
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentExecutionService(
            orchestrator,
            new FakeConfigurationService(isProtected: true),
            session,
            NullLogger<DeploymentExecutionService>.Instance);

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.False(orchestrator.WasRun);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProtectionIsDisabled_RunsOrchestrator()
    {
        var orchestrator = new RecordingOrchestrator();
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentExecutionService(
            orchestrator,
            new FakeConfigurationService(isProtected: false),
            session,
            NullLogger<DeploymentExecutionService>.Instance);

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(orchestrator.WasRun);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabledFlagIsClearedButWrappedKeyRemains_DoesNotRunOrchestrator()
    {
        var orchestrator = new RecordingOrchestrator();
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentExecutionService(
            orchestrator,
            new FakeConfigurationService(isProtected: false, hasWrappedKey: true),
            session,
            NullLogger<DeploymentExecutionService>.Instance);

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.False(orchestrator.WasRun);
    }

    private static DeploymentContext CreateContext() => new()
    {
        Mode = DeploymentMode.Iso,
        CacheRootPath = "X:\\Cache",
        TargetDiskNumber = 1,
        TargetComputerName = "TEST-PC",
        OperatingSystem = new OperatingSystemCatalogItem(),
        DriverPackSelectionKind = DriverPackSelectionKind.None
    };

    private sealed class FakeConfigurationService(bool isProtected, bool hasWrappedKey = false) : IDeployConfigurationService
    {
        public DeployConfigurationLoadResult LoadOptional() => new()
        {
            ConfigurationPath = string.Empty,
            Exists = true,
            Document = new FoundryDeployConfigurationDocument
            {
                Protection = new DeployProtectionSettings
                {
                    IsEnabled = isProtected,
                    ProtectedDeploymentKey = hasWrappedKey
                        ? new SecretEnvelope { Ciphertext = "wrapped" }
                        : new SecretEnvelope()
                }
            }
        };
    }

    private sealed class RecordingOrchestrator : IDeploymentOrchestrator
    {
        public event EventHandler<DeploymentStepProgress>? StepProgressChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? CompletionStarting
        {
            add { }
            remove { }
        }

        public bool WasRun { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }
        public DeploymentResult Result { get; init; } = new() { IsSuccess = true, Message = "Completed" };
        public Func<CancellationToken, Task<DeploymentResult>>? Run { get; init; }

        public Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default)
        {
            WasRun = true;
            ReceivedToken = cancellationToken;
            return Run?.Invoke(cancellationToken) ?? Task.FromResult(Result);
        }

        public Task<DeploymentResult> RunAsync(DeploymentContext context, DomainJoinPreparedInput? input, CancellationToken cancellationToken = default) =>
            RunAsync(context, cancellationToken);
    }
}
