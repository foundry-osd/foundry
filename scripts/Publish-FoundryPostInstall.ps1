param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')][string]$RuntimeIdentifier = 'win-x64',
    [switch]$AllRuntimes,
    [string]$ReleaseTag = 'local',
    [string]$OutputRoot,
    [string]$DescriptorPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts\post-install' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$assets = @()
foreach ($rid in $(if ($AllRuntimes) { @('win-x64', 'win-arm64') } else { @($RuntimeIdentifier) })) {
    $platform = if ($rid -eq 'win-x64') { 'x64' } else { 'ARM64' }
    $publish = Join-Path $OutputRoot ('publish\' + $rid + '\' + [Guid]::NewGuid().ToString('N'))
    dotnet publish (Join-Path $repoRoot 'src\Foundry.PostInstall\Foundry.PostInstall.csproj') -c $Configuration -r $rid --self-contained true -o $publish --nologo "-p:Platform=$platform" -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "PostInstall publication failed for $rid." }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'Foundry.PostInstall.exe'))) { throw 'Published runner executable is missing.' }
    $launcher = @'
@echo off
setlocal
set "DOTNET_BUNDLE_EXTRACT_BASE_DIR=%SystemRoot%\Temp\Foundry\Runtime\PreOobe\Bundle"
"%SystemRoot%\Temp\Foundry\Runtime\PreOobe\Foundry.PostInstall.exe" --setup
exit /b %ERRORLEVEL%
'@
    [IO.File]::WriteAllText((Join-Path $publish 'Launch.cmd'), ($launcher -replace "`r?`n", "`r`n") + "`r`n", [Text.Encoding]::ASCII)
    $name = "Foundry.PostInstall-$rid.zip"
    $archive = Join-Path $OutputRoot $name
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -CompressionLevel Optimal -Force
    $files = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
    [long]$expanded = ($files | Measure-Object -Property Length -Sum).Sum
    $length = (Get-Item -LiteralPath $archive).Length
    if ($length -gt 256MB -or $expanded -gt 512MB -or $files.Count -gt 1024) { throw 'PostInstall runtime exceeds authenticated archive limits.' }
    $assets += [ordered]@{ runtimeIdentifier = $rid; assetName = $name; archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(); archiveLength = $length; expandedLength = $expanded; entryCount = $files.Count }
}
$descriptor = [ordered]@{ schemaVersion = 1; releaseTag = $ReleaseTag; contractVersion = 1; assets = $assets }
if (-not $DescriptorPath) { $DescriptorPath = Join-Path $OutputRoot 'foundry.postinstall-runtime.json' }
New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($DescriptorPath))) -Force | Out-Null
[IO.File]::WriteAllText($DescriptorPath, ($descriptor | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
Write-Host "PostInstall archives and descriptor published to $OutputRoot."
