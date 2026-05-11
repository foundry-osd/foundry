Clear-Host

#region Bootstrap Configuration
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PSNativeCommandUseErrorActionPreference = $true

$WinPeRoot = 'X:\Foundry'
$LogPath = Join-Path $WinPeRoot 'Logs\FoundryBootstrap.log'
$ConsoleLogLevel = 'Info'
$FileLogLevel = 'Debug'
$Owner = 'foundry-osd'
$Repository = 'foundry'
$ReleaseApiBaseUrl = "https://api.github.com/repos/$Owner/$Repository/releases"
$EmbeddedConnectConfigurationPath = Join-Path $WinPeRoot 'Config\foundry.connect.config.json'
$EmbeddedDeployArchivePath = Join-Path $WinPeRoot 'Seed\Foundry.Deploy.zip'
$EmbeddedDeployConfigurationPath = Join-Path $WinPeRoot 'Config\foundry.deploy.config.json'
$EmbeddedConnectProvisioningSourcePath = Join-Path $WinPeRoot 'Config\foundry.connect.provisioning-source.txt'
$EmbeddedDeployProvisioningSourcePath = Join-Path $WinPeRoot 'Config\foundry.deploy.provisioning-source.txt'
$SevenZipToolsPath = Join-Path $WinPeRoot 'Tools\7zip'
$TimeZoneMapPath = Join-Path $WinPeRoot 'Config\iana-windows-timezones.json'
$DefaultWinPeTimeZoneId = 'UTC'
#endregion

#region General Helpers

function Get-LogLevelRank {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error')]
        [string]$LogLevel
    )

    switch ($LogLevel) {
        'Debug' { return 0 }
        'Info' { return 1 }
        'Warning' { return 2 }
        'Error' { return 3 }
        default { throw "Unsupported log level '$LogLevel'." }
    }
}

function Test-LogLevelEnabled {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error')]
        [string]$MessageLevel,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error')]
        [string]$MinimumLevel
    )

    return (Get-LogLevelRank -LogLevel $MessageLevel) -ge (Get-LogLevelRank -LogLevel $MinimumLevel)
}

function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [ValidateSet('Debug', 'Info', 'Warning', 'Error')]
        [string]$Level = 'Info',
        [string]$ConsoleMessage,
        [switch]$ConsoleSpacingBefore
    )

    $timestamp = [DateTime]::UtcNow.ToString(
        'yyyy-MM-dd HH:mm:ss',
        [System.Globalization.CultureInfo]::InvariantCulture
    )
    $entry = "[$timestamp UTC] [$Level] $Message"

    if (Test-LogLevelEnabled -MessageLevel $Level -MinimumLevel $FileLogLevel) {
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
    }

    if (Test-LogLevelEnabled -MessageLevel $Level -MinimumLevel $ConsoleLogLevel) {
        try {
            if ($ConsoleSpacingBefore) {
                Write-Host ''
            }

            $displayMessage = if ([string]::IsNullOrWhiteSpace($ConsoleMessage)) {
                $Message
            }
            else {
                $ConsoleMessage
            }

            switch ($Level) {
                'Debug' { $displayMessage = "[Debug] $displayMessage" }
                'Warning' { $displayMessage = "Warning: $displayMessage" }
                'Error' { $displayMessage = "Error: $displayMessage" }
            }

            Write-Host $displayMessage
        }
        catch {
            # Keep bootstrap resilient even if console output fails.
        }
    }
}

function Format-FileSize {
    param(
        [Parameter(Mandatory = $true)]
        [Int64]$Bytes
    )

    if ($Bytes -ge 1GB) {
        return ('{0:N1} GB' -f ($Bytes / 1GB))
    }

    if ($Bytes -ge 1MB) {
        return ('{0:N1} MB' -f ($Bytes / 1MB))
    }

    if ($Bytes -ge 1KB) {
        return ('{0:N1} KB' -f ($Bytes / 1KB))
    }

    return "$Bytes bytes"
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

function Resolve-ReleaseAssetName {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    switch ($ApplicationName) {
        'Foundry.Connect' {
            switch ($RuntimeIdentifier) {
                'win-x64' { return 'Foundry.Connect-win-x64.zip' }
                'win-arm64' { return 'Foundry.Connect-win-arm64.zip' }
                Default { throw "Unsupported runtime '$RuntimeIdentifier'." }
            }
        }
        'Foundry.Deploy' {
            switch ($RuntimeIdentifier) {
                'win-x64' { return 'Foundry.Deploy-win-x64.zip' }
                'win-arm64' { return 'Foundry.Deploy-win-arm64.zip' }
                Default { throw "Unsupported runtime '$RuntimeIdentifier'." }
            }
        }
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

            Write-Log "Attempt $attempt failed: $($_.Exception.Message). Retrying in $delay second(s)." -Level Debug
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

function Ensure-ServiceRunning {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceName,
        [string]$FriendlyName = $ServiceName
    )

    try {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    }
    catch {
        $service = $null
    }

    if ($null -eq $service) {
        Write-Log "$FriendlyName service '$ServiceName' is not available in this image." -Level Debug
        return $false
    }

    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Write-Log "$FriendlyName service '$ServiceName' is already running." -Level Debug
        return $true
    }

    try {
        Write-Log "Starting $FriendlyName service '$ServiceName'." -ConsoleMessage "Starting $FriendlyName service..."
        Start-Service -Name $ServiceName -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(10))
        Write-Log "$FriendlyName service '$ServiceName' is running." -ConsoleMessage "$FriendlyName service started."
        return $true
    }
    catch {
        Write-Log "Failed to start $FriendlyName service '$ServiceName': $($_.Exception.Message)." -Level Warning -ConsoleMessage "Could not start $FriendlyName service."
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

function Get-ApplicationBootstrapRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BootstrapRoot,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    return Join-Path $BootstrapRoot $ApplicationName
}

function Get-RuntimeCacheRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BootstrapRoot,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier
    )

    return Join-Path (Get-ApplicationBootstrapRoot -BootstrapRoot $BootstrapRoot -ApplicationName $ApplicationName) $RuntimeIdentifier
}

