using System.IO;
using System.Text;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Writes the troubleshooting CSV in the Intune Autopilot hardware hash import shape.
/// </summary>
public static class AutopilotHardwareHashCsvWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Writes a single-device Autopilot hardware hash CSV without quoting or additional columns.
    /// </summary>
    /// <param name="path">Destination CSV path.</param>
    /// <param name="identity">Captured device identity.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    public static async Task WriteAsync(
        string path,
        AutopilotHardwareHashDeviceIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string csv = string.Join(
            "\r\n",
            "Device Serial Number,Windows Product ID,Hardware Hash,Group Tag",
            $"{SanitizeCsvField(identity.SerialNumber)},,{SanitizeCsvField(identity.HardwareHash)},{SanitizeCsvField(identity.GroupTag)}",
            string.Empty);

        await File.WriteAllTextAsync(path, csv, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static string SanitizeCsvField(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
    }
}
