param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')][string]$RuntimeIdentifier = 'win-x64',
    [switch]$AllRuntimes,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'artifacts\post-install' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
foreach ($rid in $(if ($AllRuntimes) { @('win-x64', 'win-arm64') } else { @($RuntimeIdentifier) })) {
    $platform = if ($rid -eq 'win-x64') { 'x64' } else { 'ARM64' }
    $publish = Join-Path $OutputRoot ('publish\' + $rid + '\' + [Guid]::NewGuid().ToString('N'))
    dotnet publish (Join-Path $repoRoot 'src\Foundry.PostInstall\Foundry.PostInstall.csproj') -c $Configuration -r $rid --self-contained true -o $publish --nologo "-p:Platform=$platform" -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "PostInstall publication failed for $rid." }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'Foundry.PostInstall.exe'))) { throw 'Published runner executable is missing.' }
    $files = @('Foundry.PostInstall.exe', 'Launch.cmd' | ForEach-Object {
        $path = Join-Path $publish $_
        [ordered]@{ relativePath = $_; length = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{ schemaVersion = 1; contractVersion = 2; runtimeIdentifier = $rid; files = $files }
    [IO.File]::WriteAllText((Join-Path $publish 'foundry.postinstall.json'), ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    $archive = Join-Path $OutputRoot "Foundry.PostInstall-$rid.zip"
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -CompressionLevel Optimal -Force
}
Write-Host "PostInstall archives published to $OutputRoot."
