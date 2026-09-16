// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Utilities.Processes;
using Foundry.Utilities.Storage;

namespace Foundry.Utilities.Tests.Storage;

public sealed class WindowsDiskIdentityGuardTests
{
    [Theory]
    [MemberData(nameof(DiskIdentityTests.ResolutionCases), MemberType = typeof(DiskIdentityTests))]
    public async Task CreateScript_ResolvesOnlyTheConfirmedIdentity(
        string scenario,
        DiskIdentity expected,
        DiskIdentity[] snapshots,
        bool shouldResolve)
    {
        string encodedSnapshots = EncodeJson(snapshots.Select(static disk => new
        {
            disk.Number,
            disk.UniqueId,
            disk.SerialNumber,
            disk.FriendlyName,
            disk.BusType,
            Size = disk.SizeBytes,
            PartitionStyle = "RAW"
        }));
        string query = $$"""
            foreach ($snapshot in ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedSnapshots}}')) | ConvertFrom-Json)) {
                $snapshot
            }
            """;

        ProcessExecutionResult result = await ExecuteGuardAsync(expected, query);

        Assert.True(result.IsSuccess, $"{scenario}: {result.StandardError}");
        Assert.Equal(shouldResolve ? "Confirmed:3" : WindowsDiskIdentityGuard.FailureMarker, result.StandardOutput.Trim());
        Assert.Equal(shouldResolve, expected.Resolve(snapshots) is not null);
        Assert.DoesNotContain("injected", result.StandardError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("throw 'private-device-identifier'")]
    [InlineData("[pscustomobject]@{ Number = $null; UniqueId = 'device-3'; SerialNumber = 'serial-3'; FriendlyName = 'Storage device'; BusType = 'USB'; Size = 64000000000 }")]
    [InlineData("[pscustomobject]@{ Number = 3.5; UniqueId = 'device-3'; SerialNumber = 'serial-3'; FriendlyName = 'Storage device'; BusType = 'USB'; Size = 64000000000 }")]
    [InlineData("[pscustomobject]@{ Number = 3; UniqueId = 'device-3'; SerialNumber = 'serial-3'; FriendlyName = 'Storage device'; BusType = 'USB'; Size = 'broken' }")]
    public async Task CreateScript_WhenQueryFailsOrSnapshotIsInvalid_ThrowsPrivateFailureMarker(string query)
    {
        var expected = new DiskIdentity(3, "device-3", "serial-3", "Storage device", "USB", 64_000_000_000);

        ProcessExecutionResult result = await ExecuteGuardAsync(expected, query);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(WindowsDiskIdentityGuard.FailureMarker, result.StandardOutput.Trim());
        Assert.Empty(result.StandardError);
    }

    private static async Task<ProcessExecutionResult> ExecuteGuardAsync(DiskIdentity expected, string query)
    {
        string script = $$"""
            $PSModuleAutoLoadingPreference = 'None'
            $ErrorActionPreference = 'Stop'
            Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
            function Get-Disk {
                [CmdletBinding()]
                param()
                {{query}}
            }
            try {
                {{WindowsDiskIdentityGuard.CreateScript(expected)}}
                if ($null -eq $foundryConfirmedDisk) { throw 'No confirmed disk was returned.' }
                [Console]::WriteLine('Confirmed:' + $foundryConfirmedDisk.Number)
            } catch {
                [Console]::WriteLine($_.Exception.Message)
            }
            """;

        return await new ProcessRunner().RunAsync(
            new ProcessExecutionRequest("powershell.exe",
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", .. PowerShellCommand.CreateEncodedArguments(script)],
                Path.GetTempPath()),
            TestContext.Current.CancellationToken);
    }

    private static string EncodeJson<T>(T value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    }
}
