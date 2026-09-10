// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Diagnostics;

/// <summary>Persists local session logs without changing the boot result.</summary>
internal interface IBootstrapLogPersistence
{
    Task PersistAsync(CancellationToken cancellationToken);
}
