// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Security;

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
            new ImmediateRetryDelay());

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.True(authorized);
        Assert.Equal(0, dialog.PromptCount);
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
            retryDelay);

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
            new ImmediateRetryDelay());

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
            new ImmediateRetryDelay());

        bool authorized = await gate.AuthorizeAsync(TestContext.Current.CancellationToken);

        Assert.False(authorized);
    }

    private sealed class FakeConfigurationService(
        FoundryDeployConfigurationDocument? document,
        bool exists = true) : IDeployConfigurationService
    {
        public DeployConfigurationLoadResult LoadOptional() => new() { Exists = exists, Document = document };
    }

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
