// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Pipes;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Services.Security;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Tests;

// Only this class sets the process-wide hand-off variable; its tests run one at a time.
public sealed class DeploymentKeyHandoffReceiverTests
{
    private static readonly byte[] Key = Enumerable.Range(1, DeploymentKeyHandoff.KeyLength).Select(static value => (byte)value).ToArray();

    private readonly RecordingLogger _logger = new();

    [Fact]
    public async Task TryReceiveAsync_WithoutAValidPipeName_ReturnsNullAndRemovesTheVariable()
    {
        Environment.SetEnvironmentVariable(DeploymentKeyHandoff.ReceivePipeEnvironmentVariable, "other-pipe");

        var receiver = new DeploymentKeyHandoffReceiver(_logger);

        Assert.Null(Environment.GetEnvironmentVariable(DeploymentKeyHandoff.ReceivePipeEnvironmentVariable));
        Assert.Null(await receiver.TryReceiveAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_logger.Messages, static message => message.Contains("other-pipe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryReceiveAsync_ReceivesTheKeyAtMostOnce()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string name = DeploymentKeyHandoff.CreatePipeName();
        Environment.SetEnvironmentVariable(DeploymentKeyHandoff.ReceivePipeEnvironmentVariable, name);
        var receiver = new DeploymentKeyHandoffReceiver(_logger);
        Assert.Null(Environment.GetEnvironmentVariable(DeploymentKeyHandoff.ReceivePipeEnvironmentVariable));
        using NamedPipeServerStream server = CreateServer(name);
        Task connected = server.WaitForConnectionAsync(token);

        Task<byte[]?> first = receiver.TryReceiveAsync(token);
        await connected;
        await server.WriteAsync(Key, token);
        await server.FlushAsync(token);

        Assert.Equal(Key, await first);
        Assert.Null(await receiver.TryReceiveAsync(token));
        Assert.DoesNotContain(_logger.Messages, message => message.Contains(name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryReceiveAsync_ValidPipeNameWithoutAKey_LogsThatNothingWasReceived()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string name = DeploymentKeyHandoff.CreatePipeName();
        Environment.SetEnvironmentVariable(DeploymentKeyHandoff.ReceivePipeEnvironmentVariable, name);
        var receiver = new DeploymentKeyHandoffReceiver(_logger);
        using NamedPipeServerStream server = CreateServer(name);
        Task connected = server.WaitForConnectionAsync(token);

        Task<byte[]?> receive = receiver.TryReceiveAsync(token);
        await connected;
        // A short message, then the server closes: the receiver gets no key.
        await server.WriteAsync(Key.AsMemory(0, 4), token);
        server.Disconnect();

        Assert.Null(await receive);
        Assert.Contains((LogLevel.Debug, "The deployment key hand-off was not received."), _logger.Entries);
        Assert.DoesNotContain(_logger.Messages, message => message.Contains(name, StringComparison.Ordinal));
    }

    private static NamedPipeServerStream CreateServer(string name) =>
        NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, DeploymentKeyHandoff.CreateServerSecurity());

    private sealed class RecordingLogger : ILogger<DeploymentKeyHandoffReceiver>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IEnumerable<string> Messages => Entries.Select(static entry => entry.Message);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