function Get-StagingRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeCacheRoot
    )

    return "$RuntimeCacheRoot.staging"
}

function Get-TemporaryArchivePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BootstrapRoot,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName,
        [Parameter(Mandatory = $true)]
        [string]$AssetName
    )

    return Join-Path (Get-ApplicationBootstrapRoot -BootstrapRoot $BootstrapRoot -ApplicationName $ApplicationName) "$AssetName.download"
}

function Get-ManifestPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    return Join-Path $RootPath 'manifest'
}

function Get-ExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    switch ($ApplicationName) {
        'Foundry.Connect' { return Join-Path $RootPath 'Foundry.Connect.exe' }
        'Foundry.Deploy' { return Join-Path $RootPath 'Foundry.Deploy.exe' }
    }
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
        Write-Log "Unable to write to destination directory '$DestinationDirectory'." -Level Error -ConsoleMessage 'Unable to write to the destination directory.'
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

        Write-Log "Downloading '$SourceUrl' to '$DestinationFullName' with System.Net.WebClient because $transportReason." -Level Debug
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
            Write-Log "Requesting remote headers for '$SourceUrl'." -Level Debug
            $remote = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $SourceUrl -ErrorAction Stop
            $contentLengthHeader = [string]($remote.Headers.'Content-Length' | Select-Object -First 1)
            $acceptRangesHeader = [string]($remote.Headers.'Accept-Ranges' | Select-Object -First 1)

            if (-not [string]::IsNullOrWhiteSpace($contentLengthHeader)) {
                [Int64]::TryParse($contentLengthHeader, [ref]$remoteLength) | Out-Null
            }

            $remoteAcceptsRanges = [string]::Equals($acceptRangesHeader, 'bytes', [System.StringComparison]::OrdinalIgnoreCase)
            Write-Log "HEAD probe for '$SourceUrl' returned Content-Length='$contentLengthHeader' and Accept-Ranges='$acceptRangesHeader'." -Level Debug
        }
        catch {
            Write-Log "HEAD probe for '$SourceUrl' failed: $($_.Exception.Message). Continuing with a direct curl.exe download." -Level Warning -ConsoleMessage 'Header probe failed. Continuing with a direct download.'
        }

        Write-Log "Downloading '$SourceUrl' to '$DestinationFullName' with curl.exe." -Level Debug
        & curl.exe --fail --location --progress-bar --show-error --output $DestinationFullName --url $SourceUrl
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
            Write-Log "Download is incomplete for '$DestinationFullName'. Retrying with curl.exe resume in $RetryDelaySeconds second(s)." -Level Warning -ConsoleMessage 'Download incomplete. Retrying...'
            Start-Sleep -Seconds $RetryDelaySeconds
            $RetryDelaySeconds *= 2
            $RetryCount += 1
            & curl.exe --fail --location --progress-bar --show-error --continue-at - --output $DestinationFullName --url $SourceUrl
            if ($LASTEXITCODE -ne 0) {
                throw "curl.exe resume failed with exit code $LASTEXITCODE."
            }
        }

        if ($localExists -and ($remoteLength -gt 0) -and ((Get-Item $DestinationFullName).Length -lt $remoteLength)) {
            Write-Log "Download remained incomplete for '$DestinationFullName' after $RetryCount resume attempt(s)." -Level Error -ConsoleMessage 'Download remained incomplete.'
            return $null
        }
    }

    if (Test-Path $DestinationFullName) {
        $downloadedFile = Get-Item $DestinationFullName -Force
        Write-Log "Download completed: '$DestinationFullName' ($($downloadedFile.Length) bytes)." -ConsoleMessage "Download completed ($(Format-FileSize -Bytes $downloadedFile.Length))."
        return $downloadedFile
    }
    else {
        Write-Log "Download failed because '$DestinationFullName' was not created." -Level Error -ConsoleMessage 'Download failed.'
        return $null
    }
}

