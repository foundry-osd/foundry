$ErrorActionPreference = 'Stop'
$LogDirectory = Join-Path $env:SystemRoot 'Temp\Foundry\Logs\PreOobe'
$TranscriptPath = Join-Path $LogDirectory 'Cleanup-PreOobe.transcript.log'
$TranscriptStarted = $false
$ScriptStartedAt = [DateTimeOffset]::Now

$RootFolders = @(
    'C:\DRIVERS',
    'C:\Drivers'
)

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

function Remove-RootFolder {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        Write-FoundryLog "Skipping missing folder: $Path"
        return
    }

    $operationStartedAt = [DateTimeOffset]::Now
    try {
        Write-FoundryLog "Removing folder: $Path"
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        $operationDuration = [DateTimeOffset]::Now - $operationStartedAt
        Write-FoundryLog "Removed folder '$Path' after $($operationDuration.ToString('c'))."
    }
    catch {
        Write-FoundryLog "WARNING: Unable to remove '$Path': $($_.Exception.Message)"
    }
}

try {
    Start-FoundryTranscript
    Write-FoundryLog "Foundry pre-OOBE cleanup started."
    foreach ($RootFolder in $RootFolders) {
        Remove-RootFolder -Path $RootFolder
    }

    Write-FoundryLog "Foundry pre-OOBE cleanup completed."
}
finally {
    Stop-FoundryTranscript
}
