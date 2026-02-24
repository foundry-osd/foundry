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
$projectPath = Join-Path $repoRoot 'src\Foundry.Deploy\Foundry.Deploy.csproj'
$publishRoot = Join-Path $repoRoot 'artifacts\publish\Foundry.Deploy'
$publishProperties = @(
    'PublishSingleFile=true',
    'EnableCompressionInSingleFile=true',
    'IncludeNativeLibrariesForSelfExtract=true',
    'IncludeAllContentForSelfExtract=true',
    'PublishTrimmed=false',
    'DebugType=None',
    'GenerateDocumentationFile=false'
)

if (-not (Test-Path -Path $projectPath -PathType Leaf)) {
    throw "Foundry.Deploy project not found: '$projectPath'."
}

$runtimeIdentifiers = if ($AllRuntimes) { @('win-x64', 'win-arm64') } else { @($RuntimeIdentifier) }

foreach ($rid in $runtimeIdentifiers) {
    $outputPath = Join-Path $publishRoot $rid
    if (Test-Path $outputPath) {
        Remove-Item -Path $outputPath -Recurse -Force
    }

    New-Item -Path $outputPath -ItemType Directory -Force | Out-Null

    Write-Host "Publishing Foundry.Deploy ($rid) to $outputPath..."
    $publishArgs = @(
        'publish',
        $projectPath,
        '-c', $Configuration,
        '-r', $rid,
        '--self-contained', 'true',
        '-o', $outputPath,
        '--nologo'
    )

    foreach ($property in $publishProperties) {
        $publishArgs += "-p:$property"
    }

    dotnet @publishArgs
}

Write-Host "Publish completed."
