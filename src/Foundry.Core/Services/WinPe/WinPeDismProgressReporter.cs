using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundry.Core.Services.WinPe;

internal sealed class WinPeDismProgressReporter
{
    private static readonly Regex PercentageRegex = new(@"(?<percent>\d{1,3}(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex OrdinalProgressRegex = new(@"(?<!\d)(?<current>\d+)\s+(?:of|sur)\s+(?<total>\d+)(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        if (!TryParseProgress(line, out double percent))
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

    private static bool TryParseProgress(string? line, out double percent)
    {
        if (TryParsePercent(line, out percent))
        {
            return true;
        }

        return TryParseOrdinalProgress(line, out percent);
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

    private static bool TryParseOrdinalProgress(string? line, out double percent)
    {
        percent = 0d;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        Match match = OrdinalProgressRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["current"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int current) ||
            !int.TryParse(match.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total) ||
            current <= 0 ||
            total <= 0)
        {
            return false;
        }

        percent = Math.Clamp((double)current / total * 100d, 0d, 100d);
        return true;
    }
}
