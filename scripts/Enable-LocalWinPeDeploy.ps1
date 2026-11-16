param(
    # Foundry.WinPE archive (Foundry.WinPE-<rid>.zip) used instead of publishing the project.
    [Alias('ArchivePath')]
    [string]$WinPeArchivePath = '',
    [switch]$RunFoundry
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$WinPeProjectPath = Join-Path $RepoRoot 'src\Foundry.WinPE\Foundry.WinPE.csproj'
$AutopilotInteractiveProjectPath = Join-Path $RepoRoot 'src\Foundry.AutopilotInteractive\Foundry.AutopilotInteractive.csproj'
$FoundryProjectPath = Join-Path $RepoRoot 'src\Foundry\Foundry.csproj'

if (-not (Test-Path -Path $WinPeProjectPath -PathType Leaf)) {
    throw "Foundry.WinPE project not found: '$WinPeProjectPath'."
}

if (-not (Test-Path -Path $AutopilotInteractiveProjectPath -PathType Leaf)) {
    throw "Foundry.AutopilotInteractive project not found: '$AutopilotInteractiveProjectPath'."
}

# PostInstall and AutopilotInteractive follow the WinPE debug flag.
$env:FOUNDRY_WINPE_DEBUG_WINPE = '1'
$env:FOUNDRY_WINPE_DEBUG_WINPE_PROJECT = $WinPeProjectPath
$env:FOUNDRY_WINPE_DEBUG_AUTOPILOTINTERACTIVE_PROJECT = $AutopilotInteractiveProjectPath

if ([string]::IsNullOrWhiteSpace($WinPeArchivePath)) {
    $env:FOUNDRY_WINPE_DEBUG_WINPE_ARCHIVE = ''
}
else {
    $resolvedWinPeArchivePath = Resolve-Path $WinPeArchivePath
    if (-not (Test-Path -Path $resolvedWinPeArchivePath -PathType Leaf)) {
        throw "Foundry.WinPE archive not found: '$WinPeArchivePath'."
    }

    $env:FOUNDRY_WINPE_DEBUG_WINPE_ARCHIVE = $resolvedWinPeArchivePath
}

Write-Host "Local WinPE/AutopilotInteractive mode enabled."
Write-Host "FOUNDRY_WINPE_DEBUG_WINPE=$($env:FOUNDRY_WINPE_DEBUG_WINPE)"
Write-Host "FOUNDRY_WINPE_DEBUG_WINPE_PROJECT=$($env:FOUNDRY_WINPE_DEBUG_WINPE_PROJECT)"
if (-not [string]::IsNullOrWhiteSpace($env:FOUNDRY_WINPE_DEBUG_WINPE_ARCHIVE)) {
    Write-Host "FOUNDRY_WINPE_DEBUG_WINPE_ARCHIVE=$($env:FOUNDRY_WINPE_DEBUG_WINPE_ARCHIVE)"
}

Write-Host "FOUNDRY_WINPE_DEBUG_AUTOPILOTINTERACTIVE_PROJECT=$($env:FOUNDRY_WINPE_DEBUG_AUTOPILOTINTERACTIVE_PROJECT)"

if ($RunFoundry) {
    if (-not (Test-Path -Path $FoundryProjectPath -PathType Leaf)) {
        throw "Foundry project not found: '$FoundryProjectPath'."
    }

    dotnet run --project $FoundryProjectPath
}
