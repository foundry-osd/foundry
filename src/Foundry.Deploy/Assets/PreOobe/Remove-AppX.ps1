param(
    [Parameter(Mandatory = $true)]
    [string[]]$PackageNames
)

$ErrorActionPreference = 'Stop'
$LogDirectory = Join-Path $env:SystemRoot 'Temp\Foundry\Logs\PreOobe'
$TranscriptPath = Join-Path $LogDirectory 'Remove-AppX.transcript.log'
$TranscriptStarted = $false
$ScriptStartedAt = [DateTimeOffset]::Now

function Start-FoundryTranscript {
    New-Item -Path $LogDirectory -ItemType Directory -Force | Out-Null
    Start-Transcript -Path $TranscriptPath -Force | Out-Null
    $script:TranscriptStarted = $true
}

function Stop-FoundryTranscript {
    if ($script:TranscriptStarted) {
        Stop-Transcript | Out-Null
    }
}

function Write-FoundryLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $now = [DateTimeOffset]::Now
    $elapsed = $now - $script:ScriptStartedAt
    Write-Host ("[{0}] [+{1:c}] {2}" -f $now.ToString('yyyy-MM-ddTHH:mm:ss'), $elapsed, $Message)
}

function Remove-ProvisionedAppxPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageName
    )

    $provisionedPackages = @(Get-AppxProvisionedPackage -Online | Where-Object {
        $_.DisplayName -eq $PackageName
    })

    if ($provisionedPackages.Count -eq 0) {
        Write-FoundryLog "Skipping missing provisioned AppX package: $PackageName"
        return
    }

    foreach ($provisionedPackage in $provisionedPackages) {
        $operationStartedAt = [DateTimeOffset]::Now
        try {
            Write-FoundryLog "Removing provisioned AppX package: $($provisionedPackage.DisplayName) ($($provisionedPackage.PackageName))"
            Remove-AppxProvisionedPackage -Online -PackageName $provisionedPackage.PackageName -ErrorAction Stop | Out-Null
            $operationDuration = [DateTimeOffset]::Now - $operationStartedAt
            Write-FoundryLog "Removed provisioned AppX package '$($provisionedPackage.DisplayName)' after $($operationDuration.ToString('c'))."
        }
        catch {
            Write-FoundryLog "WARNING: Unable to remove provisioned AppX package '$($provisionedPackage.DisplayName)': $($_.Exception.Message)"
        }
    }
}

try {
    Start-FoundryTranscript
    Write-FoundryLog "Foundry AppX removal started."

    $selectedPackageNames = @($PackageNames |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries) } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
    if ($selectedPackageNames.Count -eq 0) {
        Write-FoundryLog "No provisioned AppX packages were selected for removal."
        return
    }

    foreach ($PackageName in $selectedPackageNames) {
        Remove-ProvisionedAppxPackage -PackageName $PackageName
    }

    Write-FoundryLog "Foundry AppX removal completed."
}
finally {
    Stop-FoundryTranscript
}
