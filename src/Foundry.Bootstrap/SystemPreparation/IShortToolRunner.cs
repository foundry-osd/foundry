// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.SystemPreparation;

/// <summary>Runs bounded preparation tools without forwarding their output to the boot console.</summary>
internal interface IShortToolRunner
{
    Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
