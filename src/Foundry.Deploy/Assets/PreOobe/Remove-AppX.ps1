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

function Invoke-DismAppxProvisionedPackageRemoval {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageName
    )

    $dismPath = Join-Path $env:SystemRoot 'System32\dism.exe'
    if (-not (Test-Path -LiteralPath $dismPath)) {
        $dismPath = 'dism.exe'
    }

    $dismArguments = @(
        '/Online',
        '/Remove-ProvisionedAppxPackage',
        "/PackageName:$PackageName",
        '/NoRestart'
    )

    Write-FoundryLog "Running DISM provisioned AppX removal for package identity: $PackageName"
    $dismOutput = & $dismPath @dismArguments 2>&1
    $exitCode = $LASTEXITCODE

    foreach ($line in @($dismOutput)) {
        $message = [string]$line
        if (-not [string]::IsNullOrWhiteSpace($message)) {
            Write-FoundryLog "DISM: $message"
        }
    }

    return $exitCode
}

function Remove-FoundryProvisionedAppxPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CatalogPackageName
    )

    $provisionedPackages = @(Get-AppxProvisionedPackage -Online | Where-Object {
        $_.DisplayName -eq $CatalogPackageName
    })

    if ($provisionedPackages.Count -eq 0) {
        Write-FoundryLog "Skipping missing provisioned AppX package: $CatalogPackageName"
        return
    }

    foreach ($provisionedPackage in $provisionedPackages) {
        $operationStartedAt = [DateTimeOffset]::Now
        try {
            $resolvedPackageName = [string]$provisionedPackage.PackageName
            if ([string]::IsNullOrWhiteSpace($resolvedPackageName)) {
                Write-FoundryLog "WARNING: Skipping provisioned AppX package '$($provisionedPackage.DisplayName)' because its package identity is empty."
                continue
            }

            Write-FoundryLog "Removing provisioned AppX package: $($provisionedPackage.DisplayName) ($resolvedPackageName)"
            $exitCode = Invoke-DismAppxProvisionedPackageRemoval -PackageName $resolvedPackageName
            if ($exitCode -ne 0) {
                Write-FoundryLog "WARNING: DISM was unable to remove provisioned AppX package '$($provisionedPackage.DisplayName)' with exit code $exitCode."
                continue
            }

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

    foreach ($selectedPackageName in $selectedPackageNames) {
        try {
            Remove-FoundryProvisionedAppxPackage -CatalogPackageName ([string]$selectedPackageName)
        }
        catch {
            Write-FoundryLog "WARNING: Unable to process selected provisioned AppX package '$selectedPackageName': $($_.Exception.Message)"
        }
    }

    Write-FoundryLog "Foundry AppX removal completed."
}
finally {
    Stop-FoundryTranscript
}
