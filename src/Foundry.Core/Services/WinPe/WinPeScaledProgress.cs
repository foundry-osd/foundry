// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Maps the <c>0..100</c> progress of a nested media step into a sub-range of its parent, so a step that restarts
/// its own percentage cannot move the parent progress backwards.
/// </summary>
internal sealed class WinPeScaledProgress : IProgress<WinPeMediaProgress>
{
    private readonly IProgress<WinPeMediaProgress> parent;
    private readonly int start;
    private readonly int end;

    internal WinPeScaledProgress(IProgress<WinPeMediaProgress> parent, int start, int end)
    {
        this.parent = parent;
        this.start = start;
        this.end = end;
    }

    /// <summary>
    /// Creates a scaled reporter, or returns <see langword="null"/> when there is no parent to report to.
    /// </summary>
    internal static IProgress<WinPeMediaProgress>? Create(IProgress<WinPeMediaProgress>? parent, int start, int end)
    {
        return parent is null ? null : new WinPeScaledProgress(parent, start, end);
    }

    /// <summary>
    /// Splits a progress range between external content copied in order: custom images, post-installation content,
    /// then driver sets. Custom images are usually far larger, so the weights are 70, 15 and 15, normalized over the
    /// content that is present; absent content gets an empty share.
    /// </summary>
    /// <returns>The percentages where the custom image share and the post-installation share end.</returns>
    internal static (int ImagesEnd, int PostInstallationEnd) SplitExternalContent(int start, int end,
        bool hasImages, bool hasPostInstallation, bool hasDrivers)
    {
        int images = hasImages ? 70 : 0;
        int postInstallation = hasPostInstallation ? 15 : 0;
        int total = images + postInstallation + (hasDrivers ? 15 : 0);
        if (total == 0) return (start, start);
        return (start + ((end - start) * images / total), start + ((end - start) * (images + postInstallation) / total));
    }

    public void Report(WinPeMediaProgress value)
    {
        int percent = start + ((end - start) * Math.Clamp(value.Percent, 0, 100) / 100);
        parent.Report(value with { Percent = percent });
    }
}
