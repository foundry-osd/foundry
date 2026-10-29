// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Foundry.Bootstrap.Processes;
using Foundry.Core.Services.Runtime;
using Serilog;
using Xunit;

namespace Foundry.Bootstrap.Tests.Processes;

public sealed class DeploymentKeyRelayTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task Receive_FromExpectedProcess_HoldsExactlyTheKey()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        string name = relay.BeginReceive();
        relay.ExpectSender(Environment.ProcessId);

        Assert.True(await DeploymentKeyHandoff.TrySendAsync(name, Key, TestContext.Current.CancellationToken));

        Assert.True(await relay.EndReceiveAsync());
        Assert.Equal(Key, relay.HeldKeyForTesting);
    }

    [Fact]
    public async Task Receive_FromUnexpectedProcess_IsRefused()
    {
        using Process other = Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            using var relay = new DeploymentKeyRelay(Logger);
            string name = relay.BeginReceive();
            relay.ExpectSender(other.Id);

            await DeploymentKeyHandoff.TrySendAsync(name, Key, TestContext.Current.CancellationToken);

            Assert.False(await relay.EndReceiveAsync());
            Assert.True(relay.HeldKeyForTesting is null || relay.HeldKeyForTesting.All(static value => value == 0));
        }
        finally { other.Kill(); }
    }

    [Fact]
    public async Task Receive_ShortMessage_IsRefused()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        string name = relay.BeginReceive();
        relay.ExpectSender(Environment.ProcessId);
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(5000, TestContext.Current.CancellationToken);
            await client.WriteAsync(Key.AsMemory(0, 31), TestContext.Current.CancellationToken);
        }

        Assert.False(await relay.EndReceiveAsync());
    }

    [Fact]
    public async Task Serve_DeliversOnceThenZeroesTheKey()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        string inbound = relay.BeginReceive();
        relay.ExpectSender(Environment.ProcessId);
        await DeploymentKeyHandoff.TrySendAsync(inbound, Key, TestContext.Current.CancellationToken);
        await relay.EndReceiveAsync();

        string? outbound = relay.BeginServe();
        relay.ExpectReceiver(Environment.ProcessId);
        byte[]? received = await DeploymentKeyHandoff.TryReceiveAsync(outbound, TestContext.Current.CancellationToken);

        Assert.Equal(Key, received);
        Assert.True(await relay.EndServeAsync());
        Assert.All(relay.HeldKeyForTesting!, static value => Assert.Equal(0, value));
        Assert.Null(await DeploymentKeyHandoff.TryReceiveAsync(outbound, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Serve_WithoutKeyOrClient_CompletesWithoutWaiting()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        Assert.Null(relay.BeginServe());

        relay.BeginReceive();
        Assert.False(await relay.EndReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PipeDirection.In)]
    [InlineData(PipeDirection.Out)]
    public void ServerHandles_AreNotInheritable(PipeDirection direction)
    {
        using NamedPipeServerStream server = DeploymentKeyRelay.CreateServer(DeploymentKeyHandoff.CreatePipeName(), direction);

        Assert.True(GetHandleInformation(server.SafePipeHandle, out uint flags));
        Assert.Equal(0u, flags & HandleFlagInherit);
    }

    // Feasibility (spec 5.7): a real child process started like Bootstrap starts Connect reaches the pipe through the environment.
    [Fact]
    public async Task ChildProcess_SendsTheKeyThroughTheEnvironmentVariable()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        string name = relay.BeginReceive();
        using Process child = StartPowerShell(DeploymentKeyHandoff.SendPipeEnvironmentVariable, name,
            "$p = New-Object System.IO.Pipes.NamedPipeClientStream('.', $env:" + DeploymentKeyHandoff.SendPipeEnvironmentVariable +
            ", [System.IO.Pipes.PipeDirection]::Out); $p.Connect(5000); [byte[]]$k = 1..32; $p.Write($k, 0, 32); $p.Flush(); $p.Dispose()");
        relay.ExpectSender(child.Id);

        await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(0, child.ExitCode);
        Assert.True(await relay.EndReceiveAsync());
        Assert.Equal(Key, relay.HeldKeyForTesting);
    }

    [Fact]
    public async Task ChildProcess_ReadsTheServedKey()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        string inbound = relay.BeginReceive();
        relay.ExpectSender(Environment.ProcessId);
        await DeploymentKeyHandoff.TrySendAsync(inbound, Key, TestContext.Current.CancellationToken);
        await relay.EndReceiveAsync();
        string outbound = relay.BeginServe()!;

        using Process child = StartPowerShell(DeploymentKeyHandoff.ReceivePipeEnvironmentVariable, outbound,
            "$p = New-Object System.IO.Pipes.NamedPipeClientStream('.', $env:" + DeploymentKeyHandoff.ReceivePipeEnvironmentVariable +
            ", [System.IO.Pipes.PipeDirection]::In); $p.Connect(5000); $b = New-Object byte[] 32; $r = 0; " +
            "while ($r -lt 32) { $n = $p.Read($b, $r, 32 - $r); if ($n -eq 0) { break }; $r += $n }; $p.Dispose(); " +
            "[Console]::Out.Write([BitConverter]::ToString($b, 0, $r))");
        relay.ExpectReceiver(child.Id);
        string output = await child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await child.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BitConverter.ToString(Key), output);
        Assert.True(await relay.EndServeAsync());
    }

    private const uint HandleFlagInherit = 0x1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetHandleInformation(SafeHandle handle, out uint flags);

    private static Process StartPowerShell(string variable, string pipeName, string script)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) start.ArgumentList.Add(argument);
        start.Environment[variable] = pipeName;
        return Process.Start(start)!;
    }
}
