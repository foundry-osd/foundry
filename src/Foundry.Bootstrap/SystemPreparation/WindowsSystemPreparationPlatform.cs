// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Foundry.Bootstrap.SystemPreparation;

/// <summary>Applies preparation through Windows APIs and bounded native tools.</summary>
internal sealed class WindowsSystemPreparationPlatform : ISystemPreparationPlatform
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(5);
    private readonly IWindowsServiceManager _serviceManager;
    private readonly IShortToolRunner _toolRunner;

    public WindowsSystemPreparationPlatform(string winPeRoot)
        : this(winPeRoot, new WindowsServiceManager(), new ShortToolRunner())
    {
    }

    internal WindowsSystemPreparationPlatform(
        string winPeRoot,
        IWindowsServiceManager serviceManager,
        IShortToolRunner toolRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(winPeRoot);
        ArgumentNullException.ThrowIfNull(serviceManager);
        ArgumentNullException.ThrowIfNull(toolRunner);
        _serviceManager = serviceManager;
        _toolRunner = toolRunner;
    }

    public string? EnvironmentTimeZoneId => Environment.GetEnvironmentVariable("FOUNDRY_WINPE_TIMEZONE_ID");

    public bool IsWinPeSystemDrive => string.Equals(
        Environment.GetEnvironmentVariable("SystemDrive"),
        "X:",
        StringComparison.OrdinalIgnoreCase);

    public bool WirelessDependenciesPresent =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "dmcmnutils.dll")) &&
        File.Exists(Path.Combine(Environment.SystemDirectory, "mdmregistration.dll"));

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public async Task EnsureServiceRunningAsync(string serviceName, CancellationToken cancellationToken)
    {
        await _serviceManager.EnsureRunningAsync(serviceName, cancellationToken).ConfigureAwait(false);
    }

    public Task SetSystemTimeAsync(DateTimeOffset utcTime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTime value = utcTime.UtcDateTime;
        var systemTime = new SystemTime
        {
            Year = checked((ushort)value.Year),
            Month = checked((ushort)value.Month),
            Day = checked((ushort)value.Day),
            Hour = checked((ushort)value.Hour),
            Minute = checked((ushort)value.Minute),
            Second = checked((ushort)value.Second),
            Milliseconds = checked((ushort)value.Millisecond)
        };

        if (!SetSystemTime(ref systemTime))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return Task.CompletedTask;
    }

    public bool IsValidWindowsTimeZone(string timeZoneId)
    {
        return TimeZoneInfo.GetSystemTimeZones().Any(
            timeZone => string.Equals(timeZone.Id, timeZoneId, StringComparison.OrdinalIgnoreCase));
    }

    public string? GetCurrentTimeZoneId() => TimeZoneInfo.Local.Id;

    public async Task SetTimeZoneAsync(string timeZoneId, CancellationToken cancellationToken)
    {
        ShortToolResult result = await _toolRunner.RunAsync(
            "tzutil.exe",
            ["/s", timeZoneId],
            ToolTimeout,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Timezone utility returned exit code {result.ExitCode}.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemTime(ref SystemTime systemTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }
}
