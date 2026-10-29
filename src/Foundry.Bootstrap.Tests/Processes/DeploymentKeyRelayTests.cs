// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Foundry.Bootstrap.Processes;
using Foundry.Core.Services.Runtime;
using Serilog;
using Serilog.Core;
using Serilog.Events;
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
            var sink = new CollectingSink();
            using var relay = new DeploymentKeyRelay(sink.CreateLogger());
            string name = relay.BeginReceive();
            relay.ExpectSender(other.Id);

            await DeploymentKeyHandoff.TrySendAsync(name, Key, TestContext.Current.CancellationToken);

            Assert.False(await relay.EndReceiveAsync());
            Assert.True(relay.HeldKeyForTesting is null || relay.HeldKeyForTesting.All(static value => value == 0));
            LogEvent refusal = Assert.Single(sink.Events);
            Assert.Equal(LogEventLevel.Warning, refusal.Level);
            Assert.Equal("Deployment key hand-off refused a client. ExpectedProcessId={ExpectedProcessId}, ClientProcessId={ClientProcessId}",
                refusal.MessageTemplate.Text);
            Assert.Equal(other.Id, ((ScalarValue)refusal.Properties["ExpectedProcessId"]).Value);
            Assert.Equal((uint)Environment.ProcessId, ((ScalarValue)refusal.Properties["ClientProcessId"]).Value);
            sink.AssertNeverContains(name);
        }
        finally { KillQuietly(other); }
    }

    [Fact]
    public async Task Receive_OversizedMessage_IsRefused()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        string name = relay.BeginReceive();
        relay.ExpectSender(Environment.ProcessId);
        byte[] oversized = [.. Key, 33];
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(5000, TestContext.Current.CancellationToken);
            await client.WriteAsync(oversized, TestContext.Current.CancellationToken);
        }

        Assert.False(await relay.EndReceiveAsync());
        Assert.All(relay.HeldKeyForTesting!, static value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Dispose_ZeroesTheHeldKey()
    {
        var relay = new DeploymentKeyRelay(Logger);
        string name = relay.BeginReceive();
        relay.ExpectSender(Environment.ProcessId);
        await DeploymentKeyHandoff.TrySendAsync(name, Key, TestContext.Current.CancellationToken);
        Assert.True(await relay.EndReceiveAsync());
        byte[] held = relay.HeldKeyForTesting!;
        Assert.Equal(Key, held);

        relay.Dispose();

        Assert.All(held, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void ExpectWithoutPipe_DoesNothing()
    {
        using var relay = new DeploymentKeyRelay(Logger);

        relay.ExpectSender(Environment.ProcessId);
        Assert.Null(relay.BeginServe());
        relay.ExpectReceiver(Environment.ProcessId);
    }

    [Fact]
    public async Task ExpectReceiver_AfterServeWithoutKey_DoesNothing()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        relay.BeginReceive();
        relay.ExpectSender(Environment.ProcessId);
        Assert.False(await relay.EndReceiveAsync());

        Assert.Null(relay.BeginServe());
        relay.ExpectReceiver(Environment.ProcessId);

        Assert.False(await relay.EndServeAsync());
    }

    [Fact]
    public void SecondServer_ForTheRelayPipeName_Fails()
    {
        using var relay = new DeploymentKeyRelay(Logger);
        string name = relay.BeginReceive();

        Exception failure = Assert.ThrowsAny<Exception>(() => new NamedPipeServerStream(name, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous).Dispose());
        Assert.True(failure is IOException or UnauthorizedAccessException, failure.GetType().Name);
    }

    [Fact]
    public void CreateServer_WhenTheNameWasSquatted_Fails()
    {
        string name = DeploymentKeyHandoff.CreatePipeName();
        using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        Exception failure = Assert.ThrowsAny<Exception>(() => DeploymentKeyRelay.CreateServer(name, PipeDirection.In).Dispose());
        Assert.True(failure is IOException or UnauthorizedAccessException, failure.GetType().Name);
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
        var sink = new CollectingSink();
        using var relay = new DeploymentKeyRelay(sink.CreateLogger());
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
        // The served pipe is gone; a plain short connect proves it without waiting for the 5 s hand-off client timeout.
        using (var late = new NamedPipeClientStream(".", outbound!, PipeDirection.In, PipeOptions.Asynchronous))
        {
            await Assert.ThrowsAsync<TimeoutException>(() => late.ConnectAsync(250, TestContext.Current.CancellationToken));
        }

        Assert.Equal(2, sink.Events.Count);
        sink.AssertNeverContains(inbound);
        sink.AssertNeverContains(outbound!);
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
        try
        {
            relay.ExpectSender(child.Id);

            await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.Equal(0, child.ExitCode);
            Assert.True(await relay.EndReceiveAsync());
            Assert.Equal(Key, relay.HeldKeyForTesting);
        }
        finally { KillQuietly(child); }
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
        try
        {
            relay.ExpectReceiver(child.Id);
            string output = await child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.Equal(BitConverter.ToString(Key), output);
            Assert.True(await relay.EndServeAsync());
        }
        finally { KillQuietly(child); }
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

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill.
        }
        catch (Win32Exception)
        {
            // The process is terminating or cannot be killed; the test outcome does not depend on it.
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> events = new();

        public IReadOnlyList<LogEvent> Events => [.. events];

        public ILogger CreateLogger() => new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();

        public void Emit(LogEvent logEvent) => events.Enqueue(logEvent);

        // Neither the template, the rendered message nor any property may carry the text.
        public void AssertNeverContains(string text)
        {
            foreach (LogEvent logEvent in events)
            {
                Assert.DoesNotContain(text, logEvent.MessageTemplate.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(text, logEvent.RenderMessage(), StringComparison.OrdinalIgnoreCase);
                foreach (LogEventPropertyValue value in logEvent.Properties.Values)
                {
                    Assert.DoesNotContain(text, value.ToString(), StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
