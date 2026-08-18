// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Shell;

internal sealed record NavigationStatus(
    NavigationInfoBadgeSeverity? Severity,
    string StatusResourceKey);

internal interface INavigationStatusService
{
    event EventHandler? StatusChanged;

    NavigationStatus? GetStatus(Type pageType);
}