function Start-WinPeWirelessServiceIfSupported {
    [CmdletBinding()]
    param ()

    if (-not [string]::Equals($env:SystemDrive, 'X:', [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Log 'Skipping WlanSvc startup because the bootstrap is not running from the WinPE system drive.' -Level Debug
        return
    }

    $system32Path = Join-Path $env:SystemRoot 'System32'
    $requiredDependencyPaths = @(
        (Join-Path $system32Path 'dmcmnutils.dll'),
        (Join-Path $system32Path 'mdmregistration.dll')
    )
    $missingDependencyPaths = @($requiredDependencyPaths | Where-Object { -not (Test-Path -Path $_ -PathType Leaf) })
    if ($missingDependencyPaths.Count -gt 0) {
        Write-Log 'Skipping WlanSvc startup because WinRE wireless dependencies are not present in the boot image.' -Level Debug
        return
    }

    [void](Ensure-ServiceRunning -ServiceName 'WlanSvc' -FriendlyName 'Wi-Fi AutoConfig')
}

function Sync-WinPeInternetDateTime {
    [CmdletBinding()]
    param(
        [ValidateRange(1, 1440)]
        [int]$ThresholdMinutes = 5
    )

    if (-not [string]::Equals($env:SystemDrive, 'X:', [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Log 'Skipping clock synchronization because the bootstrap is not running from the WinPE system drive.' -Level Debug
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
            Write-Log "Requesting internet time from '$probeUrl'." -Level Debug
            $response = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $probeUrl -ErrorAction Stop
            $dateHeader = [string]($response.Headers['Date'] | Select-Object -First 1)

            if (-not [string]::IsNullOrWhiteSpace($dateHeader)) {
                $internetDateTime = Get-Date $dateHeader
                break
            }

            Write-Log "The time probe '$probeUrl' did not return an HTTP Date header." -Level Debug
        }
        catch {
            Write-Log "The time probe '$probeUrl' failed: $($_.Exception.Message)." -Level Debug
        }
    }

    if ($null -eq $internetDateTime) {
        Write-Log 'Could not resolve internet time. Continuing without clock synchronization.' -Level Warning -ConsoleMessage 'Clock sync unavailable. Continuing.'
        return
    }

    $localDateTime = Get-Date
    $differenceMinutes = [Math]::Abs(($internetDateTime - $localDateTime).TotalMinutes)
    $roundedDifferenceMinutes = [Math]::Round($differenceMinutes)

    if ($differenceMinutes -le $ThresholdMinutes) {
        Write-Log "System clock is already within $ThresholdMinutes minute(s) of internet time." -ConsoleMessage 'Clock: already within threshold.'
        return
    }

    Write-Log "System clock differs from internet time by $roundedDifferenceMinutes minute(s). Updating the WinPE clock." -ConsoleMessage "Clock drift detected ($roundedDifferenceMinutes minute(s)). Updating..."

    try {
        Set-Date -Date $internetDateTime -ErrorAction Stop | Out-Null
        Write-Log "System clock updated to '$($internetDateTime.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture))'." -ConsoleMessage 'Clock: updated from internet time.'
    }
    catch {
        Write-Log "Failed to update the WinPE clock: $($_.Exception.Message)." -Level Warning -ConsoleMessage 'Clock update failed. Continuing.'
    }
}

function Get-WinPeConfiguredTimeZone {
    [CmdletBinding()]
    param ()

    $environmentTimeZoneId = [string]$env:FOUNDRY_WINPE_TIMEZONE_ID
    $environmentTimeZoneId = $environmentTimeZoneId.Trim()

    if (-not [string]::IsNullOrWhiteSpace($environmentTimeZoneId)) {
        return [PSCustomObject]@{
            Id     = $environmentTimeZoneId
            Source = 'environment variable FOUNDRY_WINPE_TIMEZONE_ID'
        }
    }

    if (-not (Test-Path -Path $EmbeddedDeployConfigurationPath -PathType Leaf)) {
        return $null
    }

    try {
        $configurationJson = Get-Content -Path $EmbeddedDeployConfigurationPath -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($configurationJson)) {
            return $null
        }

        $configuration = $configurationJson | ConvertFrom-Json -ErrorAction Stop
        $configuredTimeZoneId = [string]$configuration.localization.defaultTimeZoneId
        $configuredTimeZoneId = $configuredTimeZoneId.Trim()

        if (-not [string]::IsNullOrWhiteSpace($configuredTimeZoneId)) {
            return [PSCustomObject]@{
                Id     = $configuredTimeZoneId
                Source = "embedded deploy configuration '$EmbeddedDeployConfigurationPath'"
            }
        }
    }
    catch {
        Write-Log "Failed to read the embedded deploy configuration for timezone detection: $($_.Exception.Message)." -Level Warning -ConsoleMessage 'Timezone config unavailable. Using fallback.'
    }

    return $null
}

function Test-WindowsTimeZoneId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TimeZoneId
    )

    if ([string]::IsNullOrWhiteSpace($TimeZoneId)) {
        return $false
    }

    try {
        [System.TimeZoneInfo]::FindSystemTimeZoneById($TimeZoneId.Trim()) | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Get-IanaWindowsTimeZoneMap {
    [CmdletBinding()]
    param ()

    if ($script:IanaWindowsTimeZoneMap) {
        return $script:IanaWindowsTimeZoneMap
    }

    if (-not (Test-Path -Path $TimeZoneMapPath -PathType Leaf)) {
        Write-Log "IANA to Windows timezone map was not found at '$TimeZoneMapPath'." -Level Warning -ConsoleMessage 'Timezone map unavailable. Auto-detect skipped.'
        return $null
    }

    try {
        $mapJson = Get-Content -Path $TimeZoneMapPath -Raw -ErrorAction Stop
        $parsedMap = ConvertFrom-Json -InputObject $mapJson -ErrorAction Stop
        $map = @{}

        foreach ($property in $parsedMap.PSObject.Properties) {
            $map[[string]$property.Name] = [string]$property.Value
        }

        $script:IanaWindowsTimeZoneMap = $map
        return $script:IanaWindowsTimeZoneMap
    }
    catch {
        Write-Log "Failed to load IANA to Windows timezone map from '$TimeZoneMapPath': $($_.Exception.Message)." -Level Warning -ConsoleMessage 'Timezone map load failed. Auto-detect skipped.'
        return $null
    }
}

function Convert-IanaTimeZoneIdToWindowsId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$IanaTimeZoneId
    )

    $normalizedTimeZoneId = $IanaTimeZoneId.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedTimeZoneId)) {
        return $null
    }

    $map = Get-IanaWindowsTimeZoneMap
    if ($null -eq $map) {
        return $null
    }

    if ($map.ContainsKey($normalizedTimeZoneId)) {
        return [string]$map[$normalizedTimeZoneId]
    }

    return $null
}

