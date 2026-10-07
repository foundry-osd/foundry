// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Native;
using Foundry.Deploy.Services.Deployment.Native.Servicing;

namespace Foundry.Deploy.Tests;

public sealed class NativeDismOperationsTests
{
    private const int AccessDenied = unchecked((int)0x80070005);

    [Fact]
    public void Probe_OnlyChecksExportsWithoutInitializing()
    {
        using var api = new FakeDismApi();
        var operations = new NativeDismOperations(api);

        NativeWorkerResult result = operations.Execute(new() { Operation = NativeWorkerOperation.ProbeDism }, _ => { });

        Assert.Empty(result.Features);
        Assert.Equal(["probe"], api.Calls);
    }

    [Fact]
    public void ReadFeatures_CopiesPackedUnicodeStatesBeforeReleasingResources()
    {
        using var api = new FakeDismApi();
        api.SetFeatures(("Absent", 0), ("Staged", 2), ("Removed", 3), ("Enabled", 4), ("InstallPending", 5), ("停用", 1));

        NativeWorkerResult result = new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.ReadFeatures), _ => { });

        Assert.Equal(new NativeWindowsFeature[]
        {
            new("Absent", OfflineWindowsFeatureState.Disabled),
            new("Staged", OfflineWindowsFeatureState.Disabled),
            new("Removed", OfflineWindowsFeatureState.PayloadRemoved),
            new("Enabled", OfflineWindowsFeatureState.Enabled),
            new("InstallPending", OfflineWindowsFeatureState.EnablePending),
            new("停用", OfflineWindowsFeatureState.DisablePending)
        }, result.Features);
        Assert.Equal((2u, "owned.log", "owned-scratch"), api.Options);
        Assert.Equal(["initialize", "open:Windows", "features", "delete.features", "close", "shutdown"], api.Calls);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(98)]
    public void ReadFeatures_RejectsUnknownStatesAndStillFreesClosesAndShutsDown(int state)
    {
        using var api = new FakeDismApi();
        api.SetFeatures(("Ambiguous", state));

        Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.ReadFeatures), _ => { }));

        Assert.Equal(["initialize", "open:Windows", "features", "delete.features", "close", "shutdown"], api.Calls);
    }

    [Fact]
    public void ReadFeatures_RejectsNonzeroCountWithNullBuffer()
    {
        using var api = new FakeDismApi { FeatureCount = 1 };

        Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.ReadFeatures), _ => { }));

        Assert.Equal(["initialize", "open:Windows", "features", "close", "shutdown"], api.Calls);
    }

    [Fact]
    public void NativeFailure_CapturesDetailBeforeBufferSessionAndRuntimeCleanup()
    {
        using var api = new FakeDismApi { FeatureResult = AccessDenied };
        api.SetFeatures(("Feature", 4));

        NativeOperationException failure = Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.ReadFeatures), _ => { }));

        Assert.Equal("DismGetFeatures", failure.Function);
        Assert.Equal(AccessDenied, failure.ErrorCode);
        Assert.Contains("native detail", failure.Message);
        Assert.Equal(api.FeatureCallingThread, api.ErrorCallingThread);
        Assert.Equal(["initialize", "open:Windows", "features", "last-error", "delete.error", "delete.features", "close", "shutdown"], api.Calls);
    }

    [Fact]
    public void CleanupFailures_DoNotReplacePrimaryFailureAndAllCleanupContinues()
    {
        using var api = new FakeDismApi { FeatureResult = AccessDenied, CloseResult = AccessDenied, ShutdownResult = AccessDenied, FeatureDeleteResult = AccessDenied };
        api.SetFeatures(("Feature", 4));

        NativeOperationException failure = Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.ReadFeatures), _ => { }));

        Assert.Equal("DismGetFeatures", failure.Function);
        Assert.Equal(3, failure.CleanupErrors.Count);
        Assert.Contains(failure.CleanupErrors, message => message.Contains("DismDelete", StringComparison.Ordinal));
        Assert.Contains(failure.CleanupErrors, message => message.Contains("DismCloseSession", StringComparison.Ordinal));
        Assert.Contains(failure.CleanupErrors, message => message.Contains("DismShutdown", StringComparison.Ordinal));
        Assert.Equal("shutdown", api.Calls[^1]);
    }

    [Fact]
    public void FailedInitialization_DoesNotAttemptSessionOrShutdown()
    {
        using var api = new FakeDismApi { InitializeResult = AccessDenied };

        NativeOperationException failure = Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.ReadFeatures), _ => { }));

        Assert.Equal("DismInitialize", failure.Function);
        Assert.Equal(["initialize", "last-error", "delete.error"], api.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3010)]
    public void DisableFeature_PreservesPayloadAndAcceptsOnlyDocumentedMutationSuccess(int result)
    {
        using var api = new FakeDismApi { DisableResult = result };

        new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.DisableFeature) with { FeatureName = "Target" }, _ => { });

        Assert.Equal(("Target", false), api.DisableOptions);
        Assert.Equal(["initialize", "open:Windows", "disable", "close", "shutdown"], api.Calls);
    }

    [Fact]
    public void DisableFeature_RejectsUndocumentedPositiveResultWithoutReplaying()
    {
        using var api = new FakeDismApi { DisableResult = 2 };

        NativeOperationException failure = Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.DisableFeature), _ => { }));

        Assert.Equal(2, failure.ErrorCode);
        Assert.Equal(1, api.Calls.Count(call => call == "disable"));
        Assert.Equal("shutdown", api.Calls[^1]);
    }

    [Fact]
    public void AddDrivers_RecursesInDeterministicOrderKeepsSigningPolicyAndReportsCompletedCount()
    {
        using var directory = new DriverDirectory();
        directory.Create("nested/z.inf");
        directory.Create("a.inf");
        directory.Create("ignored.txt");
        using var api = new FakeDismApi();
        var messages = new List<NativeWorkerMessage>();

        new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.AddDrivers) with { DriverRoot = directory.Path }, messages.Add);

        Assert.Equal(["a.inf", "nested/z.inf"], api.Drivers.Select(driver => System.IO.Path.GetRelativePath(directory.Path, driver.Path).Replace('\\', '/')));
        Assert.All(api.Drivers, driver => Assert.False(driver.ForceUnsigned));
        Assert.Equal(new double?[] { 50d, 100d }, messages.Select(message => message.Percent));
        Assert.Equal("close", api.Calls[^2]);
        Assert.Equal("shutdown", api.Calls[^1]);
    }

    [Fact]
    public void AddDrivers_ContinuesPastRejectedInfAndReportsIt()
    {
        using var directory = new DriverDirectory();
        directory.Create("a.inf");
        directory.Create("b.inf");
        directory.Create("c.inf");
        using var api = new FakeDismApi { FailDriverAt = 2 };
        var messages = new List<NativeWorkerMessage>();

        NativeWorkerResult result = new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.AddDrivers) with { DriverRoot = directory.Path }, messages.Add);

        Assert.Equal(3, api.Drivers.Count);
        Assert.Equal(2, result.DriversAdded);
        NativeDriverFailure rejected = Assert.Single(result.DriverFailures);
        Assert.Equal(Path.Combine(directory.Path, "b.inf"), rejected.InfPath);
        Assert.Equal(AccessDenied, rejected.ErrorCode);
        Assert.Contains("native detail", rejected.Message);
        Assert.Equal(3, messages.Count);
        Assert.Equal("close", api.Calls[^2]);
        Assert.Equal("shutdown", api.Calls[^1]);
    }

    [Fact]
    public void AddDrivers_FailsWhenEveryInfIsRejected()
    {
        using var directory = new DriverDirectory();
        directory.Create("a.inf");
        directory.Create("b.inf");
        using var api = new FakeDismApi { FailEveryDriver = true };

        NativeOperationException failure = Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(
            Request(NativeWorkerOperation.AddDrivers) with { DriverRoot = directory.Path }, _ => { }));

        Assert.Equal("DismAddDriver", failure.Function);
        Assert.Equal(AccessDenied, failure.ErrorCode);
        Assert.Equal(2, api.Drivers.Count);
        Assert.Equal("close", api.Calls[^2]);
        Assert.Equal("shutdown", api.Calls[^1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3010)]
    public void AddDrivers_AcceptsDocumentedMutationSuccess(int result)
    {
        using var directory = new DriverDirectory();
        directory.Create("a.inf");
        using var api = new FakeDismApi { DriverSuccessResult = result };

        NativeWorkerResult outcome = new NativeDismOperations(api).Execute(
            Request(NativeWorkerOperation.AddDrivers) with { DriverRoot = directory.Path }, _ => { });

        Assert.Equal(1, outcome.DriversAdded);
        Assert.Empty(outcome.DriverFailures);
    }

    [Fact]
    public void AddDrivers_RejectsEmptyPayloadBeforeOpeningSession()
    {
        using var directory = new DriverDirectory();
        directory.Create("readme.txt");
        using var api = new FakeDismApi();

        Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(
            Request(NativeWorkerOperation.AddDrivers) with { DriverRoot = directory.Path }, _ => { }));

        Assert.Equal(["initialize", "shutdown"], api.Calls);
    }

    [Fact]
    public void AddDrivers_RejectsMissingPayloadAndStillShutsDown()
    {
        using var directory = new DriverDirectory();
        using var api = new FakeDismApi();

        Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(
            Request(NativeWorkerOperation.AddDrivers) with { DriverRoot = Path.Combine(directory.Path, "missing") }, _ => { }));

        Assert.Equal(["initialize", "shutdown"], api.Calls);
    }

    [Fact]
    public void AddDrivers_DoesNotFollowJunctionOutsideStagedRoot()
    {
        using var staged = new DriverDirectory();
        using var external = new DriverDirectory();
        staged.Create("owned.inf");
        external.Create("escape.inf");
        string junction = Path.Combine(staged.Path, "external");
        CreateJunction(junction, external.Path);
        try
        {
            using var api = new FakeDismApi();

            new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.AddDrivers) with { DriverRoot = staged.Path }, _ => { });

            Assert.Equal(Path.Combine(staged.Path, "owned.inf"), Assert.Single(api.Drivers).Path);
            Assert.True(File.Exists(Path.Combine(external.Path, "escape.inf")));
        }
        finally { Directory.Delete(junction); }
    }

    [Fact]
    public void AddDrivers_RejectsJunctionAsStagedRoot()
    {
        using var staged = new DriverDirectory();
        using var external = new DriverDirectory();
        external.Create("escape.inf");
        string junction = Path.Combine(staged.Path, "external");
        CreateJunction(junction, external.Path);
        try
        {
            using var api = new FakeDismApi();

            Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(
                Request(NativeWorkerOperation.AddDrivers) with { DriverRoot = junction }, _ => { }));

            Assert.Equal(["initialize", "shutdown"], api.Calls);
        }
        finally { Directory.Delete(junction); }
    }

    [Fact]
    public void ProgressException_IsObservedOnlyAfterNativeCallReturnsThenCleanupRuns()
    {
        using var api = new FakeDismApi { InvokeProgress = true };

        NativeOperationException failure = Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.DisableFeature), _ => throw new IOException("output failed")));

        Assert.True(api.CallbackReturned);
        Assert.IsType<IOException>(failure.InnerException);
        Assert.Equal(["initialize", "open:Windows", "disable", "close", "shutdown"], api.Calls);
    }

    [Theory]
    [InlineData(false, 1u)]
    [InlineData(true, 0u)]
    public void Unmount_UsesExplicitCommitOrDiscardWithoutOpeningSession(bool commit, uint flags)
    {
        using var api = new FakeDismApi();

        new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.UnmountImage) with { MountPath = "owned-mount", Commit = commit }, _ => { });

        Assert.Equal(("owned-mount", flags), api.UnmountOptions);
        Assert.Equal(["initialize", "unmount", "shutdown"], api.Calls);
    }

    [Fact]
    public void Mount_UsesWritableSelectedIndexAndSafeNumericProgress()
    {
        using var api = new FakeDismApi { InvokeProgress = true };
        var messages = new List<NativeWorkerMessage>();

        new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.MountImage) with { ImagePath = "winre.wim", MountPath = "owned-mount", ImageIndex = 3 }, messages.Add);

        Assert.Equal(("winre.wim", 3u, "owned-mount", 0u), api.MountOptions);
        Assert.Equal(25d, Assert.Single(messages).Percent);
        Assert.Equal(["initialize", "mount", "shutdown"], api.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InspectMount_AnyStatusRetainsRegisteredOwnedPath(int status)
    {
        using var api = new FakeDismApi();
        api.SetMount("C:\\Owned-Mount\\", "winre.wim", status);

        NativeWorkerResult result = new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.InspectMount) with { MountPath = "c:\\owned-mount" }, _ => { });

        Assert.True(result.MountRegistered);
        Assert.Equal(["initialize", "mounts", "delete.mounts", "shutdown"], api.Calls);
    }

    [Fact]
    public void InspectMount_FailedInventoryNeverReturnsUnregistered()
    {
        using var api = new FakeDismApi { MountedResult = AccessDenied };

        Assert.Throws<NativeOperationException>(() => new NativeDismOperations(api).Execute(Request(NativeWorkerOperation.InspectMount), _ => { }));
    }

    private static NativeWorkerRequest Request(NativeWorkerOperation operation) => new()
    {
        Operation = operation,
        WindowsRoot = "Windows",
        ScratchDirectory = "owned-scratch",
        LogFilePath = "owned.log"
    };

    private static void CreateJunction(string path, string target)
    {
        // Windows junctions need no symlink privilege and keep this filesystem boundary test usable in CI.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "Junction creation did not finish.");
        Assert.True(process.ExitCode == 0, $"Junction creation failed: {output} {error}");
    }

    private sealed class DriverDirectory : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("foundry-native-drivers-").FullName;

        internal void Create(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "test payload");
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FakeDismApi : IDismNativeApi, IDisposable
    {
        private readonly List<IntPtr> strings = [];
        private IntPtr features;
        private IntPtr mounts;
        private IntPtr error;
        internal List<string> Calls { get; } = [];
        internal List<(string Path, bool ForceUnsigned)> Drivers { get; } = [];
        internal (uint Level, string? Log, string Scratch) Options { get; private set; }
        internal (string Name, bool RemovePayload) DisableOptions { get; private set; }
        internal (string Mount, uint Flags) UnmountOptions { get; private set; }
        internal (string Image, uint Index, string Mount, uint Flags) MountOptions { get; private set; }
        internal int InitializeResult { get; init; }
        internal int FeatureResult { get; init; }
        internal int FeatureDeleteResult { get; init; }
        internal int CloseResult { get; init; }
        internal int ShutdownResult { get; init; }
        internal int DisableResult { get; init; }
        internal int MountedResult { get; init; }
        internal int FailDriverAt { get; init; }
        internal bool FailEveryDriver { get; init; }
        internal int DriverSuccessResult { get; init; }
        internal bool InvokeProgress { get; init; }
        internal bool CallbackReturned { get; private set; }
        internal int FeatureCallingThread { get; private set; }
        internal int ErrorCallingThread { get; private set; }
        internal uint FeatureCount { get; set; }

        internal void SetFeatures(params (string Name, int State)[] entries)
        {
            // Independent SDK layout: packed PCWSTR + four-byte enum, no trailing pointer alignment.
            int size = IntPtr.Size + 4;
            features = Marshal.AllocHGlobal(entries.Length * size);
            FeatureCount = (uint)entries.Length;
            for (int index = 0; index < entries.Length; index++)
            {
                Marshal.WriteIntPtr(features, index * size, AddString(entries[index].Name));
                Marshal.WriteInt32(features, (index * size) + IntPtr.Size, entries[index].State);
            }
        }

        internal void SetMount(string path, string imagePath, int status)
        {
            mounts = Marshal.AllocHGlobal((2 * IntPtr.Size) + 12);
            Marshal.WriteIntPtr(mounts, 0, AddString(path));
            Marshal.WriteIntPtr(mounts, IntPtr.Size, AddString(imagePath));
            Marshal.WriteInt32(mounts, 2 * IntPtr.Size, 1);
            Marshal.WriteInt32(mounts, (2 * IntPtr.Size) + 4, 0);
            Marshal.WriteInt32(mounts, (2 * IntPtr.Size) + 8, status);
        }

        public void EnsureAvailable() => Calls.Add("probe");
        public int Initialize(uint level, string? log, string scratch) { Calls.Add("initialize"); Options = (level, log, scratch); return InitializeResult; }
        public int Shutdown() { Calls.Add("shutdown"); return ShutdownResult; }
        public int OpenSession(string root, out uint session) { Calls.Add($"open:{root}"); session = 27; return 0; }
        public int CloseSession(uint session) { Assert.Equal(27u, session); Calls.Add("close"); return CloseResult; }
        public int GetFeatures(uint session, out IntPtr buffer, out uint count) { Assert.Equal(27u, session); FeatureCallingThread = Environment.CurrentManagedThreadId; Calls.Add("features"); buffer = features; count = FeatureCount; return FeatureResult; }
        public int DisableFeature(uint session, string name, bool remove, DismProgressCallback callback) { Assert.Equal(27u, session); Calls.Add("disable"); DisableOptions = (name, remove); Invoke(callback); return DisableResult; }
        public int AddDriver(uint session, string path, bool force)
        {
            Assert.Equal(27u, session);
            Calls.Add("driver");
            Drivers.Add((path, force));
            return FailEveryDriver || Drivers.Count == FailDriverAt ? AccessDenied : DriverSuccessResult;
        }
        public int MountImage(string path, uint index, string mount, uint flags, DismProgressCallback callback) { Calls.Add("mount"); MountOptions = (path, index, mount, flags); Invoke(callback); return 0; }
        public int UnmountImage(string mount, uint flags, DismProgressCallback callback) { Calls.Add("unmount"); UnmountOptions = (mount, flags); Invoke(callback); return 0; }
        public int GetMountedImages(out IntPtr buffer, out uint count) { Calls.Add("mounts"); buffer = mounts; count = mounts == IntPtr.Zero ? 0u : 1u; return MountedResult; }
        public int GetLastErrorMessage(out IntPtr message)
        {
            Calls.Add("last-error");
            ErrorCallingThread = Environment.CurrentManagedThreadId;
            error = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(error, AddString("native detail"));
            message = error;
            return 0;
        }
        public int Delete(IntPtr allocation)
        {
            bool feature = allocation == features;
            if (allocation == error) { Calls.Add("delete.error"); error = IntPtr.Zero; }
            else if (feature) { Calls.Add("delete.features"); features = IntPtr.Zero; }
            else if (allocation == mounts) { Calls.Add("delete.mounts"); mounts = IntPtr.Zero; }
            else throw new InvalidOperationException("Unknown allocation.");
            Marshal.FreeHGlobal(allocation);
            return feature ? FeatureDeleteResult : 0;
        }
        private void Invoke(DismProgressCallback callback)
        {
            if (!InvokeProgress) return;
            callback(1, 4, IntPtr.Zero);
            CallbackReturned = true;
        }
        private IntPtr AddString(string value) { IntPtr pointer = Marshal.StringToHGlobalUni(value); strings.Add(pointer); return pointer; }
        public void Dispose()
        {
            if (features != IntPtr.Zero) Marshal.FreeHGlobal(features);
            if (mounts != IntPtr.Zero) Marshal.FreeHGlobal(mounts);
            if (error != IntPtr.Zero) Marshal.FreeHGlobal(error);
            foreach (IntPtr value in strings) Marshal.FreeHGlobal(value);
        }
    }
}
