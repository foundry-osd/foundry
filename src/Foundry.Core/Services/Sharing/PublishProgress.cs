// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Sharing;

/// <summary>Turns the <see cref="ContentProgress"/> of a publish into the percentage a progress bar shows.</summary>
/// <remarks>
/// <see cref="PublicationWriter"/> reports the content copy, then writes the signed manifest and <c>current.json</c> without further
/// reports. The content copy fills the first <see cref="ContentShare"/> percent; the rest stands for the manifest step, so the bar
/// never reads 100 % before the publish has returned.
/// </remarks>
public static class PublishProgress
{
    /// <summary>Percentage reached when every content item is in the folder and the manifest step starts.</summary>
    public const double ContentShare = 95;

    /// <summary>Returns the publish percentage, from 0 to <see cref="ContentShare"/>; 0 before the first report.</summary>
    /// <param name="progress">The last report of the running publish, or <see langword="null"/> when none arrived yet.</param>
    public static double Percent(ContentProgress? progress) => progress switch
    {
        null => 0,
        // The writer sends a final report with Done == Total, including for a publication without content (0 of 0).
        { Total: <= 0 } => ContentShare,
        _ => ContentShare * Math.Clamp((double)progress.Done / progress.Total, 0, 1)
    };
}
