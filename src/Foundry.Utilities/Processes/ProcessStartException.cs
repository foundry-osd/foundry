// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Processes;

/// <summary>
/// Represents a failure to start an executable.
/// </summary>
public sealed class ProcessStartException : Exception
{
    internal ProcessStartException(
        string fileName,
        string message,
        int? nativeErrorCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FileName = fileName;
        NativeErrorCode = nativeErrorCode;
    }

    /// <summary>
    /// Gets the executable that could not be started.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the native Windows error code when one was supplied by the operating system.
    /// </summary>
    public int? NativeErrorCode { get; }
}
