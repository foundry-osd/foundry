// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Sharing;

/// <summary>What a running publish is doing, as a progress label names it.</summary>
public enum PublishStage
{
    /// <summary>The folder is inspected and the password keys are derived.</summary>
    CheckingFolder,

    /// <summary>The hash of an image is verified.</summary>
    Verifying,

    /// <summary>A package or driver set is verified and its archive written.</summary>
    Preparing,

    /// <summary>A content item is encrypted into the folder.</summary>
    Sending,

    /// <summary>Every content item is in the folder; the signed manifest and <c>current.json</c> are written.</summary>
    Finishing
}

/// <summary>Turns the <see cref="ContentProgress"/> of a publish into the stage and percentage a progress bar shows.</summary>
/// <remarks>
/// <see cref="PublicationWriter"/> reports the folder check, the preparation and the encryption of each item, then writes the signed
/// manifest and <c>current.json</c> without further reports. The folder check reads as 0 %, the content steps fill the first
/// <see cref="ContentShare"/> percent in bytes, and the rest stands for the manifest step, so the bar never reads 100 % before the
/// publish has returned.
/// </remarks>
public static class PublishProgress
{
    /// <summary>Percentage reached when every content item is in the folder and the manifest step starts.</summary>
    public const double ContentShare = 95;

    /// <summary>Returns the publish percentage, from 0 to <see cref="ContentShare"/>; 0 before the first content report.</summary>
    /// <param name="progress">The last report of the running publish, or <see langword="null"/> when none arrived yet.</param>
    public static double Percent(ContentProgress? progress) => progress switch
    {
        null or { Step: ContentProgressStep.CheckingFolder } => 0,
        // The writer sends a final report with Done == Total, including for a publication without content (0 of 0).
        { Total: <= 0 } => ContentShare,
        _ => ContentShare * Math.Clamp((double)progress.Done / progress.Total, 0, 1)
    };

    /// <summary>Returns the stage of the running publish; <see cref="PublishStage.CheckingFolder"/> before the first report.</summary>
    /// <param name="progress">The last report of the running publish, or <see langword="null"/> when none arrived yet.</param>
    public static PublishStage Stage(ContentProgress? progress) => progress switch
    {
        null or { Step: ContentProgressStep.CheckingFolder } => PublishStage.CheckingFolder,
        { Step: ContentProgressStep.Verifying } => PublishStage.Verifying,
        { Step: ContentProgressStep.Preparing } => PublishStage.Preparing,
        { CurrentName.Length: 0 } => PublishStage.Finishing,
        _ => PublishStage.Sending
    };
}
