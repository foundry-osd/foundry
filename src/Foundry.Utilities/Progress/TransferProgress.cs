// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Progress;

/// <summary>
/// Calculates progress for byte transfers with an optional known total.
/// </summary>
public static class TransferProgress
{
    /// <summary>
    /// Calculates a clamped percentage, or returns <see langword="null" /> when the total is unknown.
    /// </summary>
    public static double? CalculatePercentage(long bytesTransferred, long? totalBytes)
    {
        if (totalBytes is not > 0)
        {
            return null;
        }

        if (bytesTransferred <= 0)
        {
            return 0d;
        }

        if (bytesTransferred >= totalBytes.Value)
        {
            return 100d;
        }

        return (double)bytesTransferred / totalBytes.Value * 100d;
    }
}
