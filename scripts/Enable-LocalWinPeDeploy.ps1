param(
    [string]$ArchivePath = '',
    [switch]$RunFoundry
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$DeployProjectPath = Join-Path $RepoRoot 'src\Foundry.Deploy\Foundry.Deploy.csproj'
$FoundryProjectPath = Join-Path $RepoRoot 'src\Foundry\Foundry.csproj'

if (-not (Test-Path -Path $DeployProjectPath -PathType Leaf)) {
    throw "Foundry.Deploy project not found: '$DeployProjectPath'."
}

$env:FOUNDRY_WINPE_LOCAL_DEPLOY = '1'
$env:FOUNDRY_WINPE_LOCAL_DEPLOY_PROJECT = $DeployProjectPath

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $env:FOUNDRY_WINPE_LOCAL_DEPLOY_ARCHIVE = ''
    Write-Host "Local WinPE deploy mode enabled (auto publish from project)."
}
else {
    $ResolvedArchivePath = Resolve-Path $ArchivePath
    if (-not (Test-Path -Path $ResolvedArchivePath -PathType Leaf)) {
        throw "Archive not found: '$ArchivePath'."
    }

    $env:FOUNDRY_WINPE_LOCAL_DEPLOY_ARCHIVE = $ResolvedArchivePath
    Write-Host "Local WinPE deploy mode enabled (archive override: $ResolvedArchivePath)."
}

Write-Host "FOUNDRY_WINPE_LOCAL_DEPLOY=$($env:FOUNDRY_WINPE_LOCAL_DEPLOY)"
Write-Host "FOUNDRY_WINPE_LOCAL_DEPLOY_PROJECT=$($env:FOUNDRY_WINPE_LOCAL_DEPLOY_PROJECT)"
if (-not [string]::IsNullOrWhiteSpace($env:FOUNDRY_WINPE_LOCAL_DEPLOY_ARCHIVE)) {
    Write-Host "FOUNDRY_WINPE_LOCAL_DEPLOY_ARCHIVE=$($env:FOUNDRY_WINPE_LOCAL_DEPLOY_ARCHIVE)"
}

if ($RunFoundry) {
    if (-not (Test-Path -Path $FoundryProjectPath -PathType Leaf)) {
        throw "Foundry project not found: '$FoundryProjectPath'."
    }

    dotnet run --project $FoundryProjectPath
}
