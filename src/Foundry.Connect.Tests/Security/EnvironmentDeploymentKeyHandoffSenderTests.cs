// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Pipes;
using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Services.Runtime;

namespace Foundry.Connect.Tests.Security;

[Collection(nameof(DeploymentKeyHandoffEnvironmentCollection))]
public sealed class EnvironmentDeploymentKeyHandoffSenderTests
{
    private static readonly byte[] Key = Enumerable.Range(1, DeploymentKeyHandoff.KeyLength).Select(static value => (byte)value).ToArray();

    private readonly RecordingLogger<EnvironmentDeploymentKeyHandoffSender> _logger = new();

    [Fact]
    public async Task TrySendAsync_WithoutAValidPipeName_ReturnsFalseAndRemovesTheVariable()
    {
        Environment.SetEnvironmentVariable(DeploymentKeyHandoff.SendPipeEnvironmentVariable, "other-pipe");

        var sender = new EnvironmentDeploymentKeyHandoffSender(_logger);

        Assert.Null(Environment.GetEnvironmentVariable(DeploymentKeyHandoff.SendPipeEnvironmentVariable));
        Assert.False(await sender.TrySendAsync(Key, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("other-pipe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrySendAsync_SendsTheKeyAtMostOnce()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string name = DeploymentKeyHandoff.CreatePipeName();
        Environment.SetEnvironmentVariable(DeploymentKeyHandoff.SendPipeEnvironmentVariable, name);
        var sender = new EnvironmentDeploymentKeyHandoffSender(_logger);
        Assert.Null(Environment.GetEnvironmentVariable(DeploymentKeyHandoff.SendPipeEnvironmentVariable));
        using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, DeploymentKeyHandoff.CreateServerSecurity());
        Task connected = server.WaitForConnectionAsync(token);

        Task<bool> first = sender.TrySendAsync(Key, token);
        await connected;
        byte[] received = new byte[DeploymentKeyHandoff.KeyLength];
        await server.ReadExactlyAsync(received, token);

        Assert.True(await first);
        Assert.Equal(Key, received);
        Assert.False(await sender.TrySendAsync(Key, token));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains(name, StringComparison.Ordinal));
    }
}
