param(
    [string]$ArchivePath = '',
    [string]$BootstrapArchivePath = '',
    [string]$ConnectArchivePath = '',
    [string]$DeployArchivePath = '',
    [switch]$RunFoundry
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$BootstrapProjectPath = Join-Path $RepoRoot 'src\Foundry.Bootstrap\Foundry.Bootstrap.csproj'
$ConnectProjectPath = Join-Path $RepoRoot 'src\Foundry.Connect\Foundry.Connect.csproj'
$DeployProjectPath = Join-Path $RepoRoot 'src\Foundry.Deploy\Foundry.Deploy.csproj'
$FoundryProjectPath = Join-Path $RepoRoot 'src\Foundry\Foundry.csproj'

if (-not (Test-Path -Path $BootstrapProjectPath -PathType Leaf)) {
    throw "Foundry.Bootstrap project not found: '$BootstrapProjectPath'."
}

if (-not (Test-Path -Path $ConnectProjectPath -PathType Leaf)) {
    throw "Foundry.Connect project not found: '$ConnectProjectPath'."
}

if (-not (Test-Path -Path $DeployProjectPath -PathType Leaf)) {
    throw "Foundry.Deploy project not found: '$DeployProjectPath'."
}

if (-not [string]::IsNullOrWhiteSpace($ArchivePath) -and [string]::IsNullOrWhiteSpace($DeployArchivePath)) {
    $DeployArchivePath = $ArchivePath
}

$env:FOUNDRY_WINPE_DEBUG_BOOTSTRAP = '1'
$env:FOUNDRY_WINPE_DEBUG_BOOTSTRAP_PROJECT = $BootstrapProjectPath

if ([string]::IsNullOrWhiteSpace($BootstrapArchivePath)) {
    $env:FOUNDRY_WINPE_DEBUG_BOOTSTRAP_ARCHIVE = ''
}
else {
    $resolvedBootstrapArchivePath = Resolve-Path $BootstrapArchivePath
    if (-not (Test-Path -Path $resolvedBootstrapArchivePath -PathType Leaf)) {
        throw "Foundry.Bootstrap archive not found: '$BootstrapArchivePath'."
    }

    $env:FOUNDRY_WINPE_DEBUG_BOOTSTRAP_ARCHIVE = $resolvedBootstrapArchivePath
}

$env:FOUNDRY_WINPE_DEBUG_CONNECT = '1'
$env:FOUNDRY_WINPE_DEBUG_CONNECT_PROJECT = $ConnectProjectPath
$env:FOUNDRY_WINPE_DEBUG_DEPLOY = '1'
$env:FOUNDRY_WINPE_DEBUG_DEPLOY_PROJECT = $DeployProjectPath

if ([string]::IsNullOrWhiteSpace($ConnectArchivePath)) {
    $env:FOUNDRY_WINPE_DEBUG_CONNECT_ARCHIVE = ''
}
else {
    $resolvedConnectArchivePath = Resolve-Path $ConnectArchivePath
    if (-not (Test-Path -Path $resolvedConnectArchivePath -PathType Leaf)) {
        throw "Foundry.Connect archive not found: '$ConnectArchivePath'."
    }

    $env:FOUNDRY_WINPE_DEBUG_CONNECT_ARCHIVE = $resolvedConnectArchivePath
}

if ([string]::IsNullOrWhiteSpace($DeployArchivePath)) {
    $env:FOUNDRY_WINPE_DEBUG_DEPLOY_ARCHIVE = ''
}
else {
    $resolvedDeployArchivePath = Resolve-Path $DeployArchivePath
    if (-not (Test-Path -Path $resolvedDeployArchivePath -PathType Leaf)) {
        throw "Foundry.Deploy archive not found: '$DeployArchivePath'."
    }

    $env:FOUNDRY_WINPE_DEBUG_DEPLOY_ARCHIVE = $resolvedDeployArchivePath
}

Write-Host "Local WinPE Bootstrap/Connect/Deploy mode enabled."
Write-Host "FOUNDRY_WINPE_DEBUG_BOOTSTRAP=$($env:FOUNDRY_WINPE_DEBUG_BOOTSTRAP)"
Write-Host "FOUNDRY_WINPE_DEBUG_BOOTSTRAP_PROJECT=$($env:FOUNDRY_WINPE_DEBUG_BOOTSTRAP_PROJECT)"
if (-not [string]::IsNullOrWhiteSpace($env:FOUNDRY_WINPE_DEBUG_BOOTSTRAP_ARCHIVE)) {
    Write-Host "FOUNDRY_WINPE_DEBUG_BOOTSTRAP_ARCHIVE=$($env:FOUNDRY_WINPE_DEBUG_BOOTSTRAP_ARCHIVE)"
}
Write-Host "FOUNDRY_WINPE_DEBUG_CONNECT=$($env:FOUNDRY_WINPE_DEBUG_CONNECT)"
Write-Host "FOUNDRY_WINPE_DEBUG_CONNECT_PROJECT=$($env:FOUNDRY_WINPE_DEBUG_CONNECT_PROJECT)"
if (-not [string]::IsNullOrWhiteSpace($env:FOUNDRY_WINPE_DEBUG_CONNECT_ARCHIVE)) {
    Write-Host "FOUNDRY_WINPE_DEBUG_CONNECT_ARCHIVE=$($env:FOUNDRY_WINPE_DEBUG_CONNECT_ARCHIVE)"
}

Write-Host "FOUNDRY_WINPE_DEBUG_DEPLOY=$($env:FOUNDRY_WINPE_DEBUG_DEPLOY)"
Write-Host "FOUNDRY_WINPE_DEBUG_DEPLOY_PROJECT=$($env:FOUNDRY_WINPE_DEBUG_DEPLOY_PROJECT)"
if (-not [string]::IsNullOrWhiteSpace($env:FOUNDRY_WINPE_DEBUG_DEPLOY_ARCHIVE)) {
    Write-Host "FOUNDRY_WINPE_DEBUG_DEPLOY_ARCHIVE=$($env:FOUNDRY_WINPE_DEBUG_DEPLOY_ARCHIVE)"
}

if ($RunFoundry) {
    if (-not (Test-Path -Path $FoundryProjectPath -PathType Leaf)) {
        throw "Foundry project not found: '$FoundryProjectPath'."
    }

    dotnet run --project $FoundryProjectPath
}
