param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('LenovoExecutable', 'SurfaceMsi')]
    [string]$CommandKind,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$SuccessExitCodes = @(0, 3010)
$ResolvedPackagePath = [Environment]::ExpandEnvironmentVariables($PackagePath)
$LogDirectory = Join-Path $env:SystemRoot 'Temp\Foundry\Logs\PreOobe'
$TranscriptPath = Join-Path $LogDirectory 'Install-DriverPack.transcript.log'
$TranscriptStarted = $false
$ScriptStartedAt = [DateTimeOffset]::Now
$DriverPathRegistryKey = 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\UnattendSettings\PnPUnattend\DriverPaths\1'

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

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value -replace '"', '\"') + '"'
}

function Invoke-ProcessAndWait {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$ArgumentList = @(),

        [Parameter(Mandatory = $true)]
        [string]$OperationName
    )

    Write-FoundryLog "Starting ${OperationName}: $FilePath $($ArgumentList -join ' ')"
    $operationStartedAt = [DateTimeOffset]::Now
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru
    $operationDuration = [DateTimeOffset]::Now - $operationStartedAt
    Write-FoundryLog "$OperationName exited with code $($process.ExitCode) after $($operationDuration.ToString('c'))."

    if ($SuccessExitCodes -notcontains $process.ExitCode) {
        throw "$OperationName failed with exit code $($process.ExitCode)."
    }
}

$DriverPathRegistered = $false

try {
    Start-FoundryTranscript
    Write-FoundryLog "Foundry driver pack installation started."
    Write-FoundryLog "CommandKind=$CommandKind"
    Write-FoundryLog "PackagePath=$ResolvedPackagePath"

    if (-not (Test-Path -Path $ResolvedPackagePath -PathType Leaf)) {
        throw "Driver package was not found: $ResolvedPackagePath"
    }

    switch ($CommandKind) {
        'LenovoExecutable' {
            Invoke-ProcessAndWait `
                -FilePath $ResolvedPackagePath `
                -ArgumentList @('/SILENT', '/SUPPRESSMSGBOXES') `
                -OperationName 'Lenovo driver package'

            Invoke-ProcessAndWait `
                -FilePath 'reg.exe' `
                -ArgumentList @('add', (ConvertTo-ProcessArgument -Value $DriverPathRegistryKey), '/v', 'Path', '/t', 'REG_SZ', '/d', 'C:\Drivers', '/f') `
                -OperationName 'Register PnPUnattend driver path'
            $DriverPathRegistered = $true
            Invoke-ProcessAndWait `
                -FilePath 'pnpunattend.exe' `
                -ArgumentList @('AuditSystem', '/L') `
                -OperationName 'pnpunattend.exe'
        }
        'SurfaceMsi' {
            $logDirectory = Join-Path $env:SystemRoot 'Temp\Foundry\DriverPack'
            New-Item -Path $logDirectory -ItemType Directory -Force | Out-Null

            $logPath = Join-Path $logDirectory 'surface-driverpack.log'
            Invoke-ProcessAndWait `
                -FilePath 'msiexec.exe' `
                -ArgumentList @('/i', (ConvertTo-ProcessArgument -Value $ResolvedPackagePath), '/qn', '/norestart', '/l*v', (ConvertTo-ProcessArgument -Value $logPath)) `
                -OperationName 'Surface driver package'
        }
    }

    Write-FoundryLog "Foundry driver pack installation completed."
}
finally {
    if ($DriverPathRegistered) {
        try {
            Invoke-ProcessAndWait `
                -FilePath 'reg.exe' `
                -ArgumentList @('delete', (ConvertTo-ProcessArgument -Value $DriverPathRegistryKey), '/v', 'Path', '/f') `
                -OperationName 'Remove PnPUnattend driver path'
        }
        catch {
            Write-FoundryLog "WARNING: $($_.Exception.Message)"
        }
    }

    if (Test-Path -Path $ResolvedPackagePath -PathType Leaf) {
        Write-FoundryLog "Removing staged package: $ResolvedPackagePath"
        Remove-Item -Path $ResolvedPackagePath -Force
    }

    Stop-FoundryTranscript
}
