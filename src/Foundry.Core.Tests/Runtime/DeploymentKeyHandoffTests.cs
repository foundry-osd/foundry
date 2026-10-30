// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class DeploymentKeyHandoffTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();

    [Fact]
    public void CreatePipeName_IsRandomAndValid()
    {
        string first = DeploymentKeyHandoff.CreatePipeName();
        string second = DeploymentKeyHandoff.CreatePipeName();

        Assert.Matches("^Foundry\\.DeploymentKey\\.[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);
        Assert.True(DeploymentKeyHandoff.IsValidPipeName(first));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Foundry.DeploymentKey.ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("Foundry.DeploymentKey.0123")]
    [InlineData(@"\\.\pipe\Foundry.DeploymentKey.0123456789abcdef0123456789abcdef")]
    public void IsValidPipeName_RejectsForeignNames(string? name) => Assert.False(DeploymentKeyHandoff.IsValidPipeName(name));

    [Fact]
    public async Task TrySendAsync_WithoutValidPipeName_ReturnsFalse()
    {
        Assert.False(await DeploymentKeyHandoff.TrySendAsync(null, Key, TestContext.Current.CancellationToken));
        Assert.False(await DeploymentKeyHandoff.TrySendAsync("other-pipe", Key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TrySendAsync_WrongKeyLength_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            DeploymentKeyHandoff.TrySendAsync(DeploymentKeyHandoff.CreatePipeName(), new byte[31], TestContext.Current.CancellationToken));

    [Fact]
    public void CreateServerSecurity_GrantsOnlyTheCurrentUser()
    {
        PipeSecurity security = DeploymentKeyHandoff.CreateServerSecurity();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(identity.Owner, security.GetOwner(typeof(SecurityIdentifier)));
        PipeAccessRule rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>());
        Assert.Equal(identity.User, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
    }

    [Fact]
    public async Task SendThenReceive_ThroughHandoffServerSecurity()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string name = DeploymentKeyHandoff.CreatePipeName();
        using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, DeploymentKeyHandoff.CreateServerSecurity());
        Task connected = server.WaitForConnectionAsync(token);

        // A zero-byte pipe quota completes the client write only once the server has read it.
        Task<bool> send = DeploymentKeyHandoff.TrySendAsync(name, Key, token);
        await connected;
        byte[] received = new byte[32];
        await server.ReadExactlyAsync(received, token);
        Assert.True(await send);
        Assert.Equal(Key, received);
    }

    [Fact]
    public async Task TryReceiveAsync_ShortMessage_ReturnsNull()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string name = DeploymentKeyHandoff.CreatePipeName();
        using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, DeploymentKeyHandoff.CreateServerSecurity());
        Task<byte[]?> receive = DeploymentKeyHandoff.TryReceiveAsync(name, token);
        await server.WaitForConnectionAsync(token);
        await server.WriteAsync(Key.AsMemory(0, 16), token);
        server.Disconnect();

        Assert.Null(await receive);
    }
}
