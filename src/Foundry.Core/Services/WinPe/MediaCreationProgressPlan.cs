// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Identifies the boot media output whose creation progress is reported.
/// </summary>
public enum MediaCreationKind
{
    /// <summary>An ISO file is created.</summary>
    Iso,

    /// <summary>A USB drive is formatted and populated.</summary>
    UsbCreate,

    /// <summary>The boot partition of an existing Foundry USB drive is updated.</summary>
    UsbUpdate,

    /// <summary>The network boot files are written to a folder.</summary>
    Pxe
}

/// <summary>
/// Identifies a user-visible phase of boot media creation.
/// </summary>
public enum MediaCreationPhase
{
    /// <summary>Workspace recovery, WinPE workspace creation and source preparation.</summary>
    Preparing,

    /// <summary>Driver package resolution and download.</summary>
    DownloadingDrivers,

    /// <summary>Boot image servicing, from mount to commit and signature policy evaluation.</summary>
    CustomizingBootImage,

    /// <summary>ISO output creation.</summary>
    CreatingIso,

    /// <summary>USB drive partitioning and formatting.</summary>
    FormattingUsbDrive,

    /// <summary>Content copy to a newly formatted USB drive.</summary>
    CopyingToUsbDrive,

    /// <summary>Boot partition update of an existing USB drive.</summary>
    UpdatingUsbDrive,

    /// <summary>Network boot file copy to the output folder.</summary>
    CreatingPxeFiles,

    /// <summary>Workspace cleanup after the output is written.</summary>
    Finishing
}

/// <summary>
/// Describes overall media creation progress as a phase, its 1-based step position and a global percentage.
/// </summary>
/// <param name="Phase">Current user-visible phase.</param>
/// <param name="StepNumber">1-based position of <paramref name="Phase"/> in the plan.</param>
/// <param name="StepCount">Number of phases in the plan.</param>
/// <param name="Percent">Global percentage in the <c>0..100</c> range.</param>
public readonly record struct MediaCreationProgress(MediaCreationPhase Phase, int StepNumber, int StepCount, int Percent);

/// <summary>
/// Maps phase-local progress to a weighted global percentage that never decreases, so nested operations that
/// restart their own percentage cannot move the overall progress backwards.
/// </summary>
/// <remarks>Instances are stateful and belong to one media creation run.</remarks>
public sealed class MediaCreationProgressPlan
{
    /// <summary>
    /// Final-media percentage at which USB creation finishes partitioning and formatting and starts copying content.
    /// </summary>
    public const int UsbFormattingCompletePercent = 55;

    private readonly MediaCreationKind kind;
    private readonly (MediaCreationPhase Phase, int Weight)[] phases;

    private MediaCreationProgressPlan(MediaCreationKind kind, (MediaCreationPhase Phase, int Weight)[] phases)
    {
        this.kind = kind;
        this.phases = phases;
        Current = new MediaCreationProgress(phases[0].Phase, 1, phases.Length, 0);
    }

    /// <summary>
    /// Gets the latest reported progress.
    /// </summary>
    public MediaCreationProgress Current { get; private set; }

    /// <summary>
    /// Creates the plan for a media output. Weights approximate the relative duration of each phase.
    /// </summary>
    /// <param name="kind">Media output being created.</param>
    /// <returns>A new plan positioned at the start of its first phase.</returns>
    public static MediaCreationProgressPlan For(MediaCreationKind kind)
    {
        (MediaCreationPhase, int)[] phases = kind switch
        {
            MediaCreationKind.Iso =>
            [
                (MediaCreationPhase.Preparing, 8),
                (MediaCreationPhase.DownloadingDrivers, 10),
                (MediaCreationPhase.CustomizingBootImage, 52),
                (MediaCreationPhase.CreatingIso, 25),
                (MediaCreationPhase.Finishing, 5)
            ],
            MediaCreationKind.UsbCreate =>
            [
                (MediaCreationPhase.Preparing, 8),
                (MediaCreationPhase.DownloadingDrivers, 10),
                (MediaCreationPhase.CustomizingBootImage, 42),
                (MediaCreationPhase.FormattingUsbDrive, 10),
                (MediaCreationPhase.CopyingToUsbDrive, 25),
                (MediaCreationPhase.Finishing, 5)
            ],
            MediaCreationKind.UsbUpdate =>
            [
                (MediaCreationPhase.Preparing, 8),
                (MediaCreationPhase.DownloadingDrivers, 10),
                (MediaCreationPhase.CustomizingBootImage, 47),
                (MediaCreationPhase.UpdatingUsbDrive, 30),
                (MediaCreationPhase.Finishing, 5)
            ],
            MediaCreationKind.Pxe =>
            [
                (MediaCreationPhase.Preparing, 8),
                (MediaCreationPhase.DownloadingDrivers, 10),
                (MediaCreationPhase.CustomizingBootImage, 67),
                (MediaCreationPhase.CreatingPxeFiles, 10),
                (MediaCreationPhase.Finishing, 5)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported media creation kind.")
        };

        return new MediaCreationProgressPlan(kind, phases);
    }

    /// <summary>
    /// Reports progress inside a phase. An earlier phase or a lower value keeps the current progress.
    /// </summary>
    /// <param name="phase">Phase that reports progress; it must belong to this plan.</param>
    /// <param name="phasePercent">Progress inside <paramref name="phase"/>, clamped to <c>0..100</c>.</param>
    /// <returns>The resulting overall progress.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="phase"/> is not part of this plan.</exception>
    public MediaCreationProgress Report(MediaCreationPhase phase, int phasePercent)
    {
        int index = Array.FindIndex(phases, entry => entry.Phase == phase);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, $"The phase is not part of the {kind} plan.");
        }

        int precedingWeight = phases.Take(index).Sum(entry => entry.Weight);
        int clampedPercent = Math.Clamp(phasePercent, 0, 100);
        int percent = precedingWeight + (int)Math.Round(phases[index].Weight * clampedPercent / 100.0, MidpointRounding.AwayFromZero);

        if (index + 1 < Current.StepNumber || percent < Current.Percent)
        {
            return Current;
        }

        Current = new MediaCreationProgress(phase, index + 1, phases.Length, Math.Min(percent, 100));
        return Current;
    }

    /// <summary>
    /// Reports the percentage of the final output stage, mapping it to the phase or phases that represent it.
    /// </summary>
    /// <param name="finalMediaPercent">Percentage reported by the ISO, USB or PXE output service.</param>
    /// <returns>The resulting overall progress.</returns>
    public MediaCreationProgress ReportFinalMedia(int finalMediaPercent)
    {
        int percent = Math.Clamp(finalMediaPercent, 0, 100);
        return kind switch
        {
            MediaCreationKind.Iso => Report(MediaCreationPhase.CreatingIso, percent),
            MediaCreationKind.UsbUpdate => Report(MediaCreationPhase.UpdatingUsbDrive, percent),
            MediaCreationKind.Pxe => Report(MediaCreationPhase.CreatingPxeFiles, percent),
            _ when percent < UsbFormattingCompletePercent =>
                Report(MediaCreationPhase.FormattingUsbDrive, percent * 100 / UsbFormattingCompletePercent),
            _ => Report(
                MediaCreationPhase.CopyingToUsbDrive,
                (percent - UsbFormattingCompletePercent) * 100 / (100 - UsbFormattingCompletePercent))
        };
    }
}
