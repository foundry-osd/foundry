// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;
using SecretEnvelope = Foundry.Core.Models.Configuration.SecretEnvelope;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentAccessGateTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(6, 5)]
    public void RetryDelay_IsProgressiveAndCapped(int failedAttemptNumber, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), DeploymentAccessRetryDelay.GetDelay(failedAttemptNumber));
    }

    [Fact]
    public async Task AuthorizeAsync_WhenProtectionIsDisabled_DoesNotPrompt()
    {
        var dialog = new FakePasswordDialogService();
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument()),
            new FakeUnlockService(),
            dialog,
            new ImmediateRetryDelay(),
            initialDeploymentKey: null,
            new DeploymentSecretKeySession(),
            NullLogger<DeploymentAccessGate>.Instance);

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.True(authorized);
        Assert.Equal(0, dialog.PromptCount);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenEnabledFlagIsClearedButWrappedKeyRemains_StillPrompts()
    {
        var dialog = new FakePasswordDialogService("correct");
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument
            {
                Protection = CreateWrappedProtection(isEnabled: false)
            }),
            new FakeUnlockService("correct"),
            dialog,
            new ImmediateRetryDelay(),
            initialDeploymentKey: null,
            new DeploymentSecretKeySession(),
            NullLogger<DeploymentAccessGate>.Instance);

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.True(authorized);
        Assert.Equal(1, dialog.PromptCount);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenEncryptedProfileRemainsWithoutProtectionMetadata_DeniesAccess()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-access-gate-{Guid.NewGuid():N}");
        string configurationPath = Path.Combine(root, "Config", "foundry.winpe.config.json");
        string profilePath = Path.Combine(root, "Config", "Autopilot", "Profile", "AutopilotConfigurationFile.json.encrypted");
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        await File.WriteAllTextAsync(profilePath, "encrypted", TestContext.Current.CancellationToken);
        var dialog = new FakePasswordDialogService("unused");
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument(), configurationPath: configurationPath),
            new FakeUnlockService(),
            dialog,
            new ImmediateRetryDelay(),
            initialDeploymentKey: null,
            new DeploymentSecretKeySession(),
            NullLogger<DeploymentAccessGate>.Instance);

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.False(authorized);
        Assert.Equal(0, dialog.PromptCount);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenUnprotectedMediaLacksTheDeploymentKeyFile_DoesNotPrompt()
    {
        // Protection comes from the protection block only: a missing key file no longer marks the media as protected.
        string root = Path.Combine(Path.GetTempPath(), $"foundry-access-gate-{Guid.NewGuid():N}");
        string configurationPath = Path.Combine(root, "Config", "foundry.winpe.config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configurationPath)!);
        var dialog = new FakePasswordDialogService("unused");
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument(), configurationPath: configurationPath),
            new FakeUnlockService(),
            dialog,
            new ImmediateRetryDelay(),
            initialDeploymentKey: null,
            new DeploymentSecretKeySession(),
            NullLogger<DeploymentAccessGate>.Instance);

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.True(authorized);
        Assert.Equal(0, dialog.PromptCount);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenConfigurationIsMissingButEncryptedProfileRemains_DeniesAccess()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-access-gate-{Guid.NewGuid():N}");
        string configurationPath = Path.Combine(root, "Config", "foundry.winpe.config.json");
        string profilePath = Path.Combine(root, "Config", "Autopilot", "Profile", "AutopilotConfigurationFile.json.encrypted");
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        await File.WriteAllTextAsync(profilePath, "encrypted", TestContext.Current.CancellationToken);
        var dialog = new FakePasswordDialogService("unused");
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(document: null, exists: false, configurationPath: configurationPath),
            new FakeUnlockService(),
            dialog,
            new ImmediateRetryDelay(),
            initialDeploymentKey: null,
            new DeploymentSecretKeySession(),
            NullLogger<DeploymentAccessGate>.Instance);

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.False(authorized);
        Assert.Equal(0, dialog.PromptCount);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenPasswordFails_AllowsRetryUntilSuccessful()
    {
        var dialog = new FakePasswordDialogService("wrong", "correct");
        var unlock = new FakeUnlockService("correct");
        var retryDelay = new ImmediateRetryDelay();
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument
            {
                Protection = new DeployProtectionSettings { IsEnabled = true }
            }),
            unlock,
            dialog,
            retryDelay,
            initialDeploymentKey: null,
            new DeploymentSecretKeySession(),
            NullLogger<DeploymentAccessGate>.Instance);

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.True(authorized);
        Assert.Equal(2, dialog.PromptCount);
        Assert.Equal([false, true], dialog.PreviousAttemptFailedValues);
        Assert.Equal([1], retryDelay.Attempts);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenPromptIsCancelled_DeniesAccess()
    {
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument
            {
                Protection = new DeployProtectionSettings { IsEnabled = true }
            }),
            new FakeUnlockService(),
            new FakePasswordDialogService(),
            new ImmediateRetryDelay(),
            initialDeploymentKey: null,
            new DeploymentSecretKeySession(),
            NullLogger<DeploymentAccessGate>.Instance);

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.False(authorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenExistingConfigurationCannotBeParsed_DeniesAccess()
    {
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(document: null, exists: true),
            new FakeUnlockService(),
            new FakePasswordDialogService("unused"),
            new ImmediateRetryDelay(),
            initialDeploymentKey: null,
            new DeploymentSecretKeySession(),
            NullLogger<DeploymentAccessGate>.Instance);

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.False(authorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenInitialKeyPassesKeyCheck_UnlocksWithoutPromptAndConsumesTheTransfer()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected("correct horse battery staple");
        using DeploymentKeyTransfer transfer = DeploymentKeyTransfer.FromKey(material.DeploymentKey);
        var dialog = new FakePasswordDialogService();
        using var session = new DeploymentSecretKeySession();
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument
            {
                Protection = new DeployProtectionSettings { IsEnabled = true, KeyCheck = material.Settings.KeyCheck }
            }),
            new FakeUnlockService(),
            dialog,
            new ImmediateRetryDelay(),
            transfer,
            session,
            NullLogger<DeploymentAccessGate>.Instance);

        Assert.True(await gate.AuthorizeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, dialog.PromptCount);
        Assert.True(session.IsUnlocked);
        Assert.Equal(material.DeploymentKey, session.GetKeyCopy());
        Assert.False(transfer.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public async Task AuthorizeAsync_WhenInitialKeyFailsKeyCheck_PromptsAndDisposesTheTransfer()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected("correct horse battery staple");
        using DeploymentKeyTransfer transfer = DeploymentKeyTransfer.FromKey(Enumerable.Repeat((byte)7, DeploymentKeyTransfer.KeyLength).ToArray());
        var dialog = new FakePasswordDialogService();
        using var session = new DeploymentSecretKeySession();
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument
            {
                Protection = new DeployProtectionSettings { IsEnabled = true, KeyCheck = material.Settings.KeyCheck }
            }),
            new FakeUnlockService("correct"),
            dialog,
            new ImmediateRetryDelay(),
            transfer,
            session,
            NullLogger<DeploymentAccessGate>.Instance);

        // The prompt is cancelled: nothing but the rejected key could have unlocked the session.
        Assert.False(await gate.AuthorizeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, dialog.PromptCount);
        Assert.False(session.IsUnlocked);
        Assert.False(transfer.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public async Task AuthorizeAsync_WithoutInitialKey_PromptsAsBefore()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected("correct horse battery staple");
        var dialog = new FakePasswordDialogService("correct");
        using var session = new DeploymentSecretKeySession();
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument
            {
                Protection = new DeployProtectionSettings { IsEnabled = true, KeyCheck = material.Settings.KeyCheck }
            }),
            new FakeUnlockService("correct"),
            dialog,
            new ImmediateRetryDelay(),
            initialDeploymentKey: null,
            session,
            NullLogger<DeploymentAccessGate>.Instance);

        Assert.True(await gate.AuthorizeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, dialog.PromptCount);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenKeyCheckIsMissing_IgnoresTheInitialKey()
    {
        using DeploymentKeyTransfer transfer = DeploymentKeyTransfer.FromKey(Enumerable.Repeat((byte)7, DeploymentKeyTransfer.KeyLength).ToArray());
        var dialog = new FakePasswordDialogService("correct");
        using var session = new DeploymentSecretKeySession();
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument { Protection = new DeployProtectionSettings { IsEnabled = true } }),
            new FakeUnlockService("correct"),
            dialog,
            new ImmediateRetryDelay(),
            transfer,
            session,
            NullLogger<DeploymentAccessGate>.Instance);

        Assert.True(await gate.AuthorizeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, dialog.PromptCount);
        Assert.False(session.IsUnlocked);
        Assert.False(transfer.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public async Task AuthorizeAsync_WhenProtectionIsDisabled_DisposesTheUnusedInitialKey()
    {
        using DeploymentKeyTransfer transfer = DeploymentKeyTransfer.FromKey(new byte[DeploymentKeyTransfer.KeyLength]);
        using var session = new DeploymentSecretKeySession();
        var gate = new DeploymentAccessGate(
            new FakeConfigurationService(new FoundryDeployConfigurationDocument()),
            new FakeUnlockService(),
            new FakePasswordDialogService(),
            new ImmediateRetryDelay(),
            transfer,
            session,
            NullLogger<DeploymentAccessGate>.Instance);

        Assert.True(await gate.AuthorizeAsync(TestContext.Current.CancellationToken));
        Assert.False(session.IsUnlocked);
        Assert.False(transfer.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    private sealed class FakeConfigurationService(
        FoundryDeployConfigurationDocument? document,
        bool exists = true,
        string configurationPath = "") : IDeployConfigurationService
    {
        public DeployConfigurationLoadResult LoadOptional() => new()
        {
            ConfigurationPath = configurationPath,
            Exists = exists,
            Document = document
        };
    }

    private static DeployProtectionSettings CreateWrappedProtection(bool isEnabled) => new()
    {
        IsEnabled = isEnabled,
        KeyDerivationAlgorithm = "pbkdf2-sha256",
        Iterations = 600_000,
        Salt = "salt",
        ProtectedDeploymentKey = new SecretEnvelope
        {
            Kind = "encrypted",
            Algorithm = "aes-gcm-v1",
            KeyId = "deployment-password",
            Nonce = "nonce",
            Tag = "tag",
            Ciphertext = "ciphertext"
        }
    };

    private sealed class FakeUnlockService(string acceptedPassword = "") : IDeploymentProtectionUnlockService
    {
        public bool TryUnlock(DeployProtectionSettings settings, ReadOnlySpan<char> password)
        {
            return password.SequenceEqual(acceptedPassword);
        }
    }

    private sealed class FakePasswordDialogService(params string[] passwords) : IDeploymentPasswordDialogService
    {
        private readonly Queue<string> remainingPasswords = new(passwords);

        public int PromptCount { get; private set; }

        public List<bool> PreviousAttemptFailedValues { get; } = [];

        public DeploymentPasswordPromptResult Prompt(bool previousAttemptFailed)
        {
            PromptCount++;
            PreviousAttemptFailedValues.Add(previousAttemptFailed);
            return remainingPasswords.Count == 0
                ? DeploymentPasswordPromptResult.Cancelled()
                : DeploymentPasswordPromptResult.Submitted(remainingPasswords.Dequeue().AsSpan());
        }
    }

    private sealed class ImmediateRetryDelay : IDeploymentAccessRetryDelay
    {
        public List<int> Attempts { get; } = [];

        public Task WaitAsync(int failedAttemptNumber, CancellationToken cancellationToken)
        {
            Attempts.Add(failedAttemptNumber);
            return Task.CompletedTask;
        }
    }
}
