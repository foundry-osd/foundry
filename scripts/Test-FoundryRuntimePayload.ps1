param(
    [Parameter(Mandatory)]
    [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
    [string]$Application,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier,

    [Parameter(Mandatory)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'

$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$requiredFiles = @(
    "$Application.exe",
    "$Application.deps.json",
    "$Application.runtimeconfig.json"
)

foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $requiredFile) -PathType Leaf)) {
        throw "Required payload file is missing: '$requiredFile'."
    }
}

$prohibitedFiles = @(
    'PresentationFramework.dll',
    'PresentationCore.dll',
    'PresentationFramework.Fluent.dll'
)

$files = @(Get-ChildItem -LiteralPath $publishRoot -File -Recurse)
foreach ($prohibitedFile in $prohibitedFiles) {
    if ($files.Name -contains $prohibitedFile) {
        throw "WPF assembly is prohibited in the runtime payload: '$prohibitedFile'."
    }
}

$configurationFile = $Application.ToLowerInvariant() + '.config.json'
if ($files.Name -contains $configurationFile) {
    throw "Media-generated configuration must not be included in the application payload: '$configurationFile'."
}

$expectedArchitecture = if ($RuntimeIdentifier -eq 'win-x64') { 'x64' } else { 'arm64' }
$expectedMachine = if ($RuntimeIdentifier -eq 'win-x64') { 0x8664 } else { 0xAA64 }
$executablePath = Join-Path $publishRoot "$Application.exe"
$stream = [System.IO.File]::OpenRead($executablePath)
try {
    $reader = [System.IO.BinaryReader]::new($stream)
    $stream.Position = 0x3c
    $peOffset = $reader.ReadInt32()
    $stream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x00004550) {
        throw "Payload entry point is not a valid PE executable: '$executablePath'."
    }

    $machine = $reader.ReadUInt16()
    if ($machine -ne $expectedMachine) {
        throw "Payload entry point architecture does not match '$RuntimeIdentifier'."
    }
}
finally {
    $stream.Dispose()
}

$depsPath = Join-Path $publishRoot "$Application.deps.json"
$depsContent = Get-Content -LiteralPath $depsPath -Raw
if ($depsContent -notmatch [regex]::Escape("$RuntimeIdentifier")) {
    throw "Dependency manifest does not identify the expected runtime '$RuntimeIdentifier'."
}

$manifestEntries = foreach ($file in $files | Sort-Object FullName) {
    if ($file.Name -eq 'artifact-manifest.json') {
        continue
    }

    $relativePath = [System.IO.Path]::GetRelativePath($publishRoot, $file.FullName).Replace('\', '/')
    [ordered]@{
        path = $relativePath
        size = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    application = $Application
    runtimeIdentifier = $RuntimeIdentifier
    architecture = $expectedArchitecture
    files = @($manifestEntries)
}

$manifestPath = Join-Path $publishRoot 'artifact-manifest.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Host "Validated $Application $RuntimeIdentifier payload ($($manifest.files.Count) files)."
Write-Host "Manifest: $manifestPath"
