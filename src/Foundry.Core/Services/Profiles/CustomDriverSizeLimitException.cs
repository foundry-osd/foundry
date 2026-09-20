// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Profiles;

/// <summary>Identifies custom driver sources that exceed the aggregate size supported by a media snapshot.</summary>
public sealed class CustomDriverSizeLimitException(long actualBytes, long limitBytes)
    : IOException($"Custom driver sources exceed the supported size limit: at least {actualBytes} bytes were found; the limit is {limitBytes} bytes.")
{
    /// <summary>Gets the aggregate source bytes observed when the size limit was exceeded.</summary>
    public long ActualBytes { get; } = actualBytes;

    /// <summary>Gets the maximum aggregate source size accepted by the snapshot.</summary>
    public long LimitBytes { get; } = limitBytes;
}
