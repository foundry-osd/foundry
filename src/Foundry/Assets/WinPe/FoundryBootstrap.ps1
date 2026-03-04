Clear-Host

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PSNativeCommandUseErrorActionPreference = $true

$WinPeRoot = 'X:\Foundry'
$LogPath = Join-Path $WinPeRoot 'Logs\FoundryDeploy.log'
$Owner = 'mchave3'
$Repository = 'Foundry'
$ReleaseApiBaseUrl = "https://api.github.com/repos/$Owner/$Repository/releases"
$EmbeddedArchivePath = Join-Path $WinPeRoot 'Seed\Foundry.Deploy.zip'
$SevenZipToolsPath = Join-Path $WinPeRoot 'Tools\7zip'

function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $timestamp = [DateTime]::UtcNow.ToString(
        'yyyy-MM-dd HH:mm:ss',
        [System.Globalization.CultureInfo]::InvariantCulture
    )
    $entry = "[$timestamp UTC] $Message"

    try {
        $directory = Split-Path -Path $LogPath -Parent
        if (-not (Test-Path -Path $directory)) {
            New-Item -Path $directory -ItemType Directory -Force | Out-Null
        }

        $entry | Out-File -FilePath $LogPath -Encoding utf8 -Append
    }
    catch {
        # Keep bootstrap resilient even if logging fails.
    }

    try {
        Write-Host $entry
    }
    catch {
        # Keep bootstrap resilient even if console output fails.
    }
}

function Get-TargetRuntimeIdentifier {
    $architecture = [string]$env:PROCESSOR_ARCHITECTURE
    $architecture = $architecture.Trim().ToUpperInvariant()

    switch ($architecture) {
        'AMD64' { return 'win-x64' }
        'ARM64' { return 'win-arm64' }
        Default { throw "Unsupported architecture '$architecture'." }
    }
}

function Resolve-DeployAssetName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    switch ($RuntimeIdentifier) {
        'win-x64' { return 'Foundry.Deploy-win-x64.zip' }
        'win-arm64' { return 'Foundry.Deploy-win-arm64.zip' }
        Default { throw "Unsupported runtime '$RuntimeIdentifier'." }
    }
}

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,
        [int]$MaxAttempts = 3,
        [int]$InitialDelaySeconds = 2
    )

    $attempt = 1
    $delay = $InitialDelaySeconds

    while ($attempt -le $MaxAttempts) {
        try {
            return & $Action
        }
        catch {
            if ($attempt -ge $MaxAttempts) {
                throw
            }

            Write-Log "Attempt $attempt failed: $($_.Exception.Message). Retrying in $delay second(s)."
            Start-Sleep -Seconds $delay
            $delay = [Math]::Min($delay * 2, 20)
            $attempt++
        }
    }
}

function Ensure-BitsAvailable {
    if (-not (Get-Command -Name Start-BitsTransfer -ErrorAction SilentlyContinue)) {
        throw 'Start-BitsTransfer is not available in this WinPE image.'
    }

    try {
        $service = Get-Service -Name BITS -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -ne 'Running') {
            Start-Service -Name BITS -ErrorAction SilentlyContinue
        }
    }
    catch {
        Write-Log "Unable to start BITS service explicitly: $($_.Exception.Message). Continuing."
    }
}

function Test-HttpUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    try {
        $uri = [System.Uri]$Value
        return $uri.IsAbsoluteUri -and ($uri.Scheme -eq 'http' -or $uri.Scheme -eq 'https')
    }
    catch {
        return $false
    }
}

function Get-UsbCacheRuntimeRoot {
    foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
        if (-not $drive.IsReady) {
            continue
        }

        $rootPath = $drive.RootDirectory.FullName

        try {
            if ([string]::Equals($drive.VolumeLabel, 'Foundry Cache', [System.StringComparison]::OrdinalIgnoreCase)) {
                return Join-Path $rootPath 'Runtime'
            }
        }
        catch {
            # Ignore drives that do not expose a readable volume label.
        }

        $markerPath = Join-Path $rootPath 'Foundry Cache'
        if (Test-Path -Path $markerPath -PathType Container) {
            return Join-Path $rootPath 'Runtime'
        }
    }

    return $null
}

function Ensure-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -Path $Path -PathType Container)) {
        New-Item -Path $Path -ItemType Directory -Force | Out-Null
    }
}

function Remove-DirectoryIfPresent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -Path $Path -PathType Container) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Remove-FileIfPresent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -Path $Path -PathType Leaf) {
        Remove-Item -Path $Path -Force
    }
}

