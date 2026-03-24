Clear-Host

#region Bootstrap Configuration
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
#endregion

#region General Helpers

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
#endregion

#region Runtime And Filesystem Helpers

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
#endregion

#region Network And Time Helpers

function Test-CommandCurlExe {
    [CmdletBinding()]
    param ()

    if (Get-Command 'curl.exe' -ErrorAction SilentlyContinue) {
        return $true
    }
    else {
        return $false
    }
}

function Save-WebFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceUrl,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $DestinationDirectory = Split-Path -Path $DestinationPath -Parent
    $DestinationName = Split-Path -Path $DestinationPath -Leaf

    if ([string]::IsNullOrWhiteSpace($DestinationDirectory) -or [string]::IsNullOrWhiteSpace($DestinationName)) {
        throw "Could not resolve DestinationDirectory or DestinationName from '$DestinationPath'."
    }

    if (-not (Test-Path "$DestinationDirectory")) {
        New-Item -Path "$DestinationDirectory" -ItemType Directory -Force -ErrorAction Stop | Out-Null
    }

    # Validate the target directory up front so transport failures are not masked by local IO issues.
    $DestinationNewItem = New-Item -Path (Join-Path $DestinationDirectory "$(Get-Random).txt") -ItemType File

    if (Test-Path $DestinationNewItem.FullName) {
        $DestinationDirectory = $DestinationNewItem | Select-Object -ExpandProperty Directory
        Remove-Item -Path $DestinationNewItem.FullName -Force | Out-Null
    }
    else {
        Write-Log "Unable to write to destination directory '$DestinationDirectory'."
        Write-Warning 'Unable to write to Destination Directory'
        return $null
    }

    $DestinationDirectoryItem = (Get-Item $DestinationDirectory -Force).FullName
    $DestinationFullName = Join-Path $DestinationDirectoryItem $DestinationName

    $SourceUrl = [Uri]::EscapeUriString($SourceUrl.Replace('%', '~')).Replace('~', '%')
    $proxyAddress = $null

    try {
        $defaultProxy = [System.Net.WebRequest]::DefaultWebProxy
        if ($null -ne $defaultProxy) {
            $proxyAddress = $defaultProxy.Address
        }
    }
    catch {
        $proxyAddress = $null
    }

    # Use curl.exe for file payloads and keep WebClient only as a compatibility fallback.
    $UseWebClient = $false
    if ($null -ne $proxyAddress) {
        $UseWebClient = $true
    }
    elseif (!(Test-CommandCurlExe)) {
        $UseWebClient = $true
    }

    if ($UseWebClient -eq $true) {
        $transportReason = if ($null -ne $proxyAddress) {
            "proxy '$proxyAddress' is configured"
        }
        else {
            'curl.exe is unavailable'
        }

        Write-Log "Downloading '$SourceUrl' to '$DestinationFullName' with System.Net.WebClient because $transportReason."
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls1
        $WebClient = New-Object System.Net.WebClient
        $WebClient.DownloadFile($SourceUrl, $DestinationFullName)
        $WebClient.Dispose()
    }
    else {
        $remoteLength = 0L
        $remoteAcceptsRanges = $false

        # A HEAD request gives us header-only metadata for logging and download resume decisions.
        try {
            Write-Log "Requesting remote headers for '$SourceUrl'."
            $remote = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $SourceUrl -ErrorAction Stop
            $contentLengthHeader = [string]($remote.Headers.'Content-Length' | Select-Object -First 1)
            $acceptRangesHeader = [string]($remote.Headers.'Accept-Ranges' | Select-Object -First 1)

            if (-not [string]::IsNullOrWhiteSpace($contentLengthHeader)) {
                [Int64]::TryParse($contentLengthHeader, [ref]$remoteLength) | Out-Null
            }

            $remoteAcceptsRanges = [string]::Equals($acceptRangesHeader, 'bytes', [System.StringComparison]::OrdinalIgnoreCase)
            Write-Log "HEAD probe for '$SourceUrl' returned Content-Length='$contentLengthHeader' and Accept-Ranges='$acceptRangesHeader'."
        }
        catch {
            Write-Log "HEAD probe for '$SourceUrl' failed: $($_.Exception.Message). Continuing with a direct curl.exe download."
        }

        Write-Log "Downloading '$SourceUrl' to '$DestinationFullName' with curl.exe."
        & curl.exe --fail --insecure --location --output $DestinationFullName --url $SourceUrl
        if ($LASTEXITCODE -ne 0) {
            throw "curl.exe failed with exit code $LASTEXITCODE."
        }

        $localExists = $false
        if (Test-Path $DestinationFullName) {
            $localExists = $true
        }

        $RetryDelaySeconds = 1
        $MaxRetryCount = 10
        $RetryCount = 0
        while (
            $localExists `
                -and ($remoteLength -gt 0) `
                -and ((Get-Item $DestinationFullName).Length -lt $remoteLength) `
                -and $remoteAcceptsRanges `
                -and ($RetryCount -lt $MaxRetryCount)
        ) {
            # Only retry with resume when the server explicitly advertises byte ranges.
            Write-Log "Download is incomplete for '$DestinationFullName'. Retrying with curl.exe resume in $RetryDelaySeconds second(s)."
            Start-Sleep -Seconds $RetryDelaySeconds
            $RetryDelaySeconds *= 2
            $RetryCount += 1
            & curl.exe --fail --insecure --location --continue-at - --output $DestinationFullName --url $SourceUrl
            if ($LASTEXITCODE -ne 0) {
                throw "curl.exe resume failed with exit code $LASTEXITCODE."
            }
        }

        if ($localExists -and ($remoteLength -gt 0) -and ((Get-Item $DestinationFullName).Length -lt $remoteLength)) {
            Write-Log "Download remained incomplete for '$DestinationFullName' after $RetryCount resume attempt(s)."
            Write-Warning "Could not download $DestinationFullName"
            return $null
        }
    }

    if (Test-Path $DestinationFullName) {
        $downloadedFile = Get-Item $DestinationFullName -Force
        Write-Log "Download completed: '$DestinationFullName' ($($downloadedFile.Length) bytes)."
        return $downloadedFile
    }
    else {
        Write-Log "Download failed because '$DestinationFullName' was not created."
        Write-Warning "Could not download $DestinationFullName"
        return $null
    }
}

