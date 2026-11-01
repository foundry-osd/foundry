// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.AutopilotInteractive.Startup;

/// <summary>
/// Command-line arguments Foundry.PostInstall passes to the application.
/// </summary>
/// <param name="ConfigPath">Path of the runtime configuration file, or <see langword="null"/> when <c>--config</c> is absent.</param>
/// <param name="DeadlineUtc">
/// Instant after which Windows setup must continue without registration, always at offset zero, or <see langword="null"/>
/// when <c>--deadline</c> is absent or malformed.
/// </param>
public sealed record AutopilotInteractiveCommandLine(string? ConfigPath, DateTimeOffset? DeadlineUtc)
{
    private const string ConfigOption = "--config";
    private const string DeadlineOption = "--deadline";

    /// <summary>
    /// Parses <c>--config &lt;path&gt;</c> and <c>--deadline &lt;UTC ISO 8601&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Parsing never throws: unknown arguments, an option without a value and a malformed deadline are reported through
    /// <paramref name="warn"/> and ignored, because the process was started unattended and must not fail on a typo.
    /// When an option repeats, the last valid occurrence wins.
    /// </remarks>
    /// <param name="args">Process arguments without the executable path.</param>
    /// <param name="warn">Receives one message per ignored argument.</param>
    /// <returns>The parsed arguments.</returns>
    public static AutopilotInteractiveCommandLine Parse(IReadOnlyList<string> args, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(warn);

        string? configPath = null;
        DateTimeOffset? deadlineUtc = null;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            bool isConfig = string.Equals(argument, ConfigOption, StringComparison.OrdinalIgnoreCase);
            bool isDeadline = string.Equals(argument, DeadlineOption, StringComparison.OrdinalIgnoreCase);

            if (!isConfig && !isDeadline)
            {
                warn($"Ignoring unknown argument '{argument}'.");
                continue;
            }

            if (index + 1 >= args.Count)
            {
                warn($"Ignoring '{argument}' because it has no value.");
                continue;
            }

            string value = args[++index];
            if (isConfig)
            {
                configPath = value;
            }
            else if (TryParseDeadline(value, out DateTimeOffset parsed))
            {
                deadlineUtc = parsed;
            }
            else
            {
                warn($"Ignoring '{DeadlineOption}' because '{value}' is not a valid ISO 8601 date and time.");
            }
        }

        return new AutopilotInteractiveCommandLine(configPath, deadlineUtc);
    }

    private static bool TryParseDeadline(string value, out DateTimeOffset deadlineUtc)
    {
        // AssumeUniversal covers a value written without an offset; AdjustToUniversal makes every result offset zero.
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out deadlineUtc);
    }
}
