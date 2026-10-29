// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Configuration;
using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Security;
using Microsoft.Extensions.Logging;

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
        Assert.Equal(2, _prompt.Returned.Count);
        Assert.All(_prompt.Returned, static password => Assert.All(password, static character => Assert.Equal('\0', character)));
    }

    [Fact]
    public async Task UnlockAsync_PromptUnavailable_IsACancelLoggedWithoutTheException()
    {
        ConnectRuntimeSecrets secrets = LockedSecrets();
        _prompt.Failure = new InvalidOperationException("The deployment password prompt requires a running WPF application.");

        ConnectUnlockResult result = await Create(secrets).UnlockAsync(ConnectUnlockTrigger.WiredStartup, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectUnlockResult.Cancelled, result);
        Assert.True(secrets.IsUnlockDeclined);
        Assert.Empty(_handoff.Sent);
        RecordedLogEntry warning = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Null(warning.Exception);
        Assert.Equal(nameof(InvalidOperationException), warning.Properties["FailureType"]);
        Assert.DoesNotContain(_logger.Entries, static entry => entry.Message.Contains("WPF application", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnlockAsync_KeyThatFailsTheMediaKeyCheck_KeepsTheSecretsLockedAndSendsNothing()
    {
        var secrets = new ConnectRuntimeSecrets(
            ConnectSecret.Locked(MediaSecretEnvelopeProtector.EncryptString("lab-passphrase", _key, MediaSecretEnvelopeProtector.DeploymentKeyId)),
            ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned(),
            protection: new DeployProtectionSettings { IsEnabled = true, KeyCheck = DeploymentKeyProtection.CreateKeyCheck(MediaSecretEnvelopeProtector.GenerateMediaKey()) });
        _prompt.Answers.Enqueue("right password".ToCharArray());

        ConnectUnlockResult result = await Create(secrets).UnlockAsync(ConnectUnlockTrigger.ManualWifi, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectUnlockResult.Cancelled, result);
        Assert.True(secrets.IsWifiLocked);
        Assert.True(secrets.IsUnlockDeclined);
        Assert.Empty(_handoff.Sent);
        RecordedLogEntry warning = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Null(warning.Exception);
        Assert.Equal(ConnectUnlockTrigger.ManualWifi, warning.Properties["Trigger"]);
    }

    [Fact]
    public async Task UnlockAsync_ProtectedFileTamperedWith_LogsItsFailureTypeOnce()
    {
        var secrets = new ConnectRuntimeSecrets(
            ConnectSecret.Locked(MediaSecretEnvelopeProtector.EncryptString("lab-passphrase", _key, MediaSecretEnvelopeProtector.DeploymentKeyId)),
            ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned(),
            wiredClientCertificate: ConnectProtectedContent.Locked(
                MediaSecretEnvelopeProtector.EncryptBytes([1, 2, 3], MediaSecretEnvelopeProtector.GenerateMediaKey(), MediaSecretEnvelopeProtector.DeploymentKeyId)),
            protection: new DeployProtectionSettings { IsEnabled = true });
        _prompt.Answers.Enqueue("right password".ToCharArray());

        ConnectUnlockResult result = await Create(secrets).UnlockAsync(ConnectUnlockTrigger.ManualWifi, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectUnlockResult.Unlocked, result);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WiredClientCertificate.Failure);
        RecordedLogEntry warning = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Null(warning.Exception);
        Assert.Equal("WiredClientCertificate", warning.Properties["ProtectedContent"]);
        Assert.Equal(nameof(System.Security.Cryptography.CryptographicException), warning.Properties["FailureType"]);
    }

    [Fact]
    public async Task UnlockAsync_CallerCancelledDuringTheHandoff_StillReportsUnlockedAndZeroesTheKey()
    {
        ConnectRuntimeSecrets secrets = LockedSecrets();
        _prompt.Answers.Enqueue("right password".ToCharArray());
        var handoff = new PendingHandoff();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var service = new ConnectUnlockService(new FoundryConnectConfigurationDocument(), secrets, _prompt,
            new FakeUnwrapper(_key, "right password"), handoff, _time, _logger);

        Task<ConnectUnlockResult> unlock = service.UnlockAsync(ConnectUnlockTrigger.ManualWifi, caller.Token);
        byte[] sentKey = await handoff.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await caller.CancelAsync();

        Assert.False(handoff.Token.IsCancellationRequested);
        Assert.Equal(_key, sentKey);
        handoff.Completion.SetResult(true);
        Assert.Equal(ConnectUnlockResult.Unlocked, await unlock);
        Assert.All(sentKey, static value => Assert.Equal(0, value));
        Assert.False(secrets.IsWifiLocked);
    }

    [Fact]
    public async Task UnlockAsync_AutomaticQueuedBehindACancelledManualPrompt_DoesNotPrompt()
    {
        ConnectRuntimeSecrets secrets = LockedSecrets();
        var prompt = new BlockingPrompt();
        var service = new ConnectUnlockService(new FoundryConnectConfigurationDocument(), secrets, prompt,
            new FakeUnwrapper(_key, "right password"), _handoff, _time, _logger);
        CancellationToken token = TestContext.Current.CancellationToken;

        Task<ConnectUnlockResult> manual = Task.Run(() => service.UnlockAsync(ConnectUnlockTrigger.ManualWifi, token), token);
        Assert.True(prompt.Opened.Wait(TimeSpan.FromSeconds(5), token));
        Task<ConnectUnlockResult> automatic = service.UnlockAsync(ConnectUnlockTrigger.AutomaticWifi, token);
        Assert.False(automatic.IsCompleted);
        prompt.Close.Set();

        Assert.Equal(ConnectUnlockResult.Cancelled, await manual);
        Assert.Equal(ConnectUnlockResult.NotRequired, await automatic);
        Assert.Equal(1, prompt.CallCount);
        Assert.True(secrets.IsWifiLocked);
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
        public Exception? Failure { get; set; }
        public char[]? Prompt(bool previousAttemptFailed)
        {
            Calls.Add(previousAttemptFailed);
            if (Failure is not null) throw Failure;
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

    // Keeps the service's own key buffer, not a copy, to observe that it is zeroed after the send.
    private sealed class PendingHandoff : IDeploymentKeyHandoffSender
    {
        public TaskCompletionSource<byte[]> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<bool> TrySendAsync(byte[] deploymentKey, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.SetResult(deploymentKey);
            return Completion.Task;
        }
    }

    // Holds the prompt open on its calling thread until the test closes it with a cancel.
    private sealed class BlockingPrompt : IDeploymentPasswordPrompt
    {
        private int _callCount;
        public ManualResetEventSlim Opened { get; } = new();
        public ManualResetEventSlim Close { get; } = new();
        public int CallCount => Volatile.Read(ref _callCount);
        public char[]? Prompt(bool previousAttemptFailed)
        {
            Interlocked.Increment(ref _callCount);
            Opened.Set();
            Close.Wait(TimeSpan.FromSeconds(5));
            return null;
        }
    }
}
