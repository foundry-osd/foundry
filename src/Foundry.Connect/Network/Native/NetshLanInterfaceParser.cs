// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network.Native;

/// <summary>
/// Reads the wired 802.1X authentication state of each interface from <c>netsh lan show interfaces</c> output.
/// </summary>
/// <remarks>
/// The parser is deliberately narrow: it reads only the <c>Name</c> and <c>State</c> lines and recognizes English
/// state text. Localized or unfamiliar text yields an unknown (<see langword="null"/>) status, which callers show as
/// <see cref="Dot1xAuthenticationStatus.ProfileApplied"/>.
/// </remarks>
public static class NetshLanInterfaceParser
{
    /// <summary>
    /// Parses the command output into one entry per <c>Name</c> line. Unexpected output yields an empty list.
    /// </summary>
    public static IReadOnlyList<WiredInterfaceAuthentication> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        List<WiredInterfaceAuthentication> interfaces = [];
        string? currentName = null;
        Dot1xAuthenticationStatus? currentStatus = null;
        bool currentStateSeen = false;

        foreach (string line in output.Split('\n'))
        {
            int separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();

            if (key.Equals("Name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                Flush();
                currentName = value;
                currentStatus = null;
                currentStateSeen = false;
            }
            else if (key.Equals("State", StringComparison.OrdinalIgnoreCase) && currentName is not null && !currentStateSeen)
            {
                currentStatus = ParseState(value);
                currentStateSeen = true;
            }
        }

        Flush();
        return interfaces;

        void Flush()
        {
            if (currentName is not null)
            {
                interfaces.Add(new WiredInterfaceAuthentication(currentName, currentStatus));
            }
        }
    }

    /// <summary>
    /// Maps one <c>State</c> value to an authentication status, or <see langword="null"/> when it is not a known English state.
    /// </summary>
    public static Dot1xAuthenticationStatus? ParseState(string stateText)
    {
        ArgumentNullException.ThrowIfNull(stateText);

        string text = stateText.ToLowerInvariant();
        if (text.Contains("authentication succeeded", StringComparison.Ordinal))
        {
            return Dot1xAuthenticationStatus.Authenticated;
        }

        if (text.Contains("authentication failed", StringComparison.Ordinal))
        {
            return Dot1xAuthenticationStatus.Failed;
        }

        return text.Contains("authenticating", StringComparison.Ordinal)
            ? Dot1xAuthenticationStatus.Authenticating
            : null;
    }

    /// <summary>
    /// Combines per-interface states into one: Authenticated, then Authenticating, then Failed, otherwise unknown.
    /// </summary>
    public static Dot1xAuthenticationStatus? Aggregate(IEnumerable<WiredInterfaceAuthentication> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);

        Dot1xAuthenticationStatus?[] statuses = [.. interfaces.Select(static item => item.Status)];
        Dot1xAuthenticationStatus[] preference =
        [
            Dot1xAuthenticationStatus.Authenticated,
            Dot1xAuthenticationStatus.Authenticating,
            Dot1xAuthenticationStatus.Failed
        ];

        foreach (Dot1xAuthenticationStatus candidate in preference)
        {
            if (statuses.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
