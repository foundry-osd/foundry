// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentExecutionServiceTests
{
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

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext());

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

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext());

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

        DeploymentExecutionRunResult result = await service.ExecuteAsync(CreateContext());

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
        public IReadOnlyList<string> PlannedSteps => [];

        public event EventHandler<DeploymentStepProgress>? StepProgressChanged
        {
            add { }
            remove { }
        }

        public bool WasRun { get; private set; }

        public Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default)
        {
            WasRun = true;
            return Task.FromResult(new DeploymentResult
            {
                IsSuccess = true,
                Message = "Completed"
            });
        }
    }
}
