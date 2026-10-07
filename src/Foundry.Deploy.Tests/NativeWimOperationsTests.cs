// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Deploy.Services.Deployment.Native;
using Foundry.Deploy.Services.Deployment.Native.Imaging;

namespace Foundry.Deploy.Tests;

public sealed class NativeWimOperationsTests
{
    [Fact]
    public void Apply_ChecksArchiveIntegrityOnOpenAndSetsScratchBeforeLoading()
    {
        var api = new FakeWimApi();
        new NativeWimOperations(api).Execute(Request(), _ => { });

        Assert.Equal(0x80000000u, api.Access);
        Assert.Equal(3u, api.Disposition);
        Assert.Equal(2u, api.OpenFlags);
        Assert.Equal(0u, api.ApplyFlags);
        Assert.Equal(7u, api.Index);
        Assert.Equal(@"T:\Windows", api.Target);
        Assert.Equal(@"T:\Foundry\Temp\Wim", api.Scratch);
        Assert.Equal(["open", "scratch", "register", "load", "apply", "close-image", "unregister", "close-wim"], api.Calls);
        Assert.Equal(api.RegisteredCallback, api.UnregisteredCallback);
        Assert.NotEqual(nint.Zero, api.RegisteredCallback);
    }

    [Theory]
    [InlineData("install.esd")]
    [InlineData("install.swm")]
    public void Apply_RejectsUnsupportedContainersBeforeOpening(string imagePath)
    {
        var api = new FakeWimApi();
        Assert.Throws<NotSupportedException>(() => new NativeWimOperations(api).Execute(Request() with { ImagePath = imagePath }, _ => { }));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void Probe_ChecksCapabilitiesWithoutOpeningAnImage()
    {
        var api = new FakeWimApi();
        new NativeWimOperations(api).Execute(new() { Operation = NativeWorkerOperation.ProbeWim }, _ => { });
        Assert.Equal(["probe"], api.Calls);
    }

    [Theory]
    [InlineData("open", "WIMCreateFile", "open")]
    [InlineData("scratch", "WIMSetTemporaryPath", "open,scratch,close-wim")]
    [InlineData("register", "WIMRegisterMessageCallback", "open,scratch,register,close-wim")]
    [InlineData("load", "WIMLoadImage", "open,scratch,register,load,unregister,close-wim")]
    [InlineData("apply", "WIMApplyImage", "open,scratch,register,load,apply,close-image,unregister,close-wim")]
    public void Apply_FailurePreservesNativeErrorAndReleasesEveryAcquiredResource(string failAt, string function, string calls)
    {
        var api = new FakeWimApi { FailAt = failAt };
        NativeOperationException error = Assert.Throws<NativeOperationException>(() => new NativeWimOperations(api).Execute(Request(), _ => { }));
        Assert.Equal(function, error.Function);
        Assert.Equal(13, error.ErrorCode);
        Assert.Equal(calls.Split(','), api.Calls);
    }

    [Fact]
    public void Apply_CleanupFailuresPreservePrimaryFailureAndContinueCleanup()
    {
        var api = new FakeWimApi { FailAt = "apply", FailCleanup = true };
        NativeOperationException error = Assert.Throws<NativeOperationException>(() => new NativeWimOperations(api).Execute(Request(), _ => { }));
        Assert.Equal("WIMApplyImage", error.Function);
        Assert.Equal(13, error.ErrorCode);
        Assert.Equal(3, error.CleanupErrors.Count);
        Assert.Equal(["close-image", "unregister", "close-wim"], api.Calls.TakeLast(3));
    }

    [Fact]
    public void Apply_ReportsCleanupFailureAfterSuccessfulMutation()
    {
        var api = new FakeWimApi { FailCleanup = true };
        NativeOperationException error = Assert.Throws<NativeOperationException>(() => new NativeWimOperations(api).Execute(Request(), _ => { }));
        Assert.Equal("WIMCloseHandle", error.Function);
        Assert.Equal(50, error.ErrorCode);
        Assert.Equal(2, error.CleanupErrors.Count);
        Assert.Equal(["close-image", "unregister", "close-wim"], api.Calls.TakeLast(3));
    }

    [Fact]
    public void Apply_RootsCallbackAndSerializesDeduplicatedProgress()
    {
        var messages = new List<NativeWorkerMessage>();
        var api = new FakeWimApi
        {
            DuringApply = callback =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Parallel.For(0, 20, _ => Assert.Equal(0u, callback(0x9478, 35, 0, 0)));
                Assert.Equal(0u, callback(0x9478, 100, 0, 0));
            }
        };
        new NativeWimOperations(api).Execute(Request(), messages.Add);
        Assert.Equal([35d, 100d], messages.Where(message => message.Percent.HasValue).Select(message => message.Percent!.Value));
        Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message.Phase)));
    }

    [Fact]
    public void Apply_CallbackExceptionWaitsForNativeReturnAndCleanup()
    {
        bool returnedFromCallback = false;
        var api = new FakeWimApi
        {
            DuringApply = callback =>
            {
                Assert.Equal(0u, callback(0x9478, 12, 0, 0));
                returnedFromCallback = true;
            }
        };
        NativeOperationException error = Assert.Throws<NativeOperationException>(() => new NativeWimOperations(api).Execute(Request(), _ => throw new IOException("Output unavailable.")));
        Assert.True(returnedFromCallback);
        Assert.Equal("WIMMessageCallback", error.Function);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(["close-image", "unregister", "close-wim"], api.Calls.TakeLast(3));
    }

    [Fact]
    public void Apply_NativeCallbackErrorDoesNotSkipFailedFiles()
    {
        var api = new FakeWimApi
        {
            FailAt = "apply",
            DuringApply = callback => Assert.Equal(0u, callback(0x947f, 0, 5, 0))
        };
        NativeOperationException error = Assert.Throws<NativeOperationException>(() => new NativeWimOperations(api).Execute(Request(), _ => { }));
        Assert.Equal(13, error.ErrorCode);
        Assert.Contains("5", error.Message);
    }

    [Fact]
    public void Apply_NativeCallbackErrorNamesTheFailedPath()
    {
        nint failedPath = Marshal.StringToHGlobalUni(@"T:\Windows\System32\broken.dll");
        try
        {
            var api = new FakeWimApi
            {
                FailAt = "apply",
                DuringApply = callback => Assert.Equal(0u, callback(0x947f, (nuint)failedPath, 5, 0))
            };
            NativeOperationException error = Assert.Throws<NativeOperationException>(() => new NativeWimOperations(api).Execute(Request(), _ => { }));
            Assert.Contains(@"T:\Windows\System32\broken.dll", error.Message);
        }
        finally { Marshal.FreeHGlobal(failedPath); }
    }

    [Theory]
    [InlineData("register-log", "WIMRegisterLogFile", "register-log")]
    [InlineData("open", "WIMCreateFile", "register-log,open,unregister-log")]
    public void Apply_FailedLogOrOpenReleasesOnlyAcquiredResources(string failAt, string function, string calls)
    {
        var api = new FakeWimApi { FailAt = failAt };
        NativeOperationException error = Assert.Throws<NativeOperationException>(() => new NativeWimOperations(api).Execute(Request() with { LogFilePath = "wim.log" }, _ => { }));
        Assert.Equal(function, error.Function);
        Assert.Equal(calls.Split(','), api.Calls);
    }

    [Fact]
    public void Apply_RegistersAndReleasesRequestedNativeLog()
    {
        var api = new FakeWimApi();
        new NativeWimOperations(api).Execute(Request() with { LogFilePath = @"T:\Foundry\Logs\wimgapi.log" }, _ => { });
        Assert.Equal("register-log", api.Calls[0]);
        Assert.Equal("unregister-log", api.Calls[^1]);
        Assert.Equal(@"T:\Foundry\Logs\wimgapi.log", api.LogPath);
    }

    [Fact]
    public void Apply_FailedUnregistrationKeepsCallbackRootedAndStopsReporting()
    {
        var messages = new List<NativeWorkerMessage>();
        var api = new FakeWimApi { FailCleanup = true };
        Assert.Throws<NativeOperationException>(() => new NativeWimOperations(api).Execute(Request(), messages.Add));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.True(api.CallbackReference!.TryGetTarget(out WimMessageCallback? callback));
        Assert.Equal(0u, callback!(0x9478, 88, 0, 0));
        Assert.Empty(messages);
    }

    [Theory]
    [InlineData(0, "scratch")]
    [InlineData(1, "")]
    public void Apply_RejectsInvalidIndexOrMissingScratchBeforeOpening(int imageIndex, string scratch)
    {
        var api = new FakeWimApi();
        Assert.ThrowsAny<ArgumentException>(() => new NativeWimOperations(api).Execute(Request() with { ImageIndex = imageIndex, ScratchDirectory = scratch }, _ => { }));
        Assert.Empty(api.Calls);
    }

    private static NativeWorkerRequest Request() => new()
    {
        Operation = NativeWorkerOperation.ApplyWim,
        ImagePath = "install.wim",
        ImageIndex = 7,
        WindowsRoot = @"T:\Windows",
        ScratchDirectory = @"T:\Foundry\Temp\Wim"
    };

    private sealed class FakeWimApi : INativeWimApi
    {
        public List<string> Calls { get; } = [];
        public string? FailAt { get; init; }
        public bool FailCleanup { get; init; }
        public Action<WimMessageCallback>? DuringApply { get; init; }
        public uint Access { get; private set; }
        public uint Disposition { get; private set; }
        public uint OpenFlags { get; private set; }
        public uint ApplyFlags { get; private set; }
        public uint Index { get; private set; }
        public string? Target { get; private set; }
        public string? Scratch { get; private set; }
        public string? LogPath { get; private set; }
        public nint RegisteredCallback { get; private set; }
        public nint UnregisteredCallback { get; private set; }
        public WeakReference<WimMessageCallback>? CallbackReference { get; private set; }
        private int lastError;

        public void Probe() => Calls.Add("probe");
        public int GetLastError() => lastError;
        public nint CreateFile(string path, uint access, uint disposition, uint flags, uint compression)
        {
            Access = access; Disposition = disposition; OpenFlags = flags;
            return Call("open") ? 101 : 0;
        }
        public bool SetTemporaryPath(nint wim, string path) { Scratch = path; return Call("scratch"); }
        public nint LoadImage(nint wim, uint index) { Index = index; return Call("load") ? 202 : 0; }
        public bool ApplyImage(nint image, string target, uint flags)
        {
            Target = target; ApplyFlags = flags;
            bool success = Call("apply");
            DuringApply?.Invoke(Marshal.GetDelegateForFunctionPointer<WimMessageCallback>(RegisteredCallback));
            return success;
        }
        public uint RegisterMessageCallback(nint wim, nint callback)
        {
            RegisteredCallback = callback;
            CallbackReference = new(Marshal.GetDelegateForFunctionPointer<WimMessageCallback>(callback));
            return Call("register") ? 0 : uint.MaxValue;
        }
        public bool UnregisterMessageCallback(nint wim, nint callback) { UnregisteredCallback = callback; return Cleanup("unregister"); }
        public bool CloseHandle(nint handle) => Cleanup(handle == 202 ? "close-image" : "close-wim");
        public bool RegisterLogFile(string path, uint flags) { LogPath = path; return Call("register-log"); }
        public bool UnregisterLogFile(string path) => Cleanup("unregister-log");
        private bool Call(string name) { Calls.Add(name); lastError = 13; return FailAt != name; }
        private bool Cleanup(string name) { Calls.Add(name); lastError = 50; return !FailCleanup; }
    }
}