function Resolve-WindowsTimeZoneId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TimeZoneCandidate
    )

    $normalizedCandidate = $TimeZoneCandidate.Trim()
    if ([string]::IsNullOrWhiteSpace($normalizedCandidate)) {
        return $null
    }

    if (Test-WindowsTimeZoneId -TimeZoneId $normalizedCandidate) {
        return $normalizedCandidate
    }

    $convertedTimeZoneId = Convert-IanaTimeZoneIdToWindowsId -IanaTimeZoneId $normalizedCandidate
    if (Test-WindowsTimeZoneId -TimeZoneId $convertedTimeZoneId) {
        return $convertedTimeZoneId
    }

    return $null
}

function Get-PublicIpTimeZoneId {
    [CmdletBinding()]
    param ()

    $providers = @(
        @{
            Name = 'time.now'
            Uri = 'https://time.now/developer/api/ip'
            ResponseType = 'Json'
        },
        @{
            Name = 'ipapi.co'
            Uri = 'https://ipapi.co/timezone/'
            ResponseType = 'Text'
        },
        @{
            Name = 'geojs'
            Uri = 'https://get.geojs.io/v1/ip/geo.json'
            ResponseType = 'Json'
        }
    )

    foreach ($provider in $providers) {
        try {
            Write-Log "Resolving public IP timezone from '$($provider.Name)'." -Level Debug

            $candidate = switch ($provider.ResponseType) {
                'Text' {
                    [string](Invoke-WebRequest -UseBasicParsing -Method Get -Uri $provider.Uri -TimeoutSec 10 -ErrorAction Stop).Content
                }
                'Json' {
                    [string](Invoke-RestMethod -Method Get -Uri $provider.Uri -TimeoutSec 10 -ErrorAction Stop).timezone
                }
                default {
                    $null
                }
            }

            $candidate = $candidate.Trim()
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                Write-Log "Public IP timezone provider '$($provider.Name)' returned '$candidate'." -Level Debug
                return $candidate
            }
        }
        catch {
            Write-Log "Public IP timezone lookup against '$($provider.Name)' failed: $($_.Exception.Message)." -Level Debug
        }
    }

    return $null
}

function Get-WinPeAutomaticTimeZone {
    [CmdletBinding()]
    param ()

    $publicIpTimeZoneId = Get-PublicIpTimeZoneId
    if ([string]::IsNullOrWhiteSpace($publicIpTimeZoneId)) {
        return $null
    }

    $resolvedTimeZoneId = Resolve-WindowsTimeZoneId -TimeZoneCandidate $publicIpTimeZoneId
    if (-not [string]::IsNullOrWhiteSpace($resolvedTimeZoneId)) {
        return [PSCustomObject]@{
            Id     = $resolvedTimeZoneId
            Source = "public IP lookup '$publicIpTimeZoneId'"
        }
    }

    Write-Log "Public IP timezone '$publicIpTimeZoneId' could not be converted to a Windows timezone ID." -Level Warning -ConsoleMessage 'Public IP timezone could not be mapped. Using fallback.'
    return $null
}

function Get-CurrentWinPeTimeZoneId {
    [CmdletBinding()]
    param ()

    try {
        if (Get-Command 'Get-TimeZone' -ErrorAction SilentlyContinue) {
            return ([string](Get-TimeZone).Id).Trim()
        }

        return $null
    }
    catch {
        Write-Log "Failed to query the current WinPE timezone: $($_.Exception.Message)." -Level Debug
        return $null
    }
}

