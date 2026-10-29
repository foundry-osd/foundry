// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Configuration;
using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Connect.Tests.Security;

public sealed class ConnectUnlockServiceTests
{
    private readonly byte[] _key = MediaSecretEnvelopeProtector.GenerateMediaKey();
    private readonly ManualTimeProvider _time = new();
    private readonly FakePrompt _prompt = new();
    private readonly FakeHandoff _handoff = new();
    private readonly RecordingLogger<ConnectUnlockService> _logger = new();

    private ConnectRuntimeSecrets LockedSecrets() => new(
        ConnectSecret.Locked(MediaSecretEnvelopeProtector.EncryptString("lab-passphrase", _key, MediaSecretEnvelopeProtector.DeploymentKeyId)),
        ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned(), protection: new DeployProtectionSettings { IsEnabled = true });

    private ConnectUnlockService Create(ConnectRuntimeSecrets secrets) =>
        new(new FoundryConnectConfigurationDocument(), secrets, _prompt, new FakeUnwrapper(_key, "right password"), _handoff, _time, _logger);

    [Fact]
    public async Task UnlockAsync_CorrectPassword_UnlocksSendsTheKeyOnceAndZeroesThePassword()
    {
        ConnectRuntimeSecrets secrets = LockedSecrets();
        _prompt.Answers.Enqueue("right password".ToCharArray());

        ConnectUnlockResult result = await Create(secrets).UnlockAsync(ConnectUnlockTrigger.ManualWifi, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectUnlockResult.Unlocked, result);
        Assert.Equal("lab-passphrase", new string(secrets.WifiPassphrase.Value));
        Assert.Equal(_key, Assert.Single(_handoff.Sent));
        Assert.All(_prompt.Returned.Single(), static character => Assert.Equal('\0', character));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("right password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnlockAsync_WrongPasswordThenCancel_WaitsDeployDelaysAndSendsNothing()
    {
        ConnectRuntimeSecrets secrets = LockedSecrets();
        _prompt.Answers.Enqueue("wrong".ToCharArray());
        _prompt.Answers.Enqueue("wrong".ToCharArray());
        _prompt.Answers.Enqueue(null);
        ConnectUnlockService service = Create(secrets);

        Task<ConnectUnlockResult> unlock = service.UnlockAsync(ConnectUnlockTrigger.AutomaticWifi, TestContext.Current.CancellationToken);
        // Wait for the retry delay timer itself, not only the prompt: advancing before Task.Delay created it would never fire.
        await AsyncAssert.EventuallyAsync(() => _prompt.Calls.Count == 1 && _time.ActiveTimerCount == 1, "first prompt and its 1 s delay");
        _time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Single(_prompt.Calls);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await AsyncAssert.EventuallyAsync(() => _prompt.Calls.Count == 2 && _time.ActiveTimerCount == 1, "second prompt and its 2 s delay");
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(ConnectUnlockResult.Cancelled, await unlock);
        Assert.Equal([false, true, true], _prompt.Calls);
        Assert.True(secrets.IsUnlockDeclined);
        Assert.True(secrets.IsWifiLocked);
        Assert.Empty(_handoff.Sent);
    }

    [Fact]
    public async Task UnlockAsync_NothingLocked_DoesNotPrompt()
    {
        ConnectUnlockResult result = await Create(ConnectRuntimeSecrets.None()).UnlockAsync(ConnectUnlockTrigger.WiredStartup, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectUnlockResult.NotRequired, result);
        Assert.Empty(_prompt.Calls);
    }

    [Fact]
    public async Task UnlockAsync_SecondCallAfterSuccess_DoesNotPromptAgain()
    {
        ConnectRuntimeSecrets secrets = LockedSecrets();
        _prompt.Answers.Enqueue("right password".ToCharArray());
        ConnectUnlockService service = Create(secrets);
        await service.UnlockAsync(ConnectUnlockTrigger.WiredStartup, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectUnlockResult.NotRequired, await service.UnlockAsync(ConnectUnlockTrigger.ManualWifi, TestContext.Current.CancellationToken));
        Assert.Single(_prompt.Calls);
    }

    [Fact]
    public async Task UnlockAsync_AutomaticAfterADecline_DoesNotPrompt()
    {
        ConnectRuntimeSecrets secrets = LockedSecrets();
        _prompt.Answers.Enqueue(null);
        ConnectUnlockService service = Create(secrets);
        await service.UnlockAsync(ConnectUnlockTrigger.ManualWifi, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectUnlockResult.NotRequired, await service.UnlockAsync(ConnectUnlockTrigger.AutomaticWifi, TestContext.Current.CancellationToken));
        Assert.Single(_prompt.Calls);
    }

    private sealed class FakePrompt : IDeploymentPasswordPrompt
    {
        public Queue<char[]?> Answers { get; } = new();
        public List<bool> Calls { get; } = [];
        public List<char[]> Returned { get; } = [];
        public char[]? Prompt(bool previousAttemptFailed)
        {
            Calls.Add(previousAttemptFailed);
            char[]? answer = Answers.Dequeue();
            if (answer is not null) Returned.Add(answer);
            return answer;
        }
    }

    private sealed class FakeUnwrapper(byte[] key, string password) : IDeploymentKeyUnwrapper
    {
        public byte[]? TryUnwrap(DeployProtectionSettings settings, ReadOnlySpan<char> candidate) =>
            candidate.SequenceEqual(password) ? key.ToArray() : null;
    }

    private sealed class FakeHandoff : IDeploymentKeyHandoffSender
    {
        public List<byte[]> Sent { get; } = [];
        public Task<bool> TrySendAsync(byte[] deploymentKey, CancellationToken cancellationToken)
        {
            Sent.Add(deploymentKey.ToArray());
            return Task.FromResult(true);
        }
    }
}
