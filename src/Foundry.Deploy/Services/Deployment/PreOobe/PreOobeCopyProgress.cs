// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Reports the copy of the post-installation runtime and packages to the target as one byte progress across all files, with the
/// file in progress, at most every 100 milliseconds like every other transfer.
/// </summary>
internal sealed class PreOobeCopyProgress(Action<double, string> report, long totalBytes)
{
    private const long ReportIntervalMilliseconds = 100;
    private long completedBytes;
    private long nextReport;

    /// <summary>Returns the callback that receives the bytes copied so far of <paramref name="fileName"/>.</summary>
    public Action<long> For(string fileName) => copied => Report(fileName, completedBytes + copied, force: false);

    /// <summary>Counts a finished file and reports it.</summary>
    public void Completed(string fileName, long length)
    {
        completedBytes = checked(completedBytes + length);
        Report(fileName, completedBytes, force: true);
    }

    private void Report(string fileName, long copied, bool force)
    {
        long now = Environment.TickCount64;
        if (!force && now < nextReport) return;
        nextReport = now + ReportIntervalMilliseconds;
        report(Percent(copied, totalBytes), FormatDetail(fileName, copied, totalBytes));
    }

    private static double Percent(long copied, long total) => total > 0 ? Math.Min(100d, copied * 100d / total) : 100d;

    /// <summary>Formats the detail line, for example <c>setup.exe: 42% (21.0 MB / 50.0 MB)</c>.</summary>
    internal static string FormatDetail(string fileName, long copied, long total) =>
        $"{fileName}: {Percent(copied, total):0}% ({DeploymentStepExecutionContext.FormatByteSize(copied)} / {DeploymentStepExecutionContext.FormatByteSize(total)})";
}