function Get-RuntimeCacheRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BootstrapRoot,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    return Join-Path $BootstrapRoot $RuntimeIdentifier
}

function Get-StagingRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BootstrapRoot,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    return Join-Path $BootstrapRoot "$RuntimeIdentifier.staging"
}

function Get-TemporaryArchivePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BootstrapRoot,
        [Parameter(Mandatory = $true)]
        [string]$AssetName
    )

    return Join-Path $BootstrapRoot "$AssetName.download"
}

function Get-ManifestPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    return Join-Path $RootPath 'manifest'
}

function Get-DeployExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    return Join-Path $RootPath 'Foundry.Deploy.exe'
}

function Download-FileViaBits {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceUrl,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    Ensure-BitsAvailable

    Invoke-WithRetry -Action {
        Remove-FileIfPresent -Path $DestinationPath

        Start-BitsTransfer `
            -Source $SourceUrl `
            -Destination $DestinationPath `
            -TransferType Download `
            -Description $Description `
            -ErrorAction Stop
    }
}

function Copy-LocalArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (-not (Test-Path -Path $SourcePath -PathType Leaf)) {
        throw "Override archive path not found: '$SourcePath'."
    }

    Remove-FileIfPresent -Path $DestinationPath
    Copy-Item -Path $SourcePath -Destination $DestinationPath -Force
}

function Get-FileSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-ReleaseAssetSha256 {
    param(
        [Parameter(Mandatory = $true)]
        $Asset
    )

    $digest = [string]($Asset.digest)
    if ([string]::IsNullOrWhiteSpace($digest)) {
        return $null
    }

    if (-not $digest.StartsWith('sha256:', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return $digest.Substring(7).Trim().ToUpperInvariant()
}

function Assert-ExpectedSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ActualSha256,
        [string]$ExpectedSha256,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedSha256)) {
        return
    }

    $normalized = $ExpectedSha256.Trim().ToUpperInvariant()
    if ($normalized -notmatch '^[0-9A-F]{64}$') {
        throw "Invalid SHA256 value '$ExpectedSha256' for $Context."
    }

    if ($normalized -ne $ActualSha256) {
        throw "SHA256 mismatch for $Context. Expected '$normalized', actual '$ActualSha256'."
    }
}

function Ensure-7ZipTooling {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    $runtimeFolder = switch ($RuntimeIdentifier) {
        'win-x64' { 'x64' }
        'win-arm64' { 'arm64' }
        Default { throw "Unsupported runtime '$RuntimeIdentifier' for 7-Zip tools." }
    }

    $runtimeExecutable = Join-Path (Join-Path $SevenZipToolsPath $runtimeFolder) '7za.exe'
    if (Test-Path -Path $runtimeExecutable -PathType Leaf) {
        return $runtimeExecutable
    }

    throw "7-Zip executable was not provisioned in this image. Expected path: '$runtimeExecutable'."
}

function Expand-ZipVia7Zip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    $sevenZipExecutable = Ensure-7ZipTooling -RuntimeIdentifier $RuntimeIdentifier
    Ensure-Directory -Path $DestinationPath

    $outputArgument = "-o$DestinationPath"
    & $sevenZipExecutable x -y $outputArgument $ArchivePath
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip extraction failed with exit code $LASTEXITCODE."
    }
}

function Read-Manifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath
    )

    if (-not (Test-Path -Path $ManifestPath -PathType Leaf)) {
        return $null
    }

    $data = @{}
    foreach ($line in Get-Content -Path $ManifestPath) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $separatorIndex = $line.IndexOf('=')
        if ($separatorIndex -lt 1) {
            continue
        }

        $key = $line.Substring(0, $separatorIndex).Trim()
        $value = $line.Substring($separatorIndex + 1).Trim()
        if (-not [string]::IsNullOrWhiteSpace($key)) {
            $data[$key] = $value
        }
    }

    if ($data.Count -eq 0) {
        return $null
    }

    return $data
}

function Write-Manifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [string]$Tag,
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$AssetName,
        [Parameter(Mandatory = $true)]
        [string]$ArchiveSha256
    )

    $updatedUtc = [DateTime]::UtcNow.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
    $lines = @(
        "Tag=$Tag",
        "Version=$Version",
        "Asset=$AssetName",
        "ArchiveSha256=$ArchiveSha256",
        "UpdatedUtc=$updatedUtc"
    )

    $lines | Out-File -FilePath $ManifestPath -Encoding utf8
}

function Get-ExecutableVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath
    )

    if (-not (Test-Path -Path $ExecutablePath -PathType Leaf)) {
        return $null
    }

    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath).FileVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        return $null
    }

    return $version.Trim()
}

function Get-ReleaseVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag
    )

    $normalizedTag = $Tag.Trim()
    if ($normalizedTag.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $normalizedTag.Substring(1)
    }

    return $normalizedTag
}

function Resolve-ReleaseApiUrl {
    param(
        [string]$ReleaseTagOverride
    )

    if ([string]::IsNullOrWhiteSpace($ReleaseTagOverride)) {
        return "$ReleaseApiBaseUrl/latest"
    }

    $encodedTag = [System.Uri]::EscapeDataString($ReleaseTagOverride)
    return "$ReleaseApiBaseUrl/tags/$encodedTag"
}

function Get-ReleaseAsset {
    param(
        [Parameter(Mandatory = $true)]
        $Release,
        [Parameter(Mandatory = $true)]
        [string]$AssetName
    )

    $asset = $Release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    if ($null -eq $asset) {
        throw "No deploy asset named '$AssetName' was found in release '$($Release.tag_name)'."
    }

    return $asset
}

function Resolve-CachedExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeCacheRoot
    )

    $executablePath = Get-DeployExecutablePath -RootPath $RuntimeCacheRoot
    if (-not (Test-Path -Path $executablePath -PathType Leaf)) {
        throw "No cached Foundry.Deploy executable is available in '$RuntimeCacheRoot'."
    }

    return Get-Item -Path $executablePath
}

function Test-CacheCurrent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeCacheRoot,
        [Parameter(Mandatory = $true)]
        [string]$AssetName,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseTag,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseVersion,
        [Parameter(Mandatory = $true)]
        $Asset
    )

    $executablePath = Get-DeployExecutablePath -RootPath $RuntimeCacheRoot
    if (-not (Test-Path -Path $executablePath -PathType Leaf)) {
        return $false
    }

    $manifest = Read-Manifest -ManifestPath (Get-ManifestPath -RootPath $RuntimeCacheRoot)
    if ($null -ne $manifest) {
        $manifestAsset = [string]$manifest['Asset']
        if (-not [string]::Equals($manifestAsset, $AssetName, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }

        $expectedSha256 = Get-ReleaseAssetSha256 -Asset $Asset
        if (-not [string]::IsNullOrWhiteSpace($expectedSha256)) {
            $manifestSha256 = [string]$manifest['ArchiveSha256']
            return [string]::Equals($manifestSha256, $expectedSha256, [System.StringComparison]::OrdinalIgnoreCase)
        }

        $manifestTag = [string]$manifest['Tag']
        if (-not [string]::IsNullOrWhiteSpace($manifestTag)) {
            return [string]::Equals($manifestTag, $ReleaseTag, [System.StringComparison]::OrdinalIgnoreCase)
        }

        $manifestVersion = [string]$manifest['Version']
        if (-not [string]::IsNullOrWhiteSpace($manifestVersion)) {
            return [string]::Equals($manifestVersion, $ReleaseVersion, [System.StringComparison]::OrdinalIgnoreCase)
        }
    }

    $cachedVersion = Get-ExecutableVersion -ExecutablePath $executablePath
    if ([string]::IsNullOrWhiteSpace($cachedVersion)) {
        return $false
    }

    return [string]::Equals($cachedVersion, $ReleaseVersion, [System.StringComparison]::OrdinalIgnoreCase)
}

function Promote-StagedCache {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StagingRoot,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeCacheRoot
    )

    $backupRoot = "$RuntimeCacheRoot.previous"
    $activeMoved = $false

    Remove-DirectoryIfPresent -Path $backupRoot

    try {
        if (Test-Path -Path $RuntimeCacheRoot -PathType Container) {
            Move-Item -Path $RuntimeCacheRoot -Destination $backupRoot
            $activeMoved = $true
        }

        Move-Item -Path $StagingRoot -Destination $RuntimeCacheRoot
        Remove-DirectoryIfPresent -Path $backupRoot
    }
    catch {
        if ($activeMoved -and -not (Test-Path -Path $RuntimeCacheRoot -PathType Container) -and (Test-Path -Path $backupRoot -PathType Container)) {
            Move-Item -Path $backupRoot -Destination $RuntimeCacheRoot
        }

        throw
    }
}

function Update-CacheFromArchiveFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$BootstrapRoot,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeCacheRoot,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)]
        [string]$AssetName,
        [string]$Tag,
        [string]$Version,
        [string]$ArchiveSha256
    )

    $stagingRoot = Get-StagingRoot -BootstrapRoot $BootstrapRoot -RuntimeIdentifier $RuntimeIdentifier
    Remove-DirectoryIfPresent -Path $stagingRoot

    try {
        Ensure-Directory -Path $stagingRoot
        Expand-ZipVia7Zip -ArchivePath $ArchivePath -DestinationPath $stagingRoot -RuntimeIdentifier $RuntimeIdentifier

        $stagedExecutablePath = Get-DeployExecutablePath -RootPath $stagingRoot
        if (-not (Test-Path -Path $stagedExecutablePath -PathType Leaf)) {
            throw "The extracted deploy cache does not contain 'Foundry.Deploy.exe'."
        }

        $resolvedVersion = $Version
        if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
            $resolvedVersion = Get-ExecutableVersion -ExecutablePath $stagedExecutablePath
        }

        if ([string]::IsNullOrWhiteSpace($ArchiveSha256)) {
            $ArchiveSha256 = Get-FileSha256 -Path $ArchivePath
        }

        Write-Manifest `
            -ManifestPath (Get-ManifestPath -RootPath $stagingRoot) `
            -Tag $Tag `
            -Version $resolvedVersion `
            -AssetName $AssetName `
            -ArchiveSha256 $ArchiveSha256

        Promote-StagedCache -StagingRoot $stagingRoot -RuntimeCacheRoot $RuntimeCacheRoot
        return Resolve-CachedExecutable -RuntimeCacheRoot $RuntimeCacheRoot
    }
    finally {
        Remove-FileIfPresent -Path $ArchivePath
        Remove-DirectoryIfPresent -Path $stagingRoot
    }
}

