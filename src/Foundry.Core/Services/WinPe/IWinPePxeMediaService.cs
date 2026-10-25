// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Writes the network boot files of a prepared WinPE workspace to a folder that a PXE server can serve.
/// </summary>
public interface IWinPePxeMediaService
{
    /// <summary>
    /// Copies <c>boot.wim</c>, <c>boot.sdi</c>, <c>BCD</c> and <c>bootmgr.efi</c> to the output folder, replacing
    /// earlier copies only after all four files are written.
    /// </summary>
    /// <returns>The number of bytes written when the four files are in place.</returns>
    Task<WinPeResult<long>> CreateAsync(WinPePxeMediaOptions options, CancellationToken cancellationToken = default);
}
