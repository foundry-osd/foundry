// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.SystemPreparation;

/// <summary>
/// Isolates host mutations required by WinPE system preparation.
/// </summary>
internal interface ISystemPreparationPlatform
{
    string? EnvironmentTimeZoneId { get; }

    bool IsWinPeSystemDrive { get; }

    bool WirelessDependenciesPresent { get; }

    DateTimeOffset UtcNow { get; }

    Task EnsureServiceRunningAsync(string serviceName, CancellationToken cancellationToken);

    Task SetSystemTimeAsync(DateTimeOffset utcTime, CancellationToken cancellationToken);

    bool IsValidWindowsTimeZone(string timeZoneId);

    string? GetCurrentTimeZoneId();

    Task SetTimeZoneAsync(string timeZoneId, CancellationToken cancellationToken);
}
