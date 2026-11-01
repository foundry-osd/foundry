// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.AutopilotInteractive.Services.QrCode;

/// <summary>
/// Builds the address encoded in the sign-in QR code.
/// </summary>
public static class VerificationUrlBuilder
{
    /// <summary>
    /// Appends the user code as the <c>otc</c> query parameter so scanning the QR code lands on the verification page with
    /// the code already filled in.
    /// </summary>
    /// <param name="verificationUri">Verification page returned with the device code.</param>
    /// <param name="userCode">User code of the device code.</param>
    /// <returns>The verification page address carrying the escaped user code.</returns>
    public static Uri Build(Uri verificationUri, string userCode)
    {
        string separator = string.IsNullOrEmpty(verificationUri.Query) ? "?" : "&";
        return new Uri(verificationUri.AbsoluteUri + separator + "otc=" + Uri.EscapeDataString(userCode));
    }
}
