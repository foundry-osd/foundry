// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.Sharing;
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

    [Theory]
    [InlineData(SharedContentSyncState.Syncing, MediaPreflightBlockingReason.SharedContentSyncing)]
    [InlineData(SharedContentSyncState.Failed, MediaPreflightBlockingReason.SharedContentFailed)]
    [InlineData(SharedContentSyncState.Stalled, MediaPreflightBlockingReason.SharedContentStalled)]
    public void Evaluate_WhenSharedContentIsNotSynced_BlocksEveryMediaTarget(SharedContentSyncState state, MediaPreflightBlockingReason reason)
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with { IsFinalExecutionEnabled = true, SharedContentState = state });

        Assert.False(evaluation.CanCreateIso);
        Assert.False(evaluation.CanCreateUsb);
        Assert.False(evaluation.CanCreateUsbWithoutCache);
        Assert.False(evaluation.CanCreatePxe);
        Assert.Contains(reason, evaluation.IsoBlockingReasons);
        Assert.Contains(reason, evaluation.UsbBlockingReasons);
        Assert.Contains(reason, evaluation.UsbWithoutCacheBlockingReasons);
        Assert.Contains(reason, evaluation.PxeBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenSharedContentIsSynced_AllowsEveryMediaTarget()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with { IsFinalExecutionEnabled = true, SharedContentState = SharedContentSyncState.Synced });

        Assert.True(evaluation.CanCreateIso);
        Assert.True(evaluation.CanCreateUsb);
        Assert.True(evaluation.CanCreateUsbWithoutCache);
        Assert.True(evaluation.CanCreatePxe);
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
    public void Evaluate_WhenIsoPathIsInvalid_StillAllowsPxe()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with { IsoOutputPath = string.Empty, IsFinalExecutionEnabled = true });

        Assert.False(evaluation.CanCreateIso);
        Assert.True(evaluation.CanCreatePxe);
        Assert.Empty(evaluation.PxeBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenUsbTargetIsBelowTheCacheMinimum_StillAllowsUsbWithoutCache()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                IsFinalExecutionEnabled = true,
                SelectedUsbDisk = CreateReadyOptions().SelectedUsbDisk! with { SizeBytes = 8_000_000_000UL }
            });

        Assert.False(evaluation.CanCreateUsb);
        Assert.True(evaluation.CanCreateUsbWithoutCache);
        Assert.Empty(evaluation.UsbWithoutCacheBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenUsbTargetIsBelowOneGibibyte_BlocksUsbWithoutCache()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                IsFinalExecutionEnabled = true,
                SelectedUsbDisk = CreateReadyOptions().SelectedUsbDisk! with { SizeBytes = 512UL * 1024 * 1024 }
            });

        Assert.False(evaluation.CanCreateUsbWithoutCache);
        Assert.Contains(MediaPreflightBlockingReason.UsbTargetBelowMinimumSizeWithoutCache, evaluation.UsbWithoutCacheBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenNoUsbTargetIsSelected_BlocksUsbWithoutCache()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with { IsFinalExecutionEnabled = true, SelectedUsbDisk = null });

        Assert.False(evaluation.CanCreateUsbWithoutCache);
        Assert.Contains(MediaPreflightBlockingReason.NoUsbTarget, evaluation.UsbWithoutCacheBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenCustomDriverDirectoryIsMissing_BlocksPxe()
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                IsFinalExecutionEnabled = true,
                CustomDriverDirectoryPath = Path.Combine(Path.GetTempPath(), $"foundry-missing-{Guid.NewGuid():N}")
            });

        Assert.False(evaluation.CanCreatePxe);
        Assert.Contains(MediaPreflightBlockingReason.CustomDriverDirectoryNotFound, evaluation.PxeBlockingReasons);
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

    [Theory]
    [InlineData(WinPeArchitecture.X64, "arm64")]
    [InlineData(WinPeArchitecture.X64, "x86")]
    [InlineData(WinPeArchitecture.Arm64, "x64")]
    [InlineData(WinPeArchitecture.Arm64, "x86")]
    public void Evaluate_WhenIncludedCustomImageTargetsAnotherArchitecture_BlocksMedia(
        WinPeArchitecture mediaArchitecture,
        string imageArchitecture)
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                Architecture = mediaArchitecture,
                CustomImages = CreateCustomImages(imageArchitecture),
                IsFinalExecutionEnabled = true
            });

        Assert.False(evaluation.CanCreateIso);
        Assert.False(evaluation.CanCreateUsb);
        Assert.Contains(MediaPreflightBlockingReason.CustomImageArchitectureMismatch, evaluation.IsoBlockingReasons);
        Assert.Contains(MediaPreflightBlockingReason.CustomImageArchitectureMismatch, evaluation.UsbBlockingReasons);
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "x64")]
    [InlineData(WinPeArchitecture.Arm64, "arm64")]
    [InlineData(WinPeArchitecture.Arm64, "ARM64")]
    [InlineData(WinPeArchitecture.X64, "unknown")]
    [InlineData(WinPeArchitecture.Arm64, "")]
    public void Evaluate_WhenIncludedCustomImageMatchesOrHasUnknownArchitecture_AllowsMedia(
        WinPeArchitecture mediaArchitecture,
        string imageArchitecture)
    {
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                Architecture = mediaArchitecture,
                CustomImages = CreateCustomImages(imageArchitecture),
                IsFinalExecutionEnabled = true
            });

        Assert.True(evaluation.CanCreateIso);
        Assert.DoesNotContain(MediaPreflightBlockingReason.CustomImageArchitectureMismatch, evaluation.IsoBlockingReasons);
        Assert.DoesNotContain(MediaPreflightBlockingReason.CustomImageArchitectureMismatch, evaluation.UsbBlockingReasons);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Evaluate_WhenMismatchedCustomImageIsNotOnMedia_IgnoresIt(bool isEnabled, bool isIncluded)
    {
        CustomImagesSettings customImages = CreateCustomImages("arm64", isIncluded) with { IsEnabled = isEnabled };

        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                Architecture = WinPeArchitecture.X64,
                CustomImages = customImages
            });

        Assert.DoesNotContain(MediaPreflightBlockingReason.CustomImageArchitectureMismatch, evaluation.IsoBlockingReasons);
        Assert.DoesNotContain(MediaPreflightBlockingReason.CustomImageArchitectureMismatch, evaluation.UsbBlockingReasons);
    }

    [Fact]
    public void Evaluate_WhenAnyIndexOfIncludedCustomImageTargetsAnotherArchitecture_BlocksMedia()
    {
        CustomImagesSettings customImages = CreateCustomImages("x64");
        CustomImageReference image = customImages.Images[0];
        customImages = customImages with
        {
            Images = [image with { Indexes = [.. image.Indexes, new CustomImageIndex { Index = 2, Architecture = "arm64" }] }]
        };

        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(
            CreateReadyOptions() with
            {
                Architecture = WinPeArchitecture.X64,
                CustomImages = customImages
            });

        Assert.Contains(MediaPreflightBlockingReason.CustomImageArchitectureMismatch, evaluation.IsoBlockingReasons);
    }

    private static CustomImagesSettings CreateCustomImages(string architecture, bool isIncluded = true)
    {
        return new CustomImagesSettings
        {
            IsEnabled = true,
            Images =
            [
                new CustomImageReference
                {
                    Id = "image",
                    DisplayName = "Image",
                    IsIncluded = isIncluded,
                    Indexes = [new CustomImageIndex { Index = 1, Architecture = architecture }]
                }
            ]
        };
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
