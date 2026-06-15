using System.IO;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Runtime;

public sealed class RecoveryTargetDiskResolver : IRecoveryTargetDiskResolver
{
    private const string RecoveryPartitionGuid = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";
    private const string RecoveryMarkerRelativePath = @"Recovery\WindowsRE\FoundryOsRecovery.json";
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<RecoveryTargetDiskResolver> _logger;

    public RecoveryTargetDiskResolver(IProcessRunner processRunner, ILogger<RecoveryTargetDiskResolver> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<int?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        string script = $$"""
$ErrorActionPreference = 'Stop'
$recoveryGuid = '{{RecoveryPartitionGuid}}'
$markerRelativePath = '{{RecoveryMarkerRelativePath}}'
$assignedAccessPaths = @()
$recoveryPartitions = Get-Partition |
    Where-Object { $_.GptType -and ([string]$_.GptType).Trim('{}').ToLowerInvariant() -eq $recoveryGuid } |
    Sort-Object -Property DiskNumber, PartitionNumber

try {
    $candidates = @()
    foreach ($partition in $recoveryPartitions) {
        $accessPath = @($partition.AccessPaths | Where-Object { $_ -match '^[A-Z]:\\$' } | Select-Object -First 1)[0]
        if ([string]::IsNullOrWhiteSpace($accessPath)) {
            Add-PartitionAccessPath -DiskNumber $partition.DiskNumber -PartitionNumber $partition.PartitionNumber -AssignDriveLetter | Out-Null
            $partition = Get-Partition -DiskNumber $partition.DiskNumber -PartitionNumber $partition.PartitionNumber
            $accessPath = @($partition.AccessPaths | Where-Object { $_ -match '^[A-Z]:\\$' } | Select-Object -First 1)[0]
            if (-not [string]::IsNullOrWhiteSpace($accessPath)) {
                $assignedAccessPaths += [pscustomobject]@{
                    DiskNumber = [int]$partition.DiskNumber
                    PartitionNumber = [int]$partition.PartitionNumber
                    AccessPath = $accessPath
                }
            }
        }

        if ([string]::IsNullOrWhiteSpace($accessPath)) {
            continue
        }

        $markerPath = Join-Path -Path $accessPath -ChildPath $markerRelativePath
        if (Test-Path -LiteralPath $markerPath) {
            $candidates += [pscustomobject]@{
                DiskNumber = [int]$partition.DiskNumber
                PartitionNumber = [int]$partition.PartitionNumber
                MarkerPath = $markerPath
            }
        }
    }

    if ($candidates.Count -eq 1) {
        [pscustomobject]@{
            CandidateCount = [int]$candidates.Count
            DiskNumber = [int]$candidates[0].DiskNumber
        } | ConvertTo-Json -Compress
        return
    }

    [pscustomobject]@{
        CandidateCount = [int]$candidates.Count
    } | ConvertTo-Json -Compress
}
finally {
    foreach ($assigned in $assignedAccessPaths) {
        Remove-PartitionAccessPath -DiskNumber $assigned.DiskNumber -PartitionNumber $assigned.PartitionNumber -AccessPath $assigned.AccessPath -ErrorAction SilentlyContinue
    }
}
""";

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                Path.GetTempPath(),
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            _logger.LogWarning("Unable to resolve active OS Recovery target disk. ExitCode={ExitCode}", execution.ExitCode);
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(execution.StandardOutput);
            int candidateCount = 0;
            if (document.RootElement.TryGetProperty("CandidateCount", out JsonElement candidateCountElement))
            {
                candidateCountElement.TryGetInt32(out candidateCount);
            }

            if (candidateCount != 1)
            {
                _logger.LogWarning("OS Recovery target disk resolver found {CandidateCount} Foundry recovery partition marker(s).", candidateCount);
                return null;
            }

            if (document.RootElement.TryGetProperty("DiskNumber", out JsonElement diskNumberElement) &&
                diskNumberElement.TryGetInt32(out int diskNumber))
            {
                return diskNumber;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OS Recovery target disk resolver output.");
        }

        return null;
    }
}
