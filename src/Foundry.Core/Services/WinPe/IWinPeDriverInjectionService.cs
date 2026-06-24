// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Injects prepared driver directories into a mounted boot image.
/// </summary>
public interface IWinPeDriverInjectionService
{
    /// <summary>
    /// Injects drivers into a mounted WinPE or WinRE image.
    /// </summary>
    /// <param name="options">The injection options.</param>
    /// <param name="cancellationToken">A token used to cancel DISM execution.</param>
    /// <returns>A success result or diagnostic failure.</returns>
    Task<WinPeResult> InjectAsync(
        WinPeDriverInjectionOptions options,
        CancellationToken cancellationToken = default);
}
