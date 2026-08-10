// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Avalonia.Services.Threading;
using Foundry.Connect.Models;
using Foundry.Connect.Services.ApplicationLifetime;
using System.Collections.Concurrent;

namespace Foundry.Connect.Tests;

public sealed class ApplicationLifetimeServiceTests
{
    [Fact]
    public void Exit_WhenCalledTwice_PreservesFirstExitCodeAndSchedulesOneExit()
    {
        var dispatcher = new RecordingDispatcher(checkAccess: false);
        var exitHandler = new RecordingExitHandler();
        var service = new ApplicationLifetimeService(dispatcher, exitHandler);

        service.Exit(FoundryConnectExitCode.ConfigurationFailure);
        service.Exit(FoundryConnectExitCode.Success);

        Assert.True(service.IsExitRequested);
        Assert.Equal(FoundryConnectExitCode.ConfigurationFailure, service.ExitCode);
        Assert.Equal(1, dispatcher.PostCalls);
        Assert.Equal(0, exitHandler.ExitCalls);

        dispatcher.RunPostedActions();
        Assert.Equal(1, exitHandler.ExitCalls);
        Assert.Equal(FoundryConnectExitCode.ConfigurationFailure, exitHandler.ExitCode);
    }

    [Fact]
    public void Exit_WhenAlreadyOnUiThread_ExitsImmediately()
    {
        var dispatcher = new RecordingDispatcher(checkAccess: true);
        var exitHandler = new RecordingExitHandler();
        var service = new ApplicationLifetimeService(dispatcher, exitHandler);

        service.Exit(FoundryConnectExitCode.UserAborted);

        Assert.Equal(0, dispatcher.PostCalls);
        Assert.Equal(1, exitHandler.ExitCalls);
        Assert.Equal(FoundryConnectExitCode.UserAborted, exitHandler.ExitCode);
    }

    [Fact]
    public async Task Exit_WhenCalledConcurrently_SchedulesExactlyOneExitAndPreservesItsCode()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var dispatcher = new RecordingDispatcher(checkAccess: false);
            var exitHandler = new RecordingExitHandler();
            var service = new ApplicationLifetimeService(dispatcher, exitHandler);
            using var start = new ManualResetEventSlim();
            FoundryConnectExitCode[] exitCodes = Enum.GetValues<FoundryConnectExitCode>();
            Task[] calls = exitCodes
                .Select(exitCode => Task.Run(() =>
                {
                    start.Wait(TestContext.Current.CancellationToken);
                    service.Exit(exitCode);
                }, TestContext.Current.CancellationToken))
                .ToArray();

            start.Set();
            await Task.WhenAll(calls);

            Assert.Equal(1, dispatcher.PostCalls);
            dispatcher.RunPostedActions();
            Assert.Equal(1, exitHandler.ExitCalls);
            Assert.Equal(service.ExitCode, exitHandler.ExitCode);
        }
    }

    private sealed class RecordingDispatcher(bool checkAccess) : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> _actions = new();

        private int _postCalls;

        public int PostCalls => Volatile.Read(ref _postCalls);

        public bool CheckAccess() => checkAccess;

        public void Post(Action action)
        {
            Interlocked.Increment(ref _postCalls);
            _actions.Enqueue(action);
        }

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public void RunPostedActions()
        {
            while (_actions.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }

    private sealed class RecordingExitHandler : IApplicationExitHandler
    {
        public int ExitCalls { get; private set; }

        public FoundryConnectExitCode ExitCode { get; private set; }

        public void Exit(FoundryConnectExitCode exitCode)
        {
            ExitCalls++;
            ExitCode = exitCode;
        }
    }
}
