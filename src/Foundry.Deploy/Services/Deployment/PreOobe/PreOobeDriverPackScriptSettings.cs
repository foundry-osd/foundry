// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Describes the deferred driver command executed by Foundry.PostInstall.
/// </summary>
public sealed record PreOobeDriverPackScriptSettings
{
    /// <summary>
    /// Gets the supported deferred driver command kind.
    /// </summary>
    public required DeferredDriverPackageCommandKind CommandKind { get; init; }
}
