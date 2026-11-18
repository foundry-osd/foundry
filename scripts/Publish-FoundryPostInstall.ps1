param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')][string]$RuntimeIdentifier = 'win-x64',
    [switch]$AllRuntimes,
    [string]$OutputRoot,
    # Release tag recorded in the manifest, for example v26.11.2.1. Informational only: Foundry.Deploy never compares it.
    [string]$ReleaseTag
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts\post-install' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
foreach ($rid in $(if ($AllRuntimes) { @('win-x64', 'win-arm64') } else { @($RuntimeIdentifier) })) {
    $platform = if ($rid -eq 'win-x64') { 'x64' } else { 'ARM64' }
    $publish = Join-Path $OutputRoot ('publish\' + $rid + '\' + [Guid]::NewGuid().ToString('N'))
    # Foundry.PostInstall is a single-file executable copied into boot.wim and run in place in the target OS: it extracts
    # nothing at startup and its bundle is not compressed because boot.wim already compresses it.
    dotnet publish (Join-Path $repoRoot 'src\Foundry.PostInstall\Foundry.PostInstall.csproj') -c $Configuration -r $rid --self-contained true -o $publish --nologo "-p:Platform=$platform" -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -p:IncludeAllContentForSelfExtract=false -p:DebugType=None -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "PostInstall publication failed for $rid." }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'Foundry.PostInstall.exe'))) { throw 'Published runner executable is missing.' }
    $files = @('Foundry.PostInstall.exe', 'Launch.cmd' | ForEach-Object {
        $path = Join-Path $publish $_
        [ordered]@{ relativePath = $_; length = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{ schemaVersion = 1; contractVersion = 1; runtimeIdentifier = $rid }
    if (-not [string]::IsNullOrWhiteSpace($ReleaseTag)) { $manifest.releaseTag = $ReleaseTag.Trim() }
    $manifest.files = $files
    [IO.File]::WriteAllText((Join-Path $publish 'foundry.postinstall.json'), ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    # Only the launch files enter the archive; Foundry OSD copies exactly these three files into the boot image.
    $archive = Join-Path $OutputRoot "Foundry.PostInstall-$rid.zip"
    $launchFiles = @('Foundry.PostInstall.exe', 'Launch.cmd', 'foundry.postinstall.json' | ForEach-Object { Join-Path $publish $_ })
    Compress-Archive -LiteralPath $launchFiles -DestinationPath $archive -CompressionLevel Optimal -Force
}
Write-Host "PostInstall archives published to $OutputRoot."
