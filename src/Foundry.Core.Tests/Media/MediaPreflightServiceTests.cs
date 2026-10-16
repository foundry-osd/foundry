// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

    [Theory]
    [InlineData(8_000_000_000UL)]
    [InlineData(13_999_999_999UL)]
    public void Evaluate_WhenUsbCandidateIsBelowMinimumSize_BlocksUsbCreationOnly(ulong sizeBytes)
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                SelectedUsbDisk = new WinPeUsbDiskCandidate
                {
                    DiskNumber = 3,
                    FriendlyName = "Small USB",
                    SerialNumber = "USB123",
                    UniqueId = "USB-ID",
                    BusType = "USB",
                    SizeBytes = sizeBytes
                },
                IsFinalExecutionEnabled = true
            });

        Assert.True(evaluation.CanCreateIso);
        Assert.False(evaluation.CanGenerateUsbSummary);
        Assert.False(evaluation.CanCreateUsb);
        Assert.Contains(MediaPreflightBlockingReason.UsbTargetBelowMinimumSize, evaluation.UsbBlockingReasons);
        Assert.DoesNotContain(MediaPreflightBlockingReason.UsbTargetBelowMinimumSize, evaluation.IsoBlockingReasons);
    }

    // Drives sold as 16 GB hold fewer than 16 GiB; the minimum accepts them.
    [Theory]
    [InlineData(14_000_000_000UL)]
    [InlineData(15_376_000_000UL)]
    [InlineData(16_000_000_000UL)]
    public void Evaluate_WhenUsbCandidateIsSoldAs16Gb_DoesNotBlockOnSize(ulong sizeBytes)
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                SelectedUsbDisk = new WinPeUsbDiskCandidate
                {
                    DiskNumber = 3,
                    FriendlyName = "USB sold as 16 GB",
                    SerialNumber = "USB123",
                    UniqueId = "USB-ID",
                    BusType = "USB",
                    SizeBytes = sizeBytes
                },
                IsFinalExecutionEnabled = true
            });

        Assert.DoesNotContain(MediaPreflightBlockingReason.UsbTargetBelowMinimumSize, evaluation.UsbBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenFinalExecutionIsEnabled_AllowsMediaCreationWithoutRuntimePayloadChecks()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                IsFinalExecutionEnabled = true
            });

        Assert.True(evaluation.CanCreateIso);
        Assert.True(evaluation.CanCreateUsb);
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
    public void Evaluate_WhenKeyboardLayoutNotInstalled_BlocksMedia()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                WinPeKeyboardLayoutId = "00000813",
                AvailableKeyboardLayoutIds = ["0000040C"]
            });

        Assert.Contains(MediaPreflightBlockingReason.WinPeKeyboardLayoutUnavailable, evaluation.IsoBlockingReasons);
        Assert.Contains(MediaPreflightBlockingReason.WinPeKeyboardLayoutUnavailable, evaluation.UsbBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenKeyboardLayoutIsSetButNoLayoutIsKnown_BlocksMedia()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                WinPeKeyboardLayoutId = "00000813",
                AvailableKeyboardLayoutIds = []
            });

        Assert.Contains(MediaPreflightBlockingReason.WinPeKeyboardLayoutUnavailable, evaluation.IsoBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenKeyboardLayoutIsNull_DoesNotBlock()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                AvailableKeyboardLayoutIds = ["0000040C"]
            });

        Assert.DoesNotContain(MediaPreflightBlockingReason.WinPeKeyboardLayoutUnavailable, evaluation.IsoBlockingReasons);
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
                SizeBytes = 64_000_000_000
            }
        };
    }
}
