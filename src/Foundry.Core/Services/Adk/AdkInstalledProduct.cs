// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

public sealed record AdkInstalledProduct(
    string DisplayName,
    string? DisplayVersion,
    string? UninstallString = null,
    string? QuietUninstallString = null);
