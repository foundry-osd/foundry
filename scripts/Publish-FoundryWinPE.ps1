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
$projectPath = Join-Path $repoRoot 'src\Foundry.WinPE\Foundry.WinPE.csproj'
$publishRoot = Join-Path $repoRoot 'artifacts\publish\Foundry.WinPE'
# Foundry.WinPE is a self-contained single-file executable copied directly into the boot image. WPF's native libraries
# stay beside it so nothing is extracted into the WinPE RAM disk at startup, and the bundle is not compressed because
# boot.wim already compresses the payload.
$publishProperties = @(
    'PublishSingleFile=true',
    'EnableCompressionInSingleFile=false',
    'IncludeNativeLibrariesForSelfExtract=false',
    'IncludeAllContentForSelfExtract=false',
    'DebugType=None',
    'GenerateDocumentationFile=false'
)
$requiredFiles = @(
    'Foundry.WinPE.exe',
    'foundry.startup.json'
)

if (-not (Test-Path -Path $projectPath -PathType Leaf)) {
    throw "Foundry.WinPE project not found: '$projectPath'."
}

$runtimeIdentifiers = if ($AllRuntimes) { @('win-x64', 'win-arm64') } else { @($RuntimeIdentifier) }

foreach ($rid in $runtimeIdentifiers) {
    $platform = if ($rid -eq 'win-x64') { 'x64' } else { 'ARM64' }
    $outputPath = Join-Path $publishRoot $rid
    if (Test-Path $outputPath) {
        Remove-Item -Path $outputPath -Recurse -Force
    }

    New-Item -Path $outputPath -ItemType Directory -Force | Out-Null

    Write-Host "Publishing Foundry.WinPE ($rid) to $outputPath..."
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
        throw "dotnet publish failed for Foundry.WinPE ($rid) with exit code $LASTEXITCODE."
    }

    foreach ($file in $requiredFiles) {
        if (-not (Test-Path -Path (Join-Path $outputPath $file) -PathType Leaf)) {
            throw "Foundry.WinPE publish output ($rid) is missing '$file' at the publish root."
        }
    }

    $startupManifests = @(Get-ChildItem -Path $outputPath -Filter 'foundry.startup.json' -File -Recurse)
    if ($startupManifests.Count -ne 1) {
        throw "Foundry.WinPE publish output ($rid) must contain exactly one 'foundry.startup.json'; found $($startupManifests.Count)."
    }
}

Write-Host "Publish completed."
