[CmdletBinding()]
param(
    [string]$SettingsPath = "$env:SystemRoot\Temp\Foundry\PreOobe\Data\Remove-AiComponents.settings.json"
)

$ErrorActionPreference = 'Stop'
$script:ScriptStartedAt = [DateTimeOffset]::Now
$script:TranscriptStarted = $false

function Start-FoundryTranscript {
    $logRoot = Join-Path $env:SystemRoot 'Temp\Foundry\Logs\PreOobe'
    New-Item -Path $logRoot -ItemType Directory -Force | Out-Null
    $transcriptPath = Join-Path $logRoot 'Remove-AiComponents.transcript.log'

    try {
        Start-Transcript -Path $transcriptPath -Force | Out-Null
        $script:TranscriptStarted = $true
    }
    catch {
        Write-Warning "Unable to start transcript '$transcriptPath': $($_.Exception.Message)"
    }
}

function Stop-FoundryTranscript {
    if (-not $script:TranscriptStarted) {
        return
    }

    try {
        Stop-Transcript | Out-Null
    }
    catch {
        Write-Warning "Unable to stop transcript: $($_.Exception.Message)"
    }
}

function Write-FoundryLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $now = [DateTimeOffset]::Now
    $elapsed = $now - $script:ScriptStartedAt
    Write-Host ("[{0:O}] [+{1:hh\:mm\:ss}] {2}" -f $now, $elapsed, $Message)
}

function Get-FoundryAiComponentRemovalSettings {
    if (-not (Test-Path -Path $SettingsPath)) {
        throw "AI component removal settings file '$SettingsPath' was not found."
    }

    return Get-Content -Path $SettingsPath -Raw | ConvertFrom-Json
}

function Get-SelectedAiAppxPackageNames {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings
    )

    $property = $Settings.PSObject.Properties['appxPackages']
    if ($null -eq $property -or $null -eq $property.Value) {
        return @()
    }

    return @($property.Value |
        Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace($_.packageName) } |
        ForEach-Object { [string]$_.packageName } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)
}

function Invoke-FoundryAction {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    try {
        Write-FoundryLog "Starting $Name."
        & $Action
        Write-FoundryLog "Completed $Name."
    }
    catch {
        Write-Warning "$Name failed: $($_.Exception.Message)"
    }
}

function Remove-FoundryProvisionedAppxPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CatalogPackageName
    )

    $packages = Get-AppxProvisionedPackage -Online |
        Where-Object { $_.PackageName -like "$CatalogPackageName*" -or $_.DisplayName -eq $CatalogPackageName }

    if (-not $packages) {
        Write-FoundryLog "Provisioned AppX package '$CatalogPackageName' was not found."
        return
    }

    foreach ($package in $packages) {
        $resolvedPackageName = [string]$package.PackageName
        Write-FoundryLog "Removing provisioned AppX package '$resolvedPackageName'."
        $removeArguments = @{
            Online = $true
            PackageName = $resolvedPackageName
            ErrorAction = 'Stop'
        }

        Remove-AppxProvisionedPackage @removeArguments | Out-Null
    }
}

Start-FoundryTranscript
try {
    Write-FoundryLog "Loading AI component removal settings from '$SettingsPath'."
    $settings = Get-FoundryAiComponentRemovalSettings

    $selectedAppxPackageNames = @(Get-SelectedAiAppxPackageNames -Settings $settings)
    foreach ($selectedPackageName in $selectedAppxPackageNames) {
        Invoke-FoundryAction -Name "Remove provisioned AppX package $selectedPackageName" -Action {
            Remove-FoundryProvisionedAppxPackage -CatalogPackageName ([string]$selectedPackageName)
        }
    }

    Write-FoundryLog 'AI AppX package removal completed.'
}
finally {
    Stop-FoundryTranscript
}
