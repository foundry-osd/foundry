// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.System;

/// <summary>
/// Represents a process that could not be started.
/// </summary>
public sealed class ProcessStartException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessStartException"/> class.
    /// </summary>
    /// <param name="message">Detailed local diagnostic message.</param>
    public ProcessStartException(string message)
        : base(message)
    {
    }
}
