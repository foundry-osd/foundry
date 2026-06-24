// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Describes a deferred driver package command staged into the pre-OOBE runner.
/// </summary>
public sealed record PreOobeDriverPackScriptSettings
{
    /// <summary>
    /// Gets the supported deferred driver command kind.
    /// </summary>
    public required DeferredDriverPackageCommandKind CommandKind { get; init; }

    /// <summary>
    /// Gets the runtime package path used from the first full Windows boot.
    /// </summary>
    public required string RuntimePackagePath { get; init; }
}
