// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Provides the low-cardinality identifier used to correlate diagnostics across a Foundry runtime session.
/// </summary>
public static class DiagnosticSessionContext
{
    public const string EnvironmentVariableName = "FOUNDRY_DIAGNOSTIC_SESSION_ID";

    private const int MaximumSessionIdLength = 32;

    /// <summary>
    /// Gets the session identifier inherited from the environment or creates a new process session identifier.
    /// </summary>
    public static string CurrentSessionId { get; } = ResolveSessionId(
        Environment.GetEnvironmentVariable(EnvironmentVariableName));

    /// <summary>
    /// Normalizes an inherited session identifier or creates a short identifier when none is available.
    /// </summary>
    public static string ResolveSessionId(string? inheritedSessionId)
    {
        if (string.IsNullOrWhiteSpace(inheritedSessionId))
        {
            return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        }

        var builder = new StringBuilder(Math.Min(inheritedSessionId.Length, MaximumSessionIdLength));
        bool separatorPending = false;
        foreach (char character in inheritedSessionId.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                if (separatorPending && builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                builder.Append(character);
                separatorPending = false;
            }
            else
            {
                separatorPending = true;
            }

            if (builder.Length >= MaximumSessionIdLength)
            {
                break;
            }
        }

        string normalized = builder.ToString().TrimEnd('-');
        return string.IsNullOrWhiteSpace(normalized)
            ? Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()
            : normalized;
    }
}