function Set-WinPeTimeZone {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FallbackTimeZoneId
    )

    if (-not [string]::Equals($env:SystemDrive, 'X:', [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Log 'Skipping timezone configuration because the bootstrap is not running from the WinPE system drive.' -Level Debug
        return
    }

    $configuredTimeZone = Get-WinPeConfiguredTimeZone
    if ($null -ne $configuredTimeZone) {
        $configuredTimeZoneId = Resolve-WindowsTimeZoneId -TimeZoneCandidate $configuredTimeZone.Id
        if (-not [string]::IsNullOrWhiteSpace($configuredTimeZoneId)) {
            $targetTimeZoneId = $configuredTimeZoneId
            $targetTimeZoneSource = $configuredTimeZone.Source
        }
        else {
            Write-Log "Configured timezone '$($configuredTimeZone.Id)' is invalid. Trying automatic detection." -Level Warning -ConsoleMessage 'Configured timezone invalid. Trying auto-detect.'
        }
    }

    if ([string]::IsNullOrWhiteSpace($targetTimeZoneId)) {
        $automaticTimeZone = Get-WinPeAutomaticTimeZone
        if ($null -ne $automaticTimeZone) {
            $targetTimeZoneId = $automaticTimeZone.Id
            $targetTimeZoneSource = $automaticTimeZone.Source
        }
    }

    if ([string]::IsNullOrWhiteSpace($targetTimeZoneId)) {
        $targetTimeZoneId = $FallbackTimeZoneId
        $targetTimeZoneSource = 'bootstrap fallback'
    }

    if ([string]::IsNullOrWhiteSpace($targetTimeZoneId)) {
        Write-Log 'Skipping timezone configuration because no target timezone was resolved.' -Level Debug
        return
    }

    $currentTimeZoneId = Get-CurrentWinPeTimeZoneId
    if ([string]::Equals($currentTimeZoneId, $targetTimeZoneId, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Log "WinPE timezone is already '$targetTimeZoneId'." -ConsoleMessage "Timezone: already '$targetTimeZoneId'."
        return
    }

    Write-Log "Applying WinPE timezone '$targetTimeZoneId' from $targetTimeZoneSource." -ConsoleMessage "Timezone: applying '$targetTimeZoneId'..."

    try {
        if (Get-Command 'Set-TimeZone' -ErrorAction SilentlyContinue) {
            Set-TimeZone -Id $targetTimeZoneId -ErrorAction Stop
        }
        else {
            throw 'Set-TimeZone is not available in WinPE.'
        }

        $resolvedTimeZoneId = Get-CurrentWinPeTimeZoneId
        if ([string]::IsNullOrWhiteSpace($resolvedTimeZoneId)) {
            $resolvedTimeZoneId = $targetTimeZoneId
        }

        Write-Log "WinPE timezone set to '$resolvedTimeZoneId'." -ConsoleMessage "Timezone: set to '$resolvedTimeZoneId'."
    }
    catch {
        Write-Log "Failed to set the WinPE timezone to '$targetTimeZoneId': $($_.Exception.Message)." -Level Warning -ConsoleMessage 'Timezone update failed. Continuing.'
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

function Copy-LocalArchiveToTemporaryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    Copy-LocalArchive -SourcePath $SourcePath -DestinationPath $DestinationPath
    return $DestinationPath
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
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName,
        [Parameter(Mandatory = $true)]
        [string]$AssetName
    )

    $asset = $Release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    if ($null -eq $asset) {
        throw "No $ApplicationName asset named '$AssetName' was found in release '$($Release.tag_name)'."
    }

    return $asset
}

function Resolve-CachedExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeCacheRoot,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    $executablePath = Get-ExecutablePath -RootPath $RuntimeCacheRoot -ApplicationName $ApplicationName
    if (-not (Test-Path -Path $executablePath -PathType Leaf)) {
        throw "No cached $ApplicationName executable is available in '$RuntimeCacheRoot'."
    }

    return Get-Item -Path $executablePath
}

function Test-CacheCurrent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RuntimeCacheRoot,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName,
        [Parameter(Mandatory = $true)]
        [string]$AssetName,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseTag,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseVersion,
        [Parameter(Mandatory = $true)]
        $Asset
    )

    $executablePath = Get-ExecutablePath -RootPath $RuntimeCacheRoot -ApplicationName $ApplicationName
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
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)]
        [string]$AssetName,
        [string]$Tag,
        [string]$Version,
        [string]$ArchiveSha256
    )

    $stagingRoot = Get-StagingRoot -RuntimeCacheRoot $RuntimeCacheRoot
    Remove-DirectoryIfPresent -Path $stagingRoot

    try {
        # Extract into a staging directory first so the active cache remains valid until promotion succeeds.
        Ensure-Directory -Path $stagingRoot
        Expand-ZipVia7Zip -ArchivePath $ArchivePath -DestinationPath $stagingRoot -RuntimeIdentifier $RuntimeIdentifier

        $stagedExecutablePath = Get-ExecutablePath -RootPath $stagingRoot -ApplicationName $ApplicationName
        if (-not (Test-Path -Path $stagedExecutablePath -PathType Leaf)) {
            throw "The extracted $ApplicationName cache does not contain '$([System.IO.Path]::GetFileName($stagedExecutablePath))'."
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
        return Resolve-CachedExecutable -RuntimeCacheRoot $RuntimeCacheRoot -ApplicationName $ApplicationName
    }
    finally {
        Remove-FileIfPresent -Path $ArchivePath
        Remove-DirectoryIfPresent -Path $stagingRoot
    }
}
#endregion

#region Release Resolution Helpers

function Get-EmbeddedArchivePath {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    switch ($ApplicationName) {
        'Foundry.Connect' { return '' }
        'Foundry.Deploy' { return $EmbeddedDeployArchivePath }
    }
}

function Get-EmbeddedProvisioningSourcePath {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    switch ($ApplicationName) {
        'Foundry.Connect' { return $EmbeddedConnectProvisioningSourcePath }
        'Foundry.Deploy' { return $EmbeddedDeployProvisioningSourcePath }
    }
}

function Get-EmbeddedProvisioningSource {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    $sourcePath = Get-EmbeddedProvisioningSourcePath -ApplicationName $ApplicationName
    if (-not (Test-Path -Path $sourcePath -PathType Leaf)) {
        return ''
    }

    try {
        return (Get-Content -Path $sourcePath -Raw -ErrorAction Stop).Trim().ToLowerInvariant()
    }
    catch {
        Write-Log "Failed to read embedded provisioning source for $ApplicationName from '$sourcePath': $($_.Exception.Message)." -Level Warning
        return ''
    }
}

function Get-ReleaseTagOverride {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    $specificTag = switch ($ApplicationName) {
        'Foundry.Connect' { [string]$env:FOUNDRY_CONNECT_RELEASE_TAG }
        'Foundry.Deploy' { [string]$env:FOUNDRY_DEPLOY_RELEASE_TAG }
    }

    $specificTag = $specificTag.Trim()
    if (-not [string]::IsNullOrWhiteSpace($specificTag)) {
        return $specificTag
    }

    $globalTag = [string]$env:FOUNDRY_RELEASE_TAG
    return $globalTag.Trim()
}

function Get-ArchiveOverridePath {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    $value = switch ($ApplicationName) {
        'Foundry.Connect' { [string]$env:FOUNDRY_CONNECT_ARCHIVE }
        'Foundry.Deploy' { [string]$env:FOUNDRY_DEPLOY_ARCHIVE }
    }

    return $value.Trim()
}

function Get-ArchiveOverrideSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName
    )

    $value = switch ($ApplicationName) {
        'Foundry.Connect' { [string]$env:FOUNDRY_CONNECT_ARCHIVE_SHA256 }
        'Foundry.Deploy' { [string]$env:FOUNDRY_DEPLOY_ARCHIVE_SHA256 }
    }

    return $value.Trim()
}

