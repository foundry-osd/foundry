using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.Media;

public sealed class MediaPreflightServiceTests
{
    [Fact]
    public void Evaluate_WhenBasicsAreReady_AllowsDryRunButKeepsFinalExecutionDisabled()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(CreateReadyOptions());

        Assert.True(evaluation.CanGenerateIsoSummary);
        Assert.True(evaluation.CanGenerateUsbSummary);
        Assert.False(evaluation.CanCreateIso);
        Assert.False(evaluation.CanCreateUsb);
        Assert.Contains(MediaPreflightBlockingReason.FinalExecutionDeferred, evaluation.IsoBlockingReasons);
        Assert.Contains(MediaPreflightBlockingReason.FinalExecutionDeferred, evaluation.UsbBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenIsoPathIsInvalid_BlocksIsoDryRun()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with { IsoOutputPath = @"C:\Temp\Foundry.txt" });

        Assert.False(evaluation.CanGenerateIsoSummary);
        Assert.Contains(MediaPreflightBlockingReason.InvalidIsoPath, evaluation.IsoBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenUsbCandidateIsMissing_BlocksUsbDryRun()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with { SelectedUsbDisk = null });

        Assert.False(evaluation.CanGenerateUsbSummary);
        Assert.Contains(MediaPreflightBlockingReason.NoUsbTarget, evaluation.UsbBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenAutopilotIsDisabled_DoesNotBlockDryRun()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                IsAutopilotEnabled = false,
                IsAutopilotConfigurationReady = false
            });

        Assert.True(evaluation.CanGenerateIsoSummary);
        Assert.True(evaluation.CanGenerateUsbSummary);
        Assert.DoesNotContain(MediaPreflightBlockingReason.AutopilotConfigurationNotReady, evaluation.IsoBlockingReasons);
        Assert.DoesNotContain(MediaPreflightBlockingReason.AutopilotConfigurationNotReady, evaluation.UsbBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenAutopilotDefaultProfileIsMissing_BlocksDryRun()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                IsAutopilotEnabled = true,
                IsAutopilotConfigurationReady = false
            });

        Assert.False(evaluation.CanGenerateIsoSummary);
        Assert.False(evaluation.CanGenerateUsbSummary);
        Assert.Contains(MediaPreflightBlockingReason.AutopilotConfigurationNotReady, evaluation.IsoBlockingReasons);
        Assert.Contains(MediaPreflightBlockingReason.AutopilotConfigurationNotReady, evaluation.UsbBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenWinPeLanguageIsNotAvailableForArchitecture_BlocksDryRun()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                WinPeLanguage = "fr-FR",
                AvailableWinPeLanguages = ["en-US"]
            });

        Assert.False(evaluation.CanGenerateIsoSummary);
        Assert.False(evaluation.CanGenerateUsbSummary);
        Assert.Contains(MediaPreflightBlockingReason.WinPeLanguageUnavailable, evaluation.IsoBlockingReasons);
        Assert.Contains(MediaPreflightBlockingReason.WinPeLanguageUnavailable, evaluation.UsbBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenArchitectureIsArm64_EnforcesGptPartitionStyle()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                Architecture = WinPeArchitecture.Arm64,
                UsbPartitionStyle = UsbPartitionStyle.Mbr
            });

        Assert.Equal(UsbPartitionStyle.Gpt, evaluation.EffectiveUsbPartitionStyle);
        Assert.Contains(MediaPreflightBlockingReason.Arm64RequiresGpt, evaluation.UsbBlockingReasons);
    }

    private static MediaPreflightOptions CreateReadyOptions()
    {
        return new MediaPreflightOptions
        {
            IsAdkReady = true,
            IsRuntimePayloadReady = true,
            IsNetworkConfigurationReady = true,
            IsDeployConfigurationReady = true,
            IsConnectProvisioningReady = true,
            AreRequiredSecretsReady = true,
            IsoOutputPath = @"C:\Temp\Foundry.iso",
            WinPeLanguage = "en-US",
            Architecture = WinPeArchitecture.X64,
            UsbPartitionStyle = UsbPartitionStyle.Gpt,
            UsbFormatMode = UsbFormatMode.Quick,
            SelectedUsbDisk = new WinPeUsbDiskCandidate
            {
                DiskNumber = 3,
                FriendlyName = "Safe USB",
                SerialNumber = "USB123",
                UniqueId = "USB-ID",
                BusType = "USB",
                IsRemovable = true,
                SizeBytes = 64_000_000_000
            }
        };
    }
}
