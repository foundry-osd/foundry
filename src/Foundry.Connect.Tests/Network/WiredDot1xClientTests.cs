// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network.Native;

namespace Foundry.Connect.Tests.Network;

public sealed class WiredDot1xClientTests
{
    [Fact]
    public async Task AddProfileAsync_UsesAbsoluteNetshPathAndQuotedFilename()
    {
        var runner = new FakeProcessRunner(new ConnectProcessResult(ConnectProcessStatus.Completed, 0, string.Empty, string.Empty));
        var client = new WiredDot1xClient(runner);

        WiredCommandResult result = await client.AddProfileAsync(@"X:\Foundry\Config\Network\wired.xml", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        (string path, string arguments, TimeSpan timeout, ConnectProcessLogging logging) = Assert.Single(runner.Calls);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "netsh.exe"), path);
        Assert.Equal("lan add profile filename=\"X:\\Foundry\\Config\\Network\\wired.xml\"", arguments);
        Assert.Equal(WiredDot1xClient.AddProfileTimeout, timeout);
        Assert.Equal(ConnectProcessLogging.EveryRun, logging);
    }

    [Fact]
    public async Task ReconnectAsync_QuotesInterfaceName()
    {
        var runner = new FakeProcessRunner(new ConnectProcessResult(ConnectProcessStatus.Completed, 0, string.Empty, string.Empty));

        await new WiredDot1xClient(runner).ReconnectAsync("Ethernet 2", TestContext.Current.CancellationToken);

        Assert.Equal("lan reconnect interface=\"Ethernet 2\"", Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task Commands_RefuseValuesContainingQuotes()
    {
        var runner = new FakeProcessRunner();

        WiredCommandResult result = await new WiredDot1xClient(runner).ReconnectAsync("Eth\"x", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ReadAuthenticationAsync_WhenCommandTimesOut_ReturnsEmpty()
    {
        var runner = new FakeProcessRunner(new ConnectProcessResult(ConnectProcessStatus.TimedOut, -1, string.Empty, string.Empty));

        Assert.Empty(await new WiredDot1xClient(runner).ReadAuthenticationAsync(TestContext.Current.CancellationToken));
        (_, string arguments, _, ConnectProcessLogging logging) = Assert.Single(runner.Calls);
        Assert.Equal("lan show interfaces", arguments);
        Assert.Equal(ConnectProcessLogging.OnChange, logging);
    }

    private sealed class FakeProcessRunner(params ConnectProcessResult[] results) : IConnectProcessRunner
    {
        private readonly Queue<ConnectProcessResult> _results = new(results);

        public List<(string Path, string Arguments, TimeSpan Timeout, ConnectProcessLogging Logging)> Calls { get; } = [];

        public Task<ConnectProcessResult> RunAsync(string executablePath, string rawArguments, TimeSpan timeout, ConnectProcessLogging logging, CancellationToken cancellationToken)
        {
            Calls.Add((executablePath, rawArguments, timeout, logging));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
