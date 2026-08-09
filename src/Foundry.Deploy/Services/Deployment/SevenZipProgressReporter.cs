// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Deploy.Services.System;

internal sealed class SevenZipProgressReporter
{
    private readonly IProgress<double> _progress;
    private readonly object _sync = new();
    private double _lastReportedPercent = double.NaN;

    public SevenZipProgressReporter(IProgress<double> progress)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public void HandleOutput(string line)
    {
        if (!PercentageProgressParser.TryParse(line, out double percent))
        {
            return;
        }

        lock (_sync)
        {
            if (!double.IsNaN(_lastReportedPercent) && percent <= _lastReportedPercent)
            {
                return;
            }

            _lastReportedPercent = percent;
        }

        _progress.Report(percent);
    }

}