function Sync-WinPeInternetDateTime {
    [CmdletBinding()]
    param(
        [ValidateRange(1, 1440)]
        [int]$ThresholdMinutes = 5
    )

    if (-not [string]::Equals($env:SystemDrive, 'X:', [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Log 'Skipping clock synchronization because the bootstrap is not running from the WinPE system drive.'
        return
    }

    $internetDateTime = $null
    $probeUrls = @(
        'http://www.msftconnecttest.com/connecttest.txt',
        'http://www.google.com'
    )

    # Use HTTP time probes so clock recovery is still possible when TLS would fail due to skew.
    foreach ($probeUrl in $probeUrls) {
        try {
            Write-Log "Requesting internet time from '$probeUrl'."
            $response = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $probeUrl -ErrorAction Stop
            $dateHeader = [string]($response.Headers['Date'] | Select-Object -First 1)

            if (-not [string]::IsNullOrWhiteSpace($dateHeader)) {
                $internetDateTime = Get-Date $dateHeader
                break
            }

            Write-Log "The time probe '$probeUrl' did not return an HTTP Date header."
        }
        catch {
            Write-Log "The time probe '$probeUrl' failed: $($_.Exception.Message)."
        }
    }

    if ($null -eq $internetDateTime) {
        Write-Log 'Could not resolve internet time. Continuing without clock synchronization.'
        return
    }

    $localDateTime = Get-Date
    $differenceMinutes = [Math]::Abs(($internetDateTime - $localDateTime).TotalMinutes)
    $roundedDifferenceMinutes = [Math]::Round($differenceMinutes)

    if ($differenceMinutes -le $ThresholdMinutes) {
        Write-Log "System clock is already within $ThresholdMinutes minute(s) of internet time."
        return
    }

    Write-Log "System clock differs from internet time by $roundedDifferenceMinutes minute(s). Updating the WinPE clock."

    try {
        Set-Date -Date $internetDateTime -ErrorAction Stop | Out-Null
        Write-Log "System clock updated to '$($internetDateTime.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture))'."
    }
    catch {
        Write-Log "Failed to update the WinPE clock: $($_.Exception.Message)."
    }
}
#endregion

#region Archive Integrity Helpers

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
#endregion

#region Archive Staging And Cache Helpers

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
    & $sevenZipExecutable x -y $outputArgument $ArchivePath | Out-Null
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
        # Prefer manifest comparisons because they remain stable even when executable version metadata is absent.
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
        # Extract into a staging directory first so the active cache remains valid until promotion succeeds.
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
#endregion

#region Release Resolution Helpers

function Start-DeployExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Executable
    )

    Write-Log "Launching '$($Executable.FullName)'."
    Start-Process -FilePath $Executable.FullName -WorkingDirectory $Executable.DirectoryName | Out-Null
}

function Resolve-SingleExecutable {
    param(
        [Parameter(Mandatory = $true)]
        $Candidate
    )

    $candidates = @($Candidate)
    $fileInfo = $candidates | Where-Object { $_ -is [System.IO.FileInfo] } | Select-Object -Last 1
    if ($null -ne $fileInfo) {
        return $fileInfo
    }

    $pathCandidate = $candidates | Where-Object { $_ -is [string] -and -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
    if ($null -ne $pathCandidate -and (Test-Path -Path $pathCandidate -PathType Leaf)) {
        return Get-Item -Path $pathCandidate
    }

    $typeName = if ($null -eq $Candidate) { '<null>' } else { $Candidate.GetType().FullName }
    throw "Could not resolve a single deploy executable from value of type '$typeName'."
}
#endregion

#region Bootstrap Execution

try {
    Ensure-Directory -Path $WinPeRoot

    # USB cache media takes precedence; otherwise the ISO-backed runtime directory is used.
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
    Sync-WinPeInternetDateTime -ThresholdMinutes 5

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

    # An explicit archive override short-circuits GitHub release discovery.
    if (-not [string]::IsNullOrWhiteSpace($archiveOverride)) {
        if (-not [string]::IsNullOrWhiteSpace($releaseTagOverride)) {
            Write-Log "FOUNDRY_DEPLOY_ARCHIVE is set; ignoring FOUNDRY_RELEASE_TAG '$releaseTagOverride'."
        }

        if (Test-HttpUrl -Value $archiveOverride) {
            Write-Log "Starting override archive download from '$archiveOverride'."
            Save-WebFile `
                -SourceUrl $archiveOverride `
                -DestinationPath $downloadPath | Out-Null
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
        # Default flow: resolve the latest release, reuse cache when current, otherwise refresh it.
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
                    Write-Log "Starting release asset download from '$($asset.browser_download_url)'."
                    Save-WebFile `
                        -SourceUrl $asset.browser_download_url `
                        -DestinationPath $downloadPath | Out-Null

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

    $executable = Resolve-SingleExecutable -Candidate $executable
    Start-DeployExecutable -Executable $executable
    Write-Log 'Foundry bootstrap completed successfully.'
}
catch {
    Write-Log "Foundry bootstrap failed: $($_.Exception.Message)"
}
#endregion
