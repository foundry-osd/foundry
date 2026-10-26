// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Sharing;

/// <summary>What a running fetch is doing, as a progress label names it.</summary>
public enum SyncStage
{
    /// <summary>A content item is read from the folder and decrypted.</summary>
    Downloading,

    /// <summary>A downloaded item is imported into its library.</summary>
    Installing,

    /// <summary>Every missing item was handled.</summary>
    Complete
}

/// <summary>Turns the <see cref="ContentProgress"/> of a fetch (<see cref="ContentSync"/>) into the stage and percentage a progress bar shows.</summary>
/// <remarks>
/// The download and the install of every item count in bytes. While an item is still handled the percentage stays at most
/// <see cref="MaximumWhileRunning"/>, so the bar reads full only after the final report, once every item is imported.
/// </remarks>
public static class SyncProgress
{
    /// <summary>Highest percentage before the final report.</summary>
    public const double MaximumWhileRunning = 99;

    /// <summary>Returns the fetch percentage, from 0 to 100; 0 before the first report.</summary>
    /// <param name="progress">The last report of the running fetch, or <see langword="null"/> when none arrived yet.</param>
    public static double Percent(ContentProgress? progress) => progress switch
    {
        null => 0,
        { CurrentName.Length: 0 } when progress.Done >= progress.Total => 100,
        { Total: <= 0 } => 0,
        _ => Math.Min(MaximumWhileRunning, 100 * Math.Clamp((double)progress.Done / progress.Total, 0, 1))
    };

    /// <summary>Returns the stage of the running fetch; <see cref="SyncStage.Downloading"/> before the first report.</summary>
    /// <param name="progress">The last report of the running fetch, or <see langword="null"/> when none arrived yet.</param>
    public static SyncStage Stage(ContentProgress? progress) => progress switch
    {
        { Step: ContentProgressStep.Installing } => SyncStage.Installing,
        { CurrentName.Length: 0 } when progress.Done >= progress.Total => SyncStage.Complete,
        _ => SyncStage.Downloading
    };
}
