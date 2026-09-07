// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Records required component/language keys and the actual identities verified as installed by DISM.</summary>
public sealed record WinPeCapabilityValidationResult(
    IReadOnlyList<string> RequiredPackages,
    IReadOnlyList<string> InstalledPackageIdentities);
