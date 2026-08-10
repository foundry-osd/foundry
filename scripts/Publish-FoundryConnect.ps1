param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [switch]$AllRuntimes
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot 'src\Foundry.Connect\Foundry.Connect.csproj'
$publishRoot = Join-Path $repoRoot 'artifacts\publish\Foundry.Connect'
$publishProperties = @(
    'PublishSingleFile=false',
    'PublishTrimmed=false',
    'PublishReadyToRun=false',
    'PublishAot=false',
    'DebugType=None',
    'GenerateDocumentationFile=false'
)

if (-not (Test-Path -Path $projectPath -PathType Leaf)) {
    throw "Foundry.Connect project not found: '$projectPath'."
}

$runtimeIdentifiers = if ($AllRuntimes) { @('win-x64', 'win-arm64') } else { @($RuntimeIdentifier) }

foreach ($rid in $runtimeIdentifiers) {
    $platform = if ($rid -eq 'win-x64') { 'x64' } else { 'ARM64' }
    $outputPath = Join-Path $publishRoot $rid
    if (Test-Path $outputPath) {
        Remove-Item -Path $outputPath -Recurse -Force
    }

    New-Item -Path $outputPath -ItemType Directory -Force | Out-Null

    Write-Host "Publishing Foundry.Connect ($rid) to $outputPath..."
    $publishArgs = @(
        'publish',
        $projectPath,
        '-c', $Configuration,
        '-r', $rid,
        '--self-contained', 'true',
        '-o', $outputPath,
        '--nologo',
        "-p:Platform=$platform"
    )

    foreach ($property in $publishProperties) {
        $publishArgs += "-p:$property"
    }

    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for Foundry.Connect ($rid) with exit code $LASTEXITCODE."
    }

    & (Join-Path $PSScriptRoot 'Test-FoundryRuntimePayload.ps1') `
        -Application 'Foundry.Connect' `
        -RuntimeIdentifier $rid `
        -PublishDirectory $outputPath
}

Write-Host "Publish completed."
