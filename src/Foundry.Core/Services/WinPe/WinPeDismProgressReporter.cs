// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Core.Services.WinPe;

internal sealed class WinPeDismProgressReporter
{
    private readonly string _status;
    private readonly IProgress<WinPeDismProgress> _progress;
    private readonly object _sync = new();
    private double _lastReportedPercent = double.NaN;

    public WinPeDismProgressReporter(string status, IProgress<WinPeDismProgress> progress)
    {
        _status = status;
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public bool HasReportedProgress
    {
        get
        {
            lock (_sync)
            {
                return !double.IsNaN(_lastReportedPercent);
            }
        }
    }

    public void HandleOutput(string line)
    {
        if (!PercentageProgressParser.TryParse(line, out double percent) &&
            !OrdinalProgressParser.TryParse(line, out percent))
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

        _progress.Report(new WinPeDismProgress
        {
            Percent = (int)Math.Round(percent, MidpointRounding.AwayFromZero),
            Status = _status
        });
    }

}