function Start-DeployExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Executable
    )

    Write-Log "Launching '$($Executable.FullName)'."
    Start-Process -FilePath $Executable.FullName -WorkingDirectory $Executable.DirectoryName | Out-Null
}

try {
    Ensure-Directory -Path $WinPeRoot

    $usbRuntimeRoot = Get-UsbCacheRuntimeRoot
    if (-not [string]::IsNullOrWhiteSpace($usbRuntimeRoot)) {
        $bootstrapRoot = $usbRuntimeRoot
        $deploymentMode = 'Usb'
    }
    else {
        $bootstrapRoot = Join-Path $WinPeRoot 'Runtime'
        $deploymentMode = 'Iso'
    }

    Ensure-Directory -Path $bootstrapRoot

    Write-Log 'Foundry bootstrap started.'
    Write-Log "Bootstrap root resolved to '$bootstrapRoot'."
    Write-Log "Deployment mode resolved to '$deploymentMode'."

    $env:FOUNDRY_DEPLOYMENT_MODE = $deploymentMode

    $runtimeIdentifier = Get-TargetRuntimeIdentifier
    $assetName = Resolve-DeployAssetName -RuntimeIdentifier $runtimeIdentifier
    $runtimeCacheRoot = Get-RuntimeCacheRoot -BootstrapRoot $bootstrapRoot -RuntimeIdentifier $runtimeIdentifier
    $downloadPath = Get-TemporaryArchivePath -BootstrapRoot $bootstrapRoot -AssetName $assetName
    $releaseTagOverride = [string]$env:FOUNDRY_RELEASE_TAG
    $releaseTagOverride = $releaseTagOverride.Trim()
    $archiveOverride = [string]$env:FOUNDRY_DEPLOY_ARCHIVE
    $archiveOverride = $archiveOverride.Trim()
    $archiveOverrideSha256 = [string]$env:FOUNDRY_DEPLOY_ARCHIVE_SHA256
    $archiveOverrideSha256 = $archiveOverrideSha256.Trim()
    $executable = $null

    if ([string]::IsNullOrWhiteSpace($archiveOverride) -and (Test-Path -Path $EmbeddedArchivePath -PathType Leaf)) {
        $archiveOverride = $EmbeddedArchivePath
        Write-Log "Using embedded deploy archive from '$EmbeddedArchivePath'."
    }

    $headers = @{
        'User-Agent' = 'FoundryBootstrap/1.0'
        'Accept' = 'application/vnd.github+json'
    }

    if (-not [string]::IsNullOrWhiteSpace($archiveOverride)) {
        if (-not [string]::IsNullOrWhiteSpace($releaseTagOverride)) {
            Write-Log "FOUNDRY_DEPLOY_ARCHIVE is set; ignoring FOUNDRY_RELEASE_TAG '$releaseTagOverride'."
        }

        if (Test-HttpUrl -Value $archiveOverride) {
            Write-Log "Downloading override archive via BITS: $archiveOverride"
            Download-FileViaBits `
                -SourceUrl $archiveOverride `
                -DestinationPath $downloadPath `
                -Description "Foundry Deploy bootstrap override download ($runtimeIdentifier)"
        }
        else {
            Write-Log "Copying override archive from '$archiveOverride'."
            Copy-LocalArchive -SourcePath $archiveOverride -DestinationPath $downloadPath
        }

        $archiveSha256 = Get-FileSha256 -Path $downloadPath
        Assert-ExpectedSha256 -ActualSha256 $archiveSha256 -ExpectedSha256 $archiveOverrideSha256 -Context 'override archive'

        Write-Log "Refreshing deploy cache for runtime '$runtimeIdentifier' from override archive."
        $executable = Update-CacheFromArchiveFile `
            -ArchivePath $downloadPath `
            -BootstrapRoot $bootstrapRoot `
            -RuntimeCacheRoot $runtimeCacheRoot `
            -RuntimeIdentifier $runtimeIdentifier `
            -AssetName $assetName `
            -Tag '' `
            -Version '' `
            -ArchiveSha256 $archiveSha256
    }
    else {
        $releaseApiUrl = Resolve-ReleaseApiUrl -ReleaseTagOverride $releaseTagOverride
        $release = $null

        try {
            Write-Log "Resolving release metadata from $releaseApiUrl."
            $release = Invoke-WithRetry -Action { Invoke-RestMethod -Uri $releaseApiUrl -Headers $headers -Method Get }
        }
        catch {
            Write-Log "Failed to resolve release metadata: $($_.Exception.Message). Falling back to the existing cache."
            $executable = Resolve-CachedExecutable -RuntimeCacheRoot $runtimeCacheRoot
        }

        if ($null -eq $executable) {
            $asset = Get-ReleaseAsset -Release $release -AssetName $assetName
            $releaseTag = [string]$release.tag_name
            $releaseVersion = Get-ReleaseVersion -Tag $releaseTag

            Write-Log "Using release tag '$releaseTag' and asset '$($asset.name)'."

            if (Test-CacheCurrent `
                -RuntimeCacheRoot $runtimeCacheRoot `
                -AssetName $assetName `
                -ReleaseTag $releaseTag `
                -ReleaseVersion $releaseVersion `
                -Asset $asset) {
                Write-Log "The cached deploy content for '$runtimeIdentifier' is already current."
                $executable = Resolve-CachedExecutable -RuntimeCacheRoot $runtimeCacheRoot
            }
            else {
                try {
                    Write-Log "Downloading asset via BITS: $($asset.browser_download_url)"
                    Download-FileViaBits `
                        -SourceUrl $asset.browser_download_url `
                        -DestinationPath $downloadPath `
                        -Description "Foundry Deploy bootstrap download ($runtimeIdentifier)"

                    $archiveSha256 = Get-FileSha256 -Path $downloadPath
                    $expectedSha256 = Get-ReleaseAssetSha256 -Asset $asset
                    if (-not [string]::IsNullOrWhiteSpace($expectedSha256)) {
                        Assert-ExpectedSha256 -ActualSha256 $archiveSha256 -ExpectedSha256 $expectedSha256 -Context "release asset '$($asset.name)'"
                    }
                    elseif (-not [string]::IsNullOrWhiteSpace([string]$asset.digest)) {
                        Write-Log "Release asset digest '$($asset.digest)' is not a supported SHA256 value. Continuing without digest validation."
                    }
                    else {
                        Write-Log 'No release asset digest was provided. Continuing without digest validation.'
                    }

                    Write-Log "Refreshing deploy cache for runtime '$runtimeIdentifier'."
                    $executable = Update-CacheFromArchiveFile `
                        -ArchivePath $downloadPath `
                        -BootstrapRoot $bootstrapRoot `
                        -RuntimeCacheRoot $runtimeCacheRoot `
                        -RuntimeIdentifier $runtimeIdentifier `
                        -AssetName $assetName `
                        -Tag $releaseTag `
                        -Version $releaseVersion `
                        -ArchiveSha256 $archiveSha256
                }
                catch {
                    Write-Log "Failed to refresh the deploy cache: $($_.Exception.Message). Falling back to the existing cache."
                    $executable = Resolve-CachedExecutable -RuntimeCacheRoot $runtimeCacheRoot
                }
            }
        }
    }

    Start-DeployExecutable -Executable $executable
    Write-Log 'Foundry bootstrap completed successfully.'
}
catch {
    Write-Log "Foundry bootstrap failed: $($_.Exception.Message)"
}
