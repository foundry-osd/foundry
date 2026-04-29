param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version = '1.0.0',

    [string[]]$RuntimeIdentifiers = @('win-x64', 'win-arm64'),

    [string]$VelopackVersion,

    [switch]$SkipVelopack
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot 'src\Foundry\Foundry.csproj'
$publishRoot = Join-Path $repoRoot 'artifacts\publish\Foundry'
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$toolRoot = Join-Path $repoRoot 'artifacts\tools\vpk'
$iconPath = Join-Path $repoRoot 'src\Foundry\Assets\Icons\app.ico'

if (-not (Test-Path -Path $projectPath -PathType Leaf)) {
    throw "Foundry project not found: '$projectPath'."
}

function Invoke-FoundryPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    $platform = if ($RuntimeIdentifier -eq 'win-arm64') { 'ARM64' } else { 'x64' }
    $outputPath = Join-Path $publishRoot $RuntimeIdentifier

    if (Test-Path -Path $outputPath) {
        Remove-Item -Path $outputPath -Recurse -Force
    }

    New-Item -Path $outputPath -ItemType Directory -Force | Out-Null

    $publishArgs = @(
        'publish',
        $projectPath,
        '-c', $Configuration,
        '-r', $RuntimeIdentifier,
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '-p:WindowsPackageType=None',
        '-p:WindowsAppSDKSelfContained=false',
        '-p:DebugType=None',
        '-p:GenerateDocumentationFile=false',
        "-p:Platform=$platform",
        '-o', $outputPath,
        '--nologo'
    )

    Write-Host "Publishing Foundry ($RuntimeIdentifier) to $outputPath..."
    dotnet @publishArgs | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for Foundry ($RuntimeIdentifier)."
    }

    $executablePath = Join-Path $outputPath 'Foundry.exe'
    if (-not (Test-Path -Path $executablePath -PathType Leaf)) {
        throw "Expected Foundry executable not found: '$executablePath'."
    }

    $xbfCount = @(Get-ChildItem -Path $outputPath -Filter '*.xbf' -Recurse).Count
    $priCount = @(Get-ChildItem -Path $outputPath -Filter '*.pri' -Recurse).Count
    if ($xbfCount -eq 0 -or $priCount -eq 0) {
        throw "Foundry publish output is missing WinUI resources. XBF=$xbfCount PRI=$priCount Path='$outputPath'."
    }

    return [string]$outputPath
}

function Install-VelopackTool {
    if (Test-Path -Path $toolRoot) {
        Remove-Item -Path $toolRoot -Recurse -Force
    }

    New-Item -Path $toolRoot -ItemType Directory -Force | Out-Null
    dotnet tool install vpk --tool-path $toolRoot --version $VelopackVersion | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install vpk $VelopackVersion."
    }

    return [string](Join-Path $toolRoot 'vpk.exe')
}

function Get-VelopackPackageVersion {
    [xml]$project = Get-Content -Path $projectPath
    $reference = $project.Project.ItemGroup.PackageReference |
        Where-Object { $_.Include -eq 'Velopack' } |
        Select-Object -First 1

    if ($null -eq $reference -or [string]::IsNullOrWhiteSpace($reference.Version)) {
        throw "Unable to resolve the Velopack package version from '$projectPath'."
    }

    return [string]$reference.Version
}

function Invoke-Velopack {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,

        [Parameter(Mandatory = $true)]
        [string]$PackDirectory,

        [Parameter(Mandatory = $true)]
        [string]$VpkPath
    )

    $channel = "$RuntimeIdentifier-stable"

    & $VpkPath pack `
        --packId FoundryOSD.Foundry `
        --packVersion $Version `
        --packDir $PackDirectory `
        --mainExe Foundry.exe `
        --packTitle Foundry `
        --packAuthors FoundryOSD `
        --runtime $RuntimeIdentifier `
        --channel $channel `
        --outputDir $releaseRoot `
        --msi true `
        --instLocation PerMachine `
        --delta None `
        --icon $iconPath `
        --yes `
        --skip-updates

    if ($LASTEXITCODE -ne 0) {
        throw "Velopack packaging failed for Foundry ($RuntimeIdentifier)."
    }
}

New-Item -Path $publishRoot -ItemType Directory -Force | Out-Null
New-Item -Path $releaseRoot -ItemType Directory -Force | Out-Null

$publishOutputs = @{}
foreach ($runtimeIdentifier in $RuntimeIdentifiers) {
    $publishOutputs[$runtimeIdentifier] = Invoke-FoundryPublish -RuntimeIdentifier $runtimeIdentifier
}

if (-not $SkipVelopack) {
    if ([string]::IsNullOrWhiteSpace($VelopackVersion)) {
        $VelopackVersion = Get-VelopackPackageVersion
    }

    $vpkPath = Install-VelopackTool
    foreach ($runtimeIdentifier in $RuntimeIdentifiers) {
        Invoke-Velopack -RuntimeIdentifier $runtimeIdentifier -PackDirectory $publishOutputs[$runtimeIdentifier] -VpkPath $vpkPath
    }
}