function Resolve-ApplicationExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Foundry.Connect', 'Foundry.Deploy')]
        [string]$ApplicationName,
        [Parameter(Mandatory = $true)]
        [string]$BootstrapRoot,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)]
        [hashtable]$Headers,
        [switch]$SkipReleaseLookup
    )

    $applicationBootstrapRoot = Get-ApplicationBootstrapRoot -BootstrapRoot $BootstrapRoot -ApplicationName $ApplicationName
    Ensure-Directory -Path $applicationBootstrapRoot

    $assetName = Resolve-ReleaseAssetName -ApplicationName $ApplicationName -RuntimeIdentifier $RuntimeIdentifier
    $runtimeCacheRoot = Get-RuntimeCacheRoot -BootstrapRoot $BootstrapRoot -ApplicationName $ApplicationName -RuntimeIdentifier $RuntimeIdentifier
    $downloadPath = Get-TemporaryArchivePath -BootstrapRoot $BootstrapRoot -ApplicationName $ApplicationName -AssetName $assetName
    $releaseTagOverride = Get-ReleaseTagOverride -ApplicationName $ApplicationName
    $archiveOverride = Get-ArchiveOverridePath -ApplicationName $ApplicationName
    $archiveOverrideSha256 = Get-ArchiveOverrideSha256 -ApplicationName $ApplicationName
    $embeddedArchivePath = Get-EmbeddedArchivePath -ApplicationName $ApplicationName
    $hasEmbeddedArchiveFallback = -not [string]::IsNullOrWhiteSpace($embeddedArchivePath) -and (Test-Path -Path $embeddedArchivePath -PathType Leaf)
    $releaseLookupFallbackDescription = if ($hasEmbeddedArchiveFallback) { 'the existing cache or embedded archive' } else { 'the existing cache' }
    $executable = $null

    if ($SkipReleaseLookup -and [string]::IsNullOrWhiteSpace($archiveOverride) -and $hasEmbeddedArchiveFallback) {
        $archiveOverride = $embeddedArchivePath
        Write-Log "Using embedded $ApplicationName archive from '$embeddedArchivePath'." -ConsoleMessage "Using embedded $ApplicationName archive."
    }

    if (-not [string]::IsNullOrWhiteSpace($archiveOverride)) {
        if (-not [string]::IsNullOrWhiteSpace($releaseTagOverride)) {
            Write-Log "$ApplicationName archive override is set; ignoring release tag override '$releaseTagOverride'." -Level Warning -ConsoleMessage "$ApplicationName archive override is set; release tag override ignored."
        }

        if (Test-HttpUrl -Value $archiveOverride) {
            Write-Log "Starting $ApplicationName override archive download from '$archiveOverride'." -ConsoleMessage "Downloading $ApplicationName override archive..." -ConsoleSpacingBefore
            Save-WebFile `
                -SourceUrl $archiveOverride `
                -DestinationPath $downloadPath | Out-Null
        }
        else {
            Write-Log "Copying $ApplicationName override archive from '$archiveOverride'." -ConsoleMessage "Using local $ApplicationName override archive." -ConsoleSpacingBefore
            Copy-LocalArchive -SourcePath $archiveOverride -DestinationPath $downloadPath
        }

        $archiveSha256 = Get-FileSha256 -Path $downloadPath
        Assert-ExpectedSha256 -ActualSha256 $archiveSha256 -ExpectedSha256 $archiveOverrideSha256 -Context "$ApplicationName override archive"

        Write-Log "Refreshing $ApplicationName cache for runtime '$RuntimeIdentifier' from override archive." -ConsoleMessage "Refreshing $ApplicationName cache..." -ConsoleSpacingBefore
        $executable = Update-CacheFromArchiveFile `
            -ArchivePath $downloadPath `
            -BootstrapRoot $BootstrapRoot `
            -RuntimeCacheRoot $runtimeCacheRoot `
            -ApplicationName $ApplicationName `
            -RuntimeIdentifier $RuntimeIdentifier `
            -AssetName $assetName `
            -Tag '' `
            -Version '' `
            -ArchiveSha256 $archiveSha256
    }
    elseif ($SkipReleaseLookup) {
        $executable = Resolve-CachedExecutable -RuntimeCacheRoot $runtimeCacheRoot -ApplicationName $ApplicationName
    }
    else {
        $releaseApiUrl = Resolve-ReleaseApiUrl -ReleaseTagOverride $releaseTagOverride
        $release = $null

        try {
            Write-Log "Resolving $ApplicationName release metadata from $releaseApiUrl." -Level Debug
            $release = Invoke-WithRetry -Action { Invoke-RestMethod -Uri $releaseApiUrl -Headers $Headers -Method Get }
        }
        catch {
            Write-Log "Failed to resolve $ApplicationName release metadata: $($_.Exception.Message). Falling back to $releaseLookupFallbackDescription." -Level Warning -ConsoleMessage "$ApplicationName release lookup failed. Falling back."
            try {
                $executable = Resolve-CachedExecutable -RuntimeCacheRoot $runtimeCacheRoot -ApplicationName $ApplicationName
            }
            catch {
                if ($hasEmbeddedArchiveFallback) {
                    Write-Log "Falling back to the embedded $ApplicationName archive because no cached executable was available." -Level Warning -ConsoleMessage "Using embedded $ApplicationName archive as fallback."
                    $archiveSha256 = Get-FileSha256 -Path $embeddedArchivePath
                    $executable = Update-CacheFromArchiveFile `
                        -ArchivePath (Copy-LocalArchiveToTemporaryPath -SourcePath $embeddedArchivePath -DestinationPath $downloadPath) `
                        -BootstrapRoot $BootstrapRoot `
                        -RuntimeCacheRoot $runtimeCacheRoot `
                        -ApplicationName $ApplicationName `
                        -RuntimeIdentifier $RuntimeIdentifier `
                        -AssetName $assetName `
                        -Tag '' `
                        -Version '' `
                        -ArchiveSha256 $archiveSha256
                }
                else {
                    throw
                }
            }
        }

        if ($null -eq $executable) {
            $asset = Get-ReleaseAsset -Release $release -ApplicationName $ApplicationName -AssetName $assetName
            $releaseTag = [string]$release.tag_name
            $releaseVersion = Get-ReleaseVersion -Tag $releaseTag

            Write-Log "Using $ApplicationName release tag '$releaseTag' and asset '$($asset.name)'." -ConsoleMessage "$ApplicationName release: $releaseTag" -ConsoleSpacingBefore
            Write-Log "Selected $ApplicationName asset '$($asset.name)' for runtime '$RuntimeIdentifier'." -ConsoleMessage "$ApplicationName asset: $($asset.name)"

            if (Test-CacheCurrent `
                    -RuntimeCacheRoot $runtimeCacheRoot `
                    -ApplicationName $ApplicationName `
                    -AssetName $assetName `
                    -ReleaseTag $releaseTag `
                    -ReleaseVersion $releaseVersion `
                    -Asset $asset) {
                Write-Log "The cached $ApplicationName content for '$RuntimeIdentifier' is already current." -ConsoleMessage "$ApplicationName cache is current."
                $executable = Resolve-CachedExecutable -RuntimeCacheRoot $runtimeCacheRoot -ApplicationName $ApplicationName
            }
            else {
                try {
                    Write-Log "Starting $ApplicationName release asset download from '$($asset.browser_download_url)'." -ConsoleMessage "Downloading $($asset.name)..." -ConsoleSpacingBefore
                    Save-WebFile `
                        -SourceUrl $asset.browser_download_url `
                        -DestinationPath $downloadPath | Out-Null

                    $archiveSha256 = Get-FileSha256 -Path $downloadPath
                    $expectedSha256 = Get-ReleaseAssetSha256 -Asset $asset
                    if (-not [string]::IsNullOrWhiteSpace($expectedSha256)) {
                        Assert-ExpectedSha256 -ActualSha256 $archiveSha256 -ExpectedSha256 $expectedSha256 -Context "$ApplicationName release asset '$($asset.name)'"
                    }
                    elseif (-not [string]::IsNullOrWhiteSpace([string]$asset.digest)) {
                        Write-Log "$ApplicationName release asset digest '$($asset.digest)' is not a supported SHA256 value. Continuing without digest validation." -Level Warning -ConsoleMessage "$ApplicationName digest format is unsupported. Continuing without digest validation."
                    }
                    else {
                        Write-Log "No $ApplicationName release asset digest was provided. Continuing without digest validation." -Level Warning -ConsoleMessage "No $ApplicationName release digest was provided. Continuing without digest validation."
                    }

                    Write-Log "Refreshing $ApplicationName cache for runtime '$RuntimeIdentifier'." -ConsoleMessage "Refreshing $ApplicationName cache..." -ConsoleSpacingBefore
                    $executable = Update-CacheFromArchiveFile `
                        -ArchivePath $downloadPath `
                        -BootstrapRoot $BootstrapRoot `
                        -RuntimeCacheRoot $runtimeCacheRoot `
                        -ApplicationName $ApplicationName `
                        -RuntimeIdentifier $RuntimeIdentifier `
                        -AssetName $assetName `
                        -Tag $releaseTag `
                        -Version $releaseVersion `
                        -ArchiveSha256 $archiveSha256
                }
                catch {
                    Write-Log "Failed to refresh the $ApplicationName cache: $($_.Exception.Message). Falling back to $releaseLookupFallbackDescription." -Level Warning -ConsoleMessage "$ApplicationName cache refresh failed. Falling back."
                    try {
                        $executable = Resolve-CachedExecutable -RuntimeCacheRoot $runtimeCacheRoot -ApplicationName $ApplicationName
                    }
                    catch {
                        if ($hasEmbeddedArchiveFallback) {
                            Write-Log "Falling back to the embedded $ApplicationName archive because the cache could not be refreshed." -Level Warning -ConsoleMessage "Using embedded $ApplicationName archive as fallback."
                            $archiveSha256 = Get-FileSha256 -Path $embeddedArchivePath
                            $executable = Update-CacheFromArchiveFile `
                                -ArchivePath (Copy-LocalArchiveToTemporaryPath -SourcePath $embeddedArchivePath -DestinationPath $downloadPath) `
                                -BootstrapRoot $BootstrapRoot `
                                -RuntimeCacheRoot $runtimeCacheRoot `
                                -ApplicationName $ApplicationName `
                                -RuntimeIdentifier $RuntimeIdentifier `
                                -AssetName $assetName `
                                -Tag '' `
                                -Version '' `
                                -ArchiveSha256 $archiveSha256
                        }
                        else {
                            throw
                        }
                    }
                }
            }
        }
    }

    return Resolve-SingleExecutable -Candidate $executable
}

