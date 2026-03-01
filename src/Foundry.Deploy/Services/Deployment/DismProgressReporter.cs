using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundry.Deploy.Services.Deployment;

internal sealed class DismProgressReporter
{
    private static readonly Regex PercentageRegex = new(@"(?<percent>\d{1,3}(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);

    private readonly IProgress<double> _progress;
    private readonly object _sync = new();
    private double _lastReportedPercent = double.NaN;

    public DismProgressReporter(IProgress<double> progress)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public void HandleOutput(string line)
    {
        if (!TryParsePercent(line, out double percent))
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

    private static bool TryParsePercent(string? line, out double percent)
    {
        percent = 0d;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        Match match = PercentageRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        string rawPercent = match.Groups["percent"].Value.Replace(',', '.');
        if (!double.TryParse(rawPercent, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return false;
        }

        percent = Math.Clamp(parsed, 0d, 100d);
        return true;
    }
}
