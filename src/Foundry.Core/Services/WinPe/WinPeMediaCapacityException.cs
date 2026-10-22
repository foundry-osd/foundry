// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Reports that a media data volume cannot hold the content being published.
/// It derives from <see cref="IOException"/> so existing I/O failure handling still applies, while USB updates can
/// tell a capacity shortfall apart from other I/O failures and suggest recreating the media.
/// </summary>
public sealed class WinPeMediaCapacityException : IOException
{
    /// <summary>Creates the exception with a diagnostic message.</summary>
    /// <param name="message">Diagnostic message kept in logs and failure details.</param>
    public WinPeMediaCapacityException(string message)
        : base(message)
    {
    }
}