function Start-DeployExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Executable
    )

    Write-Log "Launching '$($Executable.FullName)'." -ConsoleMessage 'Launching Foundry.Deploy.exe...' -ConsoleSpacingBefore
    Start-Process -FilePath $Executable.FullName -WorkingDirectory $Executable.DirectoryName | Out-Null
}

function Invoke-ConnectExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Executable,
        [string]$ConfigurationPath
    )

    $argumentList = @()
    if (-not [string]::IsNullOrWhiteSpace($ConfigurationPath) -and (Test-Path -Path $ConfigurationPath -PathType Leaf)) {
        $argumentList = @('--config', $ConfigurationPath)
        Write-Log "Launching '$($Executable.FullName)' with configuration '$ConfigurationPath'." -ConsoleMessage 'Launching Foundry.Connect...' -ConsoleSpacingBefore
    }
    else {
        Write-Log "Launching '$($Executable.FullName)' without an external configuration file." -ConsoleMessage 'Launching Foundry.Connect...' -ConsoleSpacingBefore
    }

    $process = Start-Process `
        -FilePath $Executable.FullName `
        -WorkingDirectory $Executable.DirectoryName `
        -ArgumentList $argumentList `
        -Wait `
        -PassThru

    return $process.ExitCode
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
    throw "Could not resolve an executable from value of type '$typeName'."
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
    Write-Log "Bootstrap root resolved to '$bootstrapRoot'." -ConsoleMessage "Runtime root: $bootstrapRoot" -ConsoleSpacingBefore
    Write-Log "Deployment mode resolved to '$deploymentMode'." -ConsoleMessage "Mode: $deploymentMode"

    $env:FOUNDRY_DEPLOYMENT_MODE = $deploymentMode
    $runtimeIdentifier = Get-TargetRuntimeIdentifier
    $headers = @{
        'User-Agent' = 'FoundryBootstrap/1.0'
        'Accept'     = 'application/vnd.github+json'
    }

    [void](Ensure-ServiceRunning -ServiceName 'dot3svc' -FriendlyName 'Wired AutoConfig')
    Start-WinPeWirelessServiceIfSupported

    $connectProvisioningSource = Get-EmbeddedProvisioningSource -ApplicationName 'Foundry.Connect'
    $deployProvisioningSource = Get-EmbeddedProvisioningSource -ApplicationName 'Foundry.Deploy'
    $skipConnectReleaseLookup = $connectProvisioningSource -eq 'debug'
    $skipDeployReleaseLookup = $deployProvisioningSource -eq 'debug'

    $connectExecutable = $null
    if ($skipConnectReleaseLookup) {
        $connectExecutable = Resolve-ApplicationExecutable `
            -ApplicationName 'Foundry.Connect' `
            -BootstrapRoot $bootstrapRoot `
            -RuntimeIdentifier $runtimeIdentifier `
            -Headers $headers `
            -SkipReleaseLookup
    }
    else {
        try {
            $connectExecutable = Resolve-ApplicationExecutable `
                -ApplicationName 'Foundry.Connect' `
                -BootstrapRoot $bootstrapRoot `
                -RuntimeIdentifier $runtimeIdentifier `
                -Headers $headers `
                -SkipReleaseLookup
        }
        catch {
            Write-Log "No cached Foundry.Connect runtime was available before launch: $($_.Exception.Message). Resolving release content now." -Level Warning -ConsoleMessage 'Foundry.Connect cache missing. Resolving release content.'
            $connectExecutable = Resolve-ApplicationExecutable `
                -ApplicationName 'Foundry.Connect' `
                -BootstrapRoot $bootstrapRoot `
                -RuntimeIdentifier $runtimeIdentifier `
                -Headers $headers
        }
    }

    $connectExitCode = Invoke-ConnectExecutable `
        -Executable $connectExecutable `
        -ConfigurationPath $EmbeddedConnectConfigurationPath

    if ($connectExitCode -ne 0) {
        switch ($connectExitCode) {
            20 {
                throw "Foundry.Connect was closed by the operator. Bootstrap will not continue."
            }
            default {
                throw "Foundry.Connect exited with code $connectExitCode."
            }
        }
    }

    Write-Log 'Foundry.Connect completed successfully.' -ConsoleMessage 'Foundry.Connect completed successfully.' -ConsoleSpacingBefore
    Sync-WinPeInternetDateTime -ThresholdMinutes 5
    Set-WinPeTimeZone -FallbackTimeZoneId $DefaultWinPeTimeZoneId

    if ($skipConnectReleaseLookup) {
        Write-Log 'Skipping Foundry.Connect cache verification because the provisioned runtime is debug.' -ConsoleMessage 'Foundry.Connect debug runtime detected. Skipping update check.'
    }
    elseif ($deploymentMode -ne 'Usb') {
        Write-Log 'Skipping Foundry.Connect cache verification because the deployment mode is ISO.' -ConsoleMessage 'Foundry.Connect update check skipped in ISO mode.'
    }
    else {
        try {
            [void](Resolve-ApplicationExecutable `
                -ApplicationName 'Foundry.Connect' `
                -BootstrapRoot $bootstrapRoot `
                -RuntimeIdentifier $runtimeIdentifier `
                -Headers $headers)
            Write-Log 'Foundry.Connect cache verification completed.' -ConsoleMessage 'Foundry.Connect cache verification completed.'
        }
        catch {
            Write-Log "Foundry.Connect cache verification failed: $($_.Exception.Message). Continuing with the cached bootstrap content." -Level Warning -ConsoleMessage 'Foundry.Connect cache verification failed. Continuing.'
        }
    }

    $deployExecutable = Resolve-ApplicationExecutable `
        -ApplicationName 'Foundry.Deploy' `
        -BootstrapRoot $bootstrapRoot `
        -RuntimeIdentifier $runtimeIdentifier `
        -Headers $headers `
        -SkipReleaseLookup:$skipDeployReleaseLookup

    Start-DeployExecutable -Executable $deployExecutable
    Write-Log 'Foundry bootstrap completed successfully.' -ConsoleMessage 'Foundry bootstrap completed successfully.' -ConsoleSpacingBefore
}
catch {
    Write-Log "Foundry bootstrap failed: $($_.Exception.Message)" -Level Error -ConsoleMessage 'Foundry bootstrap failed.'
    exit 1
}
#endregion
