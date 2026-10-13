// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeUsbIdentityContinuityTests
{
    private const string SafeDiskJson = """
        {"Number":9,"FriendlyName":"Safe USB","SerialNumber":"SERIAL","UniqueId":"UNIQUE","BusType":"USB","IsSystem":false,"IsBoot":false,"Size":64000000000,"IsOffline":true,"IsReadOnly":true,"PartitionStyle":"GPT"}
        """;

    [Theory]
    [InlineData("", "", 9)]
    [InlineData("SER", "UNI", 9)]
    [InlineData("SERIAL", "UNIQUE", 10)]
    public void ValidateDiskSafety_RejectsNameOnlyPartialOrRenumberedConfirmation(string serial, string uniqueId, int number)
    {
        var options = new UsbOutputOptions
        {
            TargetDiskNumber = number,
            ExpectedDiskFriendlyName = "Safe USB",
            ExpectedDiskSerialNumber = serial,
            ExpectedDiskUniqueId = uniqueId,
            ExpectedDiskBusType = "USB",
            ExpectedDiskSizeBytes = 64000000000
        };
        var disk = new WinPeUsbDiskIdentity
        {
            Number = 9,
            FriendlyName = "Safe USB",
            SerialNumber = "SERIAL",
            UniqueId = "UNIQUE",
            BusType = "USB",
            Size = 64000000000
        };

        WinPeResult result = WinPeUsbMediaService.ValidateDiskSafety(options, disk);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbIdentityMismatch, result.Error?.Code);
        Assert.Equal(WinPeFailureReasons.DiskValidation, result.Error?.FailureReason);
    }

    [Theory]
    [InlineData("provision", "FOUNDRY_DISK_IDENTITY_MISMATCH", WinPeErrorCodes.UsbIdentityMismatch)]
    [InlineData("layout", "FOUNDRY_DISK_IDENTITY_MISMATCH", WinPeErrorCodes.UsbIdentityMismatch)]
    [InlineData("format", "FOUNDRY_DISK_IDENTITY_MISMATCH", WinPeErrorCodes.UsbIdentityMismatch)]
    [InlineData("provision", "FOUNDRY_USB_UNSAFE_TARGET", WinPeErrorCodes.UsbUnsafeTarget)]
    [InlineData("layout", "FOUNDRY_USB_UNSAFE_TARGET", WinPeErrorCodes.UsbUnsafeTarget)]
    [InlineData("format", "FOUNDRY_USB_UNSAFE_TARGET", WinPeErrorCodes.UsbUnsafeTarget)]
    [InlineData("layout", WinPeErrorCodes.UsbBootCapacityUnknown, WinPeErrorCodes.UsbBootCapacityUnknown)]
    [InlineData("format", WinPeErrorCodes.UsbBootCapacityUnknown, WinPeErrorCodes.UsbBootCapacityUnknown)]
    public async Task Workflow_WhenBoundaryRejectsDisk_ReturnsExpectedValidationWithoutIdentifiers(string boundary, string marker, string code)
    {
        var runner = new WorkflowRunner(boundary, "failure", marker);

        WinPeResult<WinPeUsbProvisionResult> result = await RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken);

        Assert.Equal(code, result.Error?.Code);
        Assert.Equal(WinPeFailureKinds.Validation, result.Error?.FailureKind);
        Assert.Equal(WinPeFailureReasons.DiskValidation, result.Error?.FailureReason);
        Assert.DoesNotContain("SERIAL", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIQUE", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.All(runner.Arguments.Skip(1), arguments =>
        {
            Assert.DoesNotContain("EncodedCommand", arguments, StringComparison.Ordinal);
            Assert.DoesNotContain("UNIQUE", arguments, StringComparison.Ordinal);
            Assert.DoesNotContain("SERIAL", arguments, StringComparison.Ordinal);
        });
        Assert.All(runner.ScriptPaths, path => Assert.False(File.Exists(path)));
    }

    public static IEnumerable<object[]> CleanupCases()
    {
        foreach (string boundary in new[] { "provision", "layout", "format" })
        {
            foreach (string outcome in new[] { "success", "failure", "cancel", "throw" })
            {
                yield return [boundary, outcome];
            }
        }
    }

    [Theory]
    [MemberData(nameof(CleanupCases))]
    public async Task Workflow_AlwaysDeletesIdentityBearingScript(string boundary, string outcome)
    {
        var runner = new WorkflowRunner(boundary, outcome, "Synthetic process failure");

        if (outcome == "cancel")
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken));
        }
        else if (outcome == "throw")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken));
        }
        else
        {
            WinPeResult<WinPeUsbProvisionResult> result = await RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("PowerShellProvisioningScript", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("PowerShellBootPartitionUpdateScript", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("UNIQUE", result.Error?.Details ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.NotEmpty(runner.ScriptPaths);
        Assert.All(runner.ScriptPaths, path => Assert.False(File.Exists(path)));
    }

    [Theory]
    [InlineData("provision", true)]
    [InlineData("layout", true)]
    [InlineData("provision", false)]
    [InlineData("layout", false)]
    public async Task Workflow_WhenPrimaryIdIsMissingOrDuplicatedAcrossInventory_DoesNotStartMutationScript(string boundary, bool duplicate)
    {
        var runner = new WorkflowRunner(boundary, "failure", "Unexpected mutation script")
        {
            Inventory = duplicate
                ? $"[{SafeDiskJson},{SafeDiskJson.Replace("\"Number\":9", "\"Number\":10", StringComparison.Ordinal).Replace("\"BusType\":\"USB\"", "\"BusType\":\"NVMe\"", StringComparison.Ordinal)}]"
                : "[]"
        };

        WinPeResult<WinPeUsbProvisionResult> result = await RunWorkflowAsync(boundary, runner, TestContext.Current.CancellationToken);

        Assert.Equal(WinPeErrorCodes.UsbIdentityMismatch, result.Error?.Code);
        Assert.Single(runner.Arguments);
        Assert.Empty(runner.ScriptPaths);
    }

    private static UsbOutputOptions ConfirmedOptions => new()
    {
        TargetDiskNumber = 9,
        ExpectedDiskFriendlyName = "Safe USB",
        ExpectedDiskSerialNumber = "SERIAL",
        ExpectedDiskUniqueId = "UNIQUE",
        ExpectedDiskBusType = "USB",
        ExpectedDiskSizeBytes = 64000000000
    };

    [Theory]
    [InlineData("provision")]
    [InlineData("layout")]
    [InlineData("format")]
    [InlineData("copy")]
    public async Task Workflow_WhenCancelledDuringDiskMutation_FinishesCurrentStageAndStopsBeforeNext(string boundary)
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new WorkflowRunner(boundary, "success", string.Empty)
        {
            OnBoundary = token =>
            {
                cancellation.Cancel();
                Assert.False(token.CanBeCanceled);
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunWorkflowAsync(boundary, runner, cancellation.Token));

        Assert.Equal(boundary is "format" or "copy" ? 3 : 2, runner.Arguments.Count);
        Assert.All(runner.ScriptPaths, path => Assert.False(File.Exists(path)));
    }

    [Theory]
    [InlineData("provision")]
    [InlineData("layout")]
    [InlineData("format")]
    public async Task Workflow_WhenCancellationRacesDiskFailure_PreservesFailure(string boundary)
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new WorkflowRunner(boundary, "failure", "Synthetic disk failure")
        {
            OnBoundary = _ => cancellation.Cancel()
        };

        WinPeResult<WinPeUsbProvisionResult> result = await RunWorkflowAsync(boundary, runner, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("Synthetic disk failure", result.Error?.Details);
        Assert.Equal(boundary == "format" ? 3 : 2, runner.Arguments.Count);
    }

    private static async Task<WinPeResult<WinPeUsbProvisionResult>> RunWorkflowAsync(string boundary, IWinPeProcessRunner runner, CancellationToken cancellationToken)
    {
        string mediaPath = Path.Combine(Path.GetTempPath(), $"foundry-identity-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mediaPath);
        try
        {
            var service = new WinPeUsbMediaService(runner);
            var artifact = new WinPeBuildArtifact { WorkingDirectoryPath = mediaPath, MediaDirectoryPath = mediaPath };
            var tools = new WinPeToolPaths { PowerShellPath = "shadowed" };
            return boundary is "provision" or "copy"
                ? await service.ProvisionAndPopulateAsync(ConfirmedOptions, artifact, tools, false, cancellationToken)
                : await service.UpdateBootPartitionAsync(ConfirmedOptions, artifact, tools, false, cancellationToken);
        }
        finally
        {
            Directory.Delete(mediaPath, true);
        }
    }

    private sealed class WorkflowRunner(string boundary, string outcome, string failure) : IWinPeProcessRunner
    {
        public Action<CancellationToken>? OnBoundary { get; init; }
        public string Inventory { get; init; } = SafeDiskJson;
        public List<string> Arguments { get; } = [];
        public List<string> ScriptPaths { get; } = [];

        public Task<WinPeProcessExecution> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            const string layout = """{"BootPartitionSizeBytes":2147483648,"BootAllocationUnitSizeBytes":4096,"DiskNumber":9,"BootDriveLetter":"S:","CacheDriveLetter":"T:"}""";
            Arguments.Add(arguments);
            int targetCall = boundary is "format" or "copy" ? 3 : 2;
            if (arguments.Contains("-File ", StringComparison.Ordinal))
            {
                string path = arguments[(arguments.IndexOf("-File ", StringComparison.Ordinal) + 6)..].Trim('"');
                Assert.True(File.Exists(path));
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(path)[..3]);
                ScriptPaths.Add(path);
            }

            if (Arguments.Count == targetCall)
            {
                OnBoundary?.Invoke(cancellationToken);
                if (outcome == "cancel")
                {
                    throw new OperationCanceledException();
                }

                if (outcome == "throw")
                {
                    throw new InvalidOperationException("Synthetic process start failure.");
                }
            }

            bool failed = Arguments.Count > targetCall || (Arguments.Count == targetCall && outcome == "failure");
            return Task.FromResult(new WinPeProcessExecution
            {
                FileName = fileName,
                Arguments = arguments,
                ExitCode = failed ? 17 : 0,
                StandardError = failed ? failure : string.Empty,
                StandardOutput = Arguments.Count == 1 ? Inventory : Arguments.Count == 2 ? layout : string.Empty
            });
        }

        public Task<WinPeProcessExecution> RunCmdScriptAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(string scriptPath, string scriptArguments, string workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
