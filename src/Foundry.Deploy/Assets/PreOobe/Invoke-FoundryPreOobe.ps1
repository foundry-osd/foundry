param([switch]$RetryFailed, [string]$ActionId, [switch]$CleanupSecretsOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Foundry-PreOobeFunctions.ps1')
try {
    $results = @(Invoke-FoundryPlan -ManifestPath (Join-Path $PSScriptRoot 'pre-oobe-manifest.json') -ResultsPath (Join-Path $PSScriptRoot 'results.json') -RetryFailed:$RetryFailed -ActionId $ActionId -CleanupSecretsOnly:$CleanupSecretsOnly)
}
catch {
    [Console]::Error.WriteLine('first_boot_execution_failed')
    exit 1
}
if ($CleanupSecretsOnly) { exit 0 }
if (@($results | Where-Object status -ne 'succeeded').Count -gt 0) { exit 1 }
if (@($results | Where-Object rebootRequired).Count -gt 0) { exit 3010 }
exit 0
