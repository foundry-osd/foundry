using System.Globalization;

namespace Foundry.Telemetry;

/// <summary>
/// Applies the local-day throttle used by the persistent Foundry OSD activity event.
/// </summary>
public static class TelemetryDailyActivityGate
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Determines whether an activity event should be sent for the supplied local date.
    /// </summary>
    /// <param name="today">Current local calendar date.</param>
    /// <param name="lastTrackedDate">Persisted local date in ISO format.</param>
    /// <returns><see langword="true"/> when no event has been recorded for <paramref name="today"/>.</returns>
    public static bool ShouldTrack(DateOnly today, string? lastTrackedDate)
    {
        return !DateOnly.TryParseExact(
            lastTrackedDate,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly parsedDate) ||
            parsedDate != today;
    }

    /// <summary>
    /// Formats a local date for persistence in application settings.
    /// </summary>
    /// <param name="date">Local calendar date.</param>
    /// <returns>The ISO local date value.</returns>
    public static string FormatDate(DateOnly date)
    {
        return date.ToString(DateFormat, CultureInfo.InvariantCulture);
    }
}
