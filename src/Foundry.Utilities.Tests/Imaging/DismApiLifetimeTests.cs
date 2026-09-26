// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Utilities.Imaging;
using Xunit;

namespace Foundry.Utilities.Tests.Imaging;

public sealed class DismApiLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumersShareInitializationUntilBothHaveReleasedTheirLeases(bool disposeFirstConsumerFirst)
    {
        var native = new FakeNativeLifetime();
        var lifetime = new DismApiLifetime(native.Initialize, native.Shutdown);
        IDisposable first = lifetime.Acquire();
        IDisposable second = lifetime.Acquire();

        Assert.Equal(1, native.InitializeCalls);
        IDisposable released = disposeFirstConsumerFirst ? first : second;
        IDisposable remaining = disposeFirstConsumerFirst ? second : first;
        released.Dispose();
        released.Dispose();
        Assert.True(native.Initialized);
        Assert.Equal(0, native.ShutdownCalls);

        using (lifetime.Acquire()) Assert.True(native.Initialized);
        Assert.Equal(1, native.InitializeCalls);
        remaining.Dispose();
        Assert.False(native.Initialized);
        Assert.Equal(1, native.ShutdownCalls);

        using (lifetime.Acquire()) Assert.True(native.Initialized);
        Assert.Equal(2, native.InitializeCalls);
        Assert.Equal(2, native.ShutdownCalls);
    }

    [Fact]
    public void FailedInitializationDoesNotAcquireOwnershipAndCanBeRetried()
    {
        var native = new FakeNativeLifetime { InitializeResult = unchecked((int)0x80070005) };
        var lifetime = new DismApiLifetime(native.Initialize, native.Shutdown);
        COMException error = Assert.Throws<COMException>(() => lifetime.Acquire());
        Assert.Equal(native.InitializeResult, error.HResult);
        Assert.Equal(0, native.ShutdownCalls);

        native.InitializeResult = 0;
        using (lifetime.Acquire()) Assert.True(native.Initialized);
        Assert.Equal(1, native.ShutdownCalls);
    }

    private sealed class FakeNativeLifetime
    {
        public bool Initialized { get; private set; }
        public int InitializeCalls { get; private set; }
        public int ShutdownCalls { get; private set; }
        public int InitializeResult { get; set; }

        public int Initialize()
        {
            Assert.False(Initialized);
            InitializeCalls++;
            Initialized = InitializeResult == 0;
            return InitializeResult;
        }

        public int Shutdown()
        {
            Assert.True(Initialized);
            ShutdownCalls++;
            Initialized = false;
            return 0;
        }
    }
}
