Clear-Host

#region Bootstrap Configuration
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PSNativeCommandUseErrorActionPreference = $true

$WinPeRoot = 'X:\Foundry'
$LogPath = Join-Path $WinPeRoot 'Logs\FoundryBootstrap.log'
$ConsoleLogLevel = 'Info'
$FileLogLevel = 'Debug'
$MaximumLogFileSizeBytes = 10MB
$RetainedLogFileCount = 5
$DiagnosticSessionId = [string]$env:FOUNDRY_DIAGNOSTIC_SESSION_ID
if ([string]::IsNullOrWhiteSpace($DiagnosticSessionId) -or $DiagnosticSessionId -notmatch '^[A-Za-z0-9_-]{1,32}$') {
    $DiagnosticSessionId = [Guid]::NewGuid().ToString('N').Substring(0, 8).ToUpperInvariant()
}
$env:FOUNDRY_DIAGNOSTIC_SESSION_ID = $DiagnosticSessionId
$Owner = 'foundry-osd'
$Repository = 'foundry'
$ReleaseApiBaseUrl = "https://api.github.com/repos/$Owner/$Repository/releases"
$EmbeddedConnectConfigurationPath = Join-Path $WinPeRoot 'Config\foundry.connect.config.json'

$EmbeddedDeployConfigurationPath = Join-Path $WinPeRoot 'Config\foundry.deploy.config.json'

$TimeZoneMapPath = Join-Path $WinPeRoot 'Config\iana-windows-timezones.json'
$DefaultWinPeTimeZoneId = 'UTC'

$script:ConsoleLineWritten = $false
$script:ConsoleLastWasBlank = $true
#endregion

#region General Helpers

function Get-LogLevelRank {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Fatal')]
        [string]$LogLevel
    )

    switch ($LogLevel) {
        'Debug' { return 0 }
        'Info' { return 1 }
        'Warning' { return 2 }
        'Error' { return 3 }
        'Fatal' { return 4 }
        default { throw "Unsupported log level '$LogLevel'." }
    }
}

function Test-LogLevelEnabled {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Fatal')]
        [string]$MessageLevel,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Fatal')]
        [string]$MinimumLevel
    )

    return (Get-LogLevelRank -LogLevel $MessageLevel) -ge (Get-LogLevelRank -LogLevel $MinimumLevel)
}

function Get-ConsoleLevelLabel {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Fatal')]
        [string]$Level
    )

    switch ($Level) {
        'Debug' { return 'DEBUG' }
        'Info' { return 'INFO' }
        'Warning' { return 'WARN' }
        'Error' { return 'ERROR' }
        'Fatal' { return 'FATAL' }
        default { return $Level.ToUpperInvariant() }
    }
}

function Get-ConsoleLevelColor {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Fatal')]
        [string]$Level
    )

    switch ($Level) {
        'Debug' { return 'DarkGray' }
        'Info' { return 'Gray' }
        'Warning' { return 'Yellow' }
        'Error' { return 'Red' }
        'Fatal' { return 'Red' }
        default { return 'Gray' }
    }
}

function Write-ConsoleBlankLine {
    param()

    try {
        if ($script:ConsoleLineWritten -and -not $script:ConsoleLastWasBlank) {
            Write-Host ''
            $script:ConsoleLastWasBlank = $true
        }
    }
    catch {
        # Keep bootstrap resilient even if console output fails.
    }
}

function Write-ConsoleLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Fatal')]
        [string]$Level = 'Info'
    )

    try {
        $label = Get-ConsoleLevelLabel -Level $Level
        $color = Get-ConsoleLevelColor -Level $Level
        $line = '[{0,-5}] {1}' -f $label, $Message

        Write-Host $line -ForegroundColor $color
        $script:ConsoleLineWritten = $true
        $script:ConsoleLastWasBlank = $false
    }
    catch {
        # Keep bootstrap resilient even if console output fails.
    }
}

function Write-ConsoleSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title
    )

    if (-not (Test-LogLevelEnabled -MessageLevel 'Info' -MinimumLevel $ConsoleLogLevel)) {
        return
    }

    try {
        Write-ConsoleBlankLine
        Write-Host $Title -ForegroundColor Cyan
        Write-Host ('-' * [Math]::Max(3, $Title.Length)) -ForegroundColor DarkCyan
        $script:ConsoleLineWritten = $true
        $script:ConsoleLastWasBlank = $false
    }
    catch {
        # Keep bootstrap resilient even if console output fails.
    }
}

function Write-ConsoleBanner {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title
    )

    if (-not (Test-LogLevelEnabled -MessageLevel 'Info' -MinimumLevel $ConsoleLogLevel)) {
        return
    }

    try {
        Write-Host $Title -ForegroundColor Cyan
        Write-Host ('=' * [Math]::Max(3, $Title.Length)) -ForegroundColor DarkCyan
        $script:ConsoleLineWritten = $true
        $script:ConsoleLastWasBlank = $false
    }
    catch {
        # Keep bootstrap resilient even if console output fails.
    }
}

function Format-LogUri {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    try {
        $uri = [System.Uri]$Value
        if (-not $uri.IsAbsoluteUri) {
            return '<invalid-uri>'
        }

        $authority = "{0}://{1}" -f $uri.Scheme, $uri.DnsSafeHost
        if (-not $uri.IsDefaultPort) {
            $authority = "$authority`:$($uri.Port)"
        }

        return $authority
    }
    catch {
        return '<invalid-uri>'
    }
}

function Protect-LogMessage {
    param(
        [AllowEmptyString()]
        [string]$Message
    )

    $protected = $Message -replace '[\r\n\t]+', ' '
    $protected = [Regex]::Replace($protected, '(?i)(https?://)([^/\s@]+@)', '$1')
    $protected = [Regex]::Replace($protected, '(?i)(https?://[^\s?#]+)[?#][^\s]*', '$1')
    $protected = [Regex]::Replace($protected, '(?i)(password|token|secret|authorization)=([^\s,;]+)', '$1=<redacted>')
    return $protected
}

function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [ValidateSet('Debug', 'Info', 'Warning', 'Error', 'Fatal')]
        [string]$Level = 'Info',
        [string]$ConsoleMessage,
        [string]$Component = 'Bootstrap'
    )

    $timestamp = [DateTime]::UtcNow.ToString(
        "yyyy-MM-ddTHH:mm:ss.fff'Z'",
        [System.Globalization.CultureInfo]::InvariantCulture
    )
    $levelLabel = switch ($Level) {
        'Debug' { 'DBG' }
        'Info' { 'INF' }
        'Warning' { 'WRN' }
        'Error' { 'ERR' }
        'Fatal' { 'FTL' }
    }
    $safeMessage = Protect-LogMessage -Message $Message
    $safeComponent = $Component -replace '[\r\n\t\[\]]+', '-'
    $entry = "$timestamp [$levelLabel] [Foundry.Bootstrap] [Session:$DiagnosticSessionId] [$safeComponent] $safeMessage"

    if (Test-LogLevelEnabled -MessageLevel $Level -MinimumLevel $FileLogLevel) {
        try {
            $directory = Split-Path -Path $LogPath -Parent
            if (-not (Test-Path -Path $directory)) {
                New-Item -Path $directory -ItemType Directory -Force | Out-Null
            }

            Invoke-LogRotation
            $entry | Out-File -FilePath $LogPath -Encoding utf8 -Append
        }
        catch {
            # Keep bootstrap resilient even if logging fails.
        }
    }

    if (Test-LogLevelEnabled -MessageLevel $Level -MinimumLevel $ConsoleLogLevel) {
        $displayMessage = if ([string]::IsNullOrWhiteSpace($ConsoleMessage)) {
            $Message
        }
        else {
            $ConsoleMessage
        }

        Write-ConsoleLine `
            -Message $displayMessage `
            -Level $Level
    }
}

function Invoke-LogRotation {
    if (-not (Test-Path -Path $LogPath -PathType Leaf)) {
        return
    }

    $logFile = Get-Item -Path $LogPath -Force
    if ($logFile.Length -lt $MaximumLogFileSizeBytes) {
        return
    }

    $directory = $logFile.DirectoryName
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($logFile.Name)
    $extension = $logFile.Extension
    $oldestArchivePath = Join-Path $directory "$stem.$($RetainedLogFileCount - 1)$extension"
    if (Test-Path -Path $oldestArchivePath -PathType Leaf) {
        Remove-Item -Path $oldestArchivePath -Force
    }

    for ($index = $RetainedLogFileCount - 2; $index -ge 1; $index--) {
        $archivePath = Join-Path $directory "$stem.$index$extension"
        if (Test-Path -Path $archivePath -PathType Leaf) {
            $nextArchivePath = Join-Path $directory "$stem.$($index + 1)$extension"
            Move-Item -Path $archivePath -Destination $nextArchivePath -Force
        }
    }

    Move-Item -Path $LogPath -Destination (Join-Path $directory "$stem.1$extension") -Force
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
        Write-Log "$FriendlyName service '$ServiceName' is not available in this image." -ConsoleMessage "${FriendlyName}: unavailable in this image."
        return $false
    }

    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Write-Log "$FriendlyName service '$ServiceName' is already running." -ConsoleMessage "${FriendlyName}: already running."
        return $true
    }

    try {
        Write-Log "Starting $FriendlyName service '$ServiceName'." -ConsoleMessage "${FriendlyName}: starting..."
        Start-Service -Name $ServiceName -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(10))
        Write-Log "$FriendlyName service '$ServiceName' is running." -ConsoleMessage "${FriendlyName}: started."
        return $true
    }
    catch {
        Write-Log "Failed to start $FriendlyName service '$ServiceName': $($_.Exception.Message)." -Level Warning -ConsoleMessage "${FriendlyName}: could not start."
        return $false
    }
}
#endregion

#region Runtime And Filesystem Helpers

function Resolve-TrustedRelativePath {
    param([string]$RootPath, [string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '[<>:"|?*\x00-\x1F]') { throw 'Unsafe runtime path.' }
    foreach ($segment in ($RelativePath -split '[/\\]')) {
        if (-not $segment -or $segment -in @('.','..') -or $segment.TrimEnd(' ','.') -cne $segment -or
            $segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw 'Unsafe runtime path.' }
    }
    $root = [IO.Path]::GetFullPath($RootPath).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $RelativePath.Replace('/', '\')))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime path escapes root.' }
    return $path
}

function Get-BootstrapFileAttributes {
    param([string]$Path)
    return [IO.File]::GetAttributes($Path)
}

function Assert-NoReparsePath {
    param([string]$Path)
    $current = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($current)) {
        if (([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) -and
            (((Get-BootstrapFileAttributes $current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Reparse runtime path rejected.' }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Get-CompleteRuntimeFiles {
    param([string]$RootPath)
    Assert-NoReparsePath $RootPath
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push([IO.Path]::GetFullPath($RootPath))
    while ($pending.Count -gt 0) {
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            $attributes = Get-BootstrapFileAttributes $path
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse runtime entry rejected.' }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { $pending.Push($path) }
            else { Write-Output $path }
        }
    }
}

function Open-VerifiedRuntimeFiles {
    param([string]$RootPath, $ExpectedFiles)
    $opened = [Collections.Generic.List[object]]::new()
    try {
        $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $ExpectedFiles) {
            $path = Resolve-TrustedRelativePath $RootPath ([string]$entry.relativePath)
            if (-not $expected.Add($path) -or [string]$entry.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
                $null -eq $entry.length -or [long]$entry.length -lt 0) { throw 'Invalid runtime identity.' }
            Assert-NoReparsePath $path
            $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            $opened.Add(@{Stream=$stream;Entry=$entry})
            if ($stream.Length -ne [long]$entry.length) { throw 'Runtime length mismatch.' }
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
            finally { $sha.Dispose() }
            if (-not [string]::Equals($hash,[string]$entry.sha256,[StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime digest mismatch.' }
            $stream.Position = 0
        }
        if ($expected.Count -eq 0) { throw 'Runtime identity is empty.' }
        $actual = @(Get-CompleteRuntimeFiles $RootPath)
        if ($actual.Count -ne $expected.Count) { throw 'Unexpected runtime file set.' }
        foreach ($path in $actual) { if (-not $expected.Contains($path)) { throw 'Unexpected runtime file.' } }
        return ,$opened
    }
    catch { foreach ($item in $opened) { $item.Stream.Dispose() }; throw }
}

function Test-RuntimeFiles {
    param([string]$RootPath, $ExpectedFiles)
    try {
        $opened = Open-VerifiedRuntimeFiles $RootPath $ExpectedFiles
        foreach ($item in $opened) { $item.Stream.Dispose() }
        return $true
    }
    catch { return $false }
}

function Copy-VerifiedRuntimeToRam {
    param([string]$SourceRoot, $ExpectedFiles, [string]$DestinationRoot, [string]$ApplicationName)
    if ($ApplicationName -notin @('Foundry.Connect','Foundry.Deploy')) { throw 'Unknown runtime application.' }
    Assert-NoReparsePath $DestinationRoot
    if ([IO.Directory]::Exists($DestinationRoot) -or [IO.File]::Exists($DestinationRoot)) { throw 'Runtime destination already exists.' }
    $opened = Open-VerifiedRuntimeFiles $SourceRoot $ExpectedFiles
    try {
        $null = [IO.Directory]::CreateDirectory($DestinationRoot)
        foreach ($item in $opened) {
            $path = Resolve-TrustedRelativePath $DestinationRoot ([string]$item.Entry.relativePath)
            $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
            $output = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try { $item.Stream.CopyTo($output); $output.Flush() } finally { $output.Dispose() }
        }
        if (-not (Test-RuntimeFiles $DestinationRoot $ExpectedFiles)) { throw 'Copied runtime failed verification.' }
        $executable = Resolve-TrustedRelativePath $DestinationRoot ($ApplicationName + '.exe')
        if (-not [IO.File]::Exists($executable)) { throw 'Required application host is missing.' }
        return [IO.FileInfo]::new($executable)
    }
    finally { foreach ($item in $opened) { $item.Stream.Dispose() } }
}

function Read-TrustedMediaManifest {
    param([string]$Path, [string]$RuntimeIdentifier)
    Assert-NoReparsePath $Path
    $file = [IO.FileInfo]::new($Path)
    if (-not $file.Exists -or $file.Length -gt 8MB) { throw 'Trusted media manifest is unavailable.' }
    $manifest = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    $id = [Guid]::Empty
    if ($manifest.version -ne 1 -or -not [Guid]::TryParse([string]$manifest.mediaId,[ref]$id) -or $id -eq [Guid]::Empty -or
        [string]$manifest.runtimeIdentifier -cne $RuntimeIdentifier -or $manifest.target -notin @('Iso','UsbCreate','UsbUpdate')) { throw 'Trusted media identity is invalid.' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($application in $manifest.applications) {
        if ($application.applicationName -notin @('Foundry.Connect','Foundry.Deploy') -or -not $names.Add([string]$application.applicationName) -or
            [string]$application.runtimeIdentifier -cne $RuntimeIdentifier -or $application.source -notin @('Release','Debug') -or @($application.files).Count -eq 0) { throw 'Trusted runtime declaration is invalid.' }
        if (@($application.files | Where-Object { $_.relativePath -ceq ($application.applicationName + '.exe') }).Count -ne 1) { throw 'Trusted application host is missing.' }
    }
    if (-not $names.Contains('Foundry.Connect')) { throw 'Trusted Connect declaration is missing.' }
    return $manifest
}

function Test-StableUsbIdentity {
    param($Expected, $Actual)
    if ($null -eq $Expected -or $null -eq $Actual -or [uint64]$Expected.size -eq 0 -or [uint64]$Expected.size -ne [uint64]$Actual.size -or
        ([string]::IsNullOrWhiteSpace([string]$Expected.uniqueId) -and [string]::IsNullOrWhiteSpace([string]$Expected.serialNumber))) { return $false }
    foreach ($field in @('uniqueId','serialNumber','busType','friendlyName')) {
        if (-not [string]::Equals(([string]$Expected.$field).Trim(),([string]$Actual.$field).Trim(),[StringComparison]::OrdinalIgnoreCase)) { return $false }
    }
    return $true
}

function Get-BootstrapVolumeCandidates {
    $records = @()
    foreach ($volume in @(Get-Volume -ErrorAction Stop)) {
        if (-not $volume.DriveLetter) { continue }
        $root = [string]$volume.DriveLetter + ':\'
        try {
            $markerPath = Join-Path $root 'Foundry\Config\foundry.media.marker.json'
            Assert-NoReparsePath $markerPath
            $markerInfo = [IO.FileInfo]::new($markerPath)
            if (-not $markerInfo.Exists -or $markerInfo.Length -gt 4096) { continue }
            $marker = [IO.File]::ReadAllText($markerPath) | ConvertFrom-Json
            if ($marker.version -ne 1) { continue }
            $disk = Get-Partition -DriveLetter $volume.DriveLetter -ErrorAction Stop | Get-Disk -ErrorAction Stop
            if (@($disk).Count -ne 1) { continue }
            $records += @{RootPath=$root;Label=[string]$volume.FileSystemLabel;DiskNumber=$disk.Number;DiskIdentity=$disk;MediaId=$marker.mediaId;RuntimeIdentifier=$marker.runtimeIdentifier}
        }
        catch { continue }
    }
    return $records
}

function Get-UsbCacheRuntimeRoot {
    param($MediaManifest = $script:TrustedMediaManifest, $Volumes)
    if ($null -eq $MediaManifest -or $MediaManifest.target -eq 'Iso') { return $null }
    if ($null -eq $Volumes) { $Volumes = @(Get-BootstrapVolumeCandidates) }
    $matches = @()
    foreach ($volume in $Volumes) {
        if ($volume.Label -ine 'Foundry Cache' -or [string]$volume.MediaId -ine [string]$MediaManifest.mediaId -or
            [string]$volume.RuntimeIdentifier -cne [string]$MediaManifest.runtimeIdentifier -or
            -not (Test-StableUsbIdentity $MediaManifest.intendedUsbIdentity $volume.DiskIdentity)) { continue }
        $boots = @($Volumes | Where-Object {
            $_.Label -ieq 'BOOT' -and $_.DiskNumber -eq $volume.DiskNumber -and
            [string]$_.MediaId -ieq [string]$MediaManifest.mediaId -and [string]$_.RuntimeIdentifier -ceq [string]$MediaManifest.runtimeIdentifier -and
            (Test-StableUsbIdentity $MediaManifest.intendedUsbIdentity $_.DiskIdentity)
        })
        if ($boots.Count -eq 1) { $matches += $volume }
    }
    if ($matches.Count -ne 1) { return $null }
    return Join-Path $matches[0].RootPath 'Runtime'
}

function Resolve-PinnedRuntime {
    param([string]$ApplicationName, $MediaManifest, [string]$CacheRuntimeRoot, [string]$EmbeddedRuntimeRoot, [string]$RamRoot)
    $application = @($MediaManifest.applications | Where-Object { $_.applicationName -ceq $ApplicationName })
    if ($application.Count -ne 1) { throw 'Requested runtime is not pinned by this media.' }
    $relative = $ApplicationName + '/' + [string]$MediaManifest.runtimeIdentifier
    foreach ($sourceRoot in @($CacheRuntimeRoot, $EmbeddedRuntimeRoot)) {
        if ([string]::IsNullOrWhiteSpace($sourceRoot)) { continue }
        try {
            $source = Resolve-TrustedRelativePath $sourceRoot $relative
            $destination = Join-Path $RamRoot ($ApplicationName + '-' + [Guid]::NewGuid().ToString('N'))
            $executable = Copy-VerifiedRuntimeToRam $source $application[0].files $destination $ApplicationName
            $script:ExecutionIdentities[$executable.DirectoryName] = $application[0].files
            return $executable
        }
        catch { Write-Log 'Runtime candidate failed pinned file validation; trying the embedded fallback.' -Level Warning }
    }
    throw 'No verified runtime is available.'
}

function Read-OfflineReadinessEnvelope {
    param([string]$Path, [string]$Nonce, $MediaManifest, [string]$ConfigurationPath, [string]$ExpectedEnvelopeDigest = '')
    Assert-NoReparsePath $Path
    $file = [IO.FileInfo]::new($Path)
    if (-not $file.Exists -or $file.Length -le 0 -or $file.Length -gt 65536) { throw 'Offline readiness envelope is unavailable.' }
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        if ($stream.Length -gt 65536) { throw 'Offline readiness envelope exceeds its limit.' }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $sha.Dispose() }
        if ($ExpectedEnvelopeDigest -and $digest -ine $ExpectedEnvelopeDigest) { throw 'Offline readiness envelope was replaced.' }
        $stream.Position = 0
        $reader = [IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
        try { $envelope = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    if ($envelope.Version -ne 1 -or [string]$envelope.Nonce -cne $Nonce -or
        [string]$envelope.RuntimeIdentifier -cne [string]$MediaManifest.runtimeIdentifier -or
        [string]$envelope.Result.MediaId -ine [string]$MediaManifest.mediaId -or
        $envelope.CanBrowse -isnot [bool] -or $envelope.Result.CanContinue -isnot [bool] -or [string]$envelope.Result.ConfigurationDigest -notmatch '^[0-9a-fA-F]{64}$' -or
        [string]$envelope.Result.ConfigurationDigest -ine (Get-FileSha256 $ConfigurationPath)) { throw 'Offline readiness is not bound to this run.' }
    if ($envelope.Result.CanContinue -and (-not $envelope.CanBrowse -or @($envelope.Result.BlockingReasons).Count -ne 0)) { throw 'Offline readiness is contradictory.' }
    if ($envelope.CanBrowse) {
        if ($null -eq $envelope.Result.CatalogRevisions.'operating-systems') { throw 'Offline browsing lacks required evidence.' }
        foreach ($property in $envelope.Result.CatalogRevisions.PSObject.Properties) {
            $expected = @($MediaManifest.catalogSnapshots | Where-Object { $_.id -ceq $property.Name })
            if ($expected.Count -ne 1 -or [string]$expected[0].revision -cne [string]$property.Value) { throw 'Offline catalog revision is not pinned.' }
        }
    }
    return @{Envelope=$envelope;Digest=$digest;Path=$Path;Nonce=$Nonce}
}

function ConvertTo-BootstrapArgument {
    param([AllowEmptyString()][string]$Value)
    return '"' + [regex]::Replace([regex]::Replace($Value,'(\\*)"','$1$1\"'),'(\\+)$','$1$1') + '"'
}

function Invoke-OfflineReadinessChild {
    param([IO.FileInfo]$Executable, [string]$ConfigurationPath, [string]$ResultPath, [string]$Nonce)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable.FullName
    $start.WorkingDirectory = $Executable.DirectoryName
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.Arguments = (@('--check-offline-readiness','--config',$ConfigurationPath,'--result',$ResultPath,'--nonce',$Nonce) | ForEach-Object { ConvertTo-BootstrapArgument $_ }) -join ' '
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false; $confirmed = $false
    try {
        if (-not (Test-RuntimeFiles $Executable.DirectoryName $script:ExecutionIdentities[$Executable.DirectoryName])) { throw 'Readiness RAM runtime changed before launch.' }
        if (-not $process.Start()) { throw 'Offline readiness process did not start.' }
        $started = $true
        if (-not $process.WaitForExit(600000)) {
            $process.Kill()
            if (-not $process.WaitForExit(30000)) { throw 'Offline readiness process requires recovery.' }
            $confirmed = $true
            throw 'Offline readiness timed out.'
        }
        $confirmed = $true
        return $process.ExitCode
    }
    catch {
        if ($started -and -not $confirmed) {
            try { if (-not $process.HasExited) { $process.Kill() }; $confirmed = $process.WaitForExit(30000) } catch { $confirmed = $false }
            if (-not $confirmed) { $_.Exception.Data['ProcessRootExitConfirmed'] = $false }
        }
        throw
    }
    finally { $process.Dispose() }
}

function New-OfflineReadinessHandoff {
    param([IO.FileInfo]$Executable, [string]$ConfigurationPath, $MediaManifest, [string]$RamRoot)
    $nonce = [Guid]::NewGuid().ToString('D')
    $directory = Join-Path $RamRoot ('readiness-' + [Guid]::NewGuid().ToString('N'))
    Assert-NoReparsePath $directory
    $null = [IO.Directory]::CreateDirectory($directory)
    $path = Join-Path $directory 'readiness.json'
    $exitCode = Invoke-OfflineReadinessChild $Executable $ConfigurationPath $path $nonce
    if ($exitCode -notin @(0,2)) { throw 'Offline readiness evaluation failed.' }
    $result = Read-OfflineReadinessEnvelope $path $nonce $MediaManifest $ConfigurationPath
    if (($exitCode -eq 0) -ne $result.Envelope.Result.CanContinue) { throw 'Offline readiness exit disagrees with its result.' }
    return $result
}

function Expand-AuthenticatedRuntimeArchive {
    param([string]$ArchivePath, [string]$ExpectedSha256, [string]$DestinationRoot, [string]$ApplicationName)
    Add-Type -AssemblyName System.IO.Compression
    Assert-NoReparsePath $ArchivePath
    Assert-NoReparsePath $DestinationRoot
    if ([IO.Directory]::Exists($DestinationRoot)) { throw 'Archive destination already exists.' }
    $stream = [IO.File]::Open($ArchivePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $archive = $null
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $sha.Dispose() }
        Assert-ExpectedSha256 $digest $ExpectedSha256 'runtime archive'
        $stream.Position = 0
        $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Read,$true)
        $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $expanded = 0L
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.TrimEnd('/','\')
            $path = Resolve-TrustedRelativePath $DestinationRoot $relative
            if (-not $paths.Add($path) -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or
                ($entry.ExternalAttributes -band 0x400) -ne 0) { throw 'Unsafe runtime archive entry.' }
            $expanded += $entry.Length
            if ($expanded -gt 10GB -or $archive.Entries.Count -gt 100000) { throw 'Runtime archive exceeds extraction limits.' }
        }
        $null = [IO.Directory]::CreateDirectory($DestinationRoot)
        foreach ($entry in $archive.Entries) {
            $path = Resolve-TrustedRelativePath $DestinationRoot $entry.FullName.TrimEnd('/','\')
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) { $null = [IO.Directory]::CreateDirectory($path); continue }
            $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
            $input = $entry.Open()
            $output = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try {
                $buffer = New-Object byte[] 65536
                while (($count = $input.Read($buffer,0,$buffer.Length)) -gt 0) {
                    if ($output.Length + $count -gt $entry.Length) { throw 'Runtime entry exceeded its declared length.' }
                    $output.Write($buffer,0,$count)
                }
                if ($output.Length -ne $entry.Length) { throw 'Runtime entry length mismatch.' }
            }
            finally { $input.Dispose(); $output.Dispose() }
        }
        $root = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\','/') + '\'
        $files = @(foreach ($path in Get-CompleteRuntimeFiles $DestinationRoot) {
            @{relativePath=$path.Substring($root.Length).Replace('\','/');length=([IO.FileInfo]$path).Length;sha256=(Get-FileSha256 $path)}
        })
        if (@($files | Where-Object { $_.relativePath -ceq ($ApplicationName + '.exe') }).Count -ne 1 -or
            -not (Test-RuntimeFiles $DestinationRoot $files)) { throw 'Authenticated runtime is incomplete.' }
        return ,$files
    }
    finally { if ($null -ne $archive) { $archive.Dispose() }; $stream.Dispose() }
}

function Publish-VerifiedRuntimeCache {
    param([IO.FileInfo]$Executable, $Files, [string]$ApplicationName, [string]$RuntimeIdentifier)
    $selected = $script:SelectedUsbRuntimeRoot
    if ([string]::IsNullOrWhiteSpace($selected)) { return }
    if ((Get-UsbCacheRuntimeRoot -MediaManifest $script:TrustedMediaManifest) -ine $selected) { throw 'USB association changed before publication.' }
    $destination = Get-RuntimeCacheRoot -BootstrapRoot $selected -ApplicationName $ApplicationName -RuntimeIdentifier $RuntimeIdentifier
    Assert-NoReparsePath $destination
    if ([IO.Directory]::Exists($destination)) { $null = @(Get-CompleteRuntimeFiles $destination) }
    $stage = $destination + '.staging-' + [Guid]::NewGuid().ToString('N')
    $retain = $false
    try {
        $null = Copy-VerifiedRuntimeToRam $Executable.DirectoryName $Files $stage $ApplicationName
        if ((Get-UsbCacheRuntimeRoot -MediaManifest $script:TrustedMediaManifest) -ine $selected) { throw 'USB association changed before publication.' }
        Promote-StagedCache $stage $destination
    }
    catch { $retain = $_.Exception.Data['PublicationRecoveryRequired'] -eq $true; throw }
    finally { if (-not $retain -and [IO.Directory]::Exists($stage)) { [IO.Directory]::Delete($stage,$true) } }
}

function Resolve-AuthenticatedRuntime {
    param([string]$ApplicationName, [string]$RuntimeIdentifier, [string]$RamRoot, [hashtable]$Headers, [IO.FileInfo]$Fallback)
    $application = @($script:TrustedMediaManifest.applications | Where-Object { $_.applicationName -ceq $ApplicationName })
    if ($application.Count -eq 1 -and $application[0].source -eq 'Debug') {
        if ($null -eq $Fallback) { throw 'Authored debug runtime is unavailable.' }
        $override = Get-ArchiveOverridePath $ApplicationName
        if (-not [string]::IsNullOrWhiteSpace($override)) {
            $archivePath = Join-Path $RamRoot ([Guid]::NewGuid().ToString('N') + '.zip')
            try {
                if (Test-HttpUrl $override) { throw 'Debug overrides require a local archive matching the authored files.' }
                Assert-NoReparsePath $override
                $input = [IO.File]::Open($override,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
                try {
                    $output = [IO.File]::Open($archivePath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
                    try { $input.CopyTo($output) } finally { $output.Dispose() }
                }
                finally { $input.Dispose() }
                $digest = Get-FileSha256 $archivePath
                $expectedDigest = Get-ArchiveOverrideSha256 $ApplicationName
                if ($expectedDigest) { Assert-ExpectedSha256 $digest $expectedDigest 'authored debug archive' }
                $destination = Join-Path $RamRoot ($ApplicationName + '-debug-' + [Guid]::NewGuid().ToString('N'))
                $null = Expand-AuthenticatedRuntimeArchive $archivePath $digest $destination $ApplicationName
                if (-not (Test-RuntimeFiles $destination $application[0].files)) { throw 'Debug override does not match the authored runtime.' }
                $script:ExecutionIdentities[$destination] = $application[0].files
                return [IO.FileInfo]::new((Join-Path $destination ($ApplicationName + '.exe')))
            }
            catch { Write-Log 'Debug archive override was rejected; using the authored runtime.' -Level Warning }
            finally { if ([IO.File]::Exists($archivePath)) { [IO.File]::Delete($archivePath) } }
        }
        return $Fallback
    }
    try {
        $release = Invoke-WithRetry -MaxAttempts 3 -Action { Invoke-RestMethod -Uri (Resolve-ReleaseApiUrl -ReleaseTagOverride (Get-ReleaseTagOverride $ApplicationName)) -Headers $Headers -Method Get -TimeoutSec 30 -MaximumRedirection 0 }
        $name = Resolve-ReleaseAssetName $ApplicationName $RuntimeIdentifier
        $assets = @($release.assets | Where-Object { $_.name -ceq $name })
        if ($assets.Count -ne 1) { throw 'Exact runtime release asset is unavailable.' }
        $asset = $assets[0]
        $digest = Get-ReleaseAssetSha256 $asset
        if ($digest -notmatch '^[0-9a-fA-F]{64}$' -or [long]$asset.size -le 0) { throw 'Authenticated runtime identity is missing.' }
        $archivePath = Join-Path $RamRoot ([Guid]::NewGuid().ToString('N') + '.zip')
        $destination = Join-Path $RamRoot ($ApplicationName + '-release-' + [Guid]::NewGuid().ToString('N'))
        try {
            $null = Save-WebFile ([string]$asset.browser_download_url) $archivePath $digest ([long]$asset.size)
            $files = Expand-AuthenticatedRuntimeArchive $archivePath $digest $destination $ApplicationName
            $executable = [IO.FileInfo]::new((Join-Path $destination ($ApplicationName + '.exe')))
            $script:ExecutionIdentities[$destination] = $files
            try { Publish-VerifiedRuntimeCache $executable $files $ApplicationName $RuntimeIdentifier }
            catch { Write-Log 'Runtime cache publication failed; using the verified RAM runtime.' -Level Warning }
            return $executable
        }
        finally { if ([IO.File]::Exists($archivePath)) { [IO.File]::Delete($archivePath) } }
    }
    catch {
        if ($null -eq $Fallback) { throw }
        Write-Log 'Runtime update unavailable; using the boot-pinned RAM runtime.' -Level Warning
        return $Fallback
    }
}

function Copy-BootstrapLogsToCache {
    $targetDirectory = $null
    try {
        $usbRuntimeRoot = $script:SelectedUsbRuntimeRoot
        if ([string]::IsNullOrWhiteSpace($usbRuntimeRoot)) {
            Write-Log 'Skipping bootstrap log persistence because no Foundry Cache volume is available.' -Level Debug -Component 'LogPersistence'
            return
        }
        if ((Get-UsbCacheRuntimeRoot -MediaManifest $script:TrustedMediaManifest) -ine $usbRuntimeRoot) { return }

        $sourceDirectory = Split-Path -Path $LogPath -Parent
        if (-not (Test-Path -Path $sourceDirectory -PathType Container)) {
            return
        }

        $cacheRoot = Split-Path -Path $usbRuntimeRoot -Parent
        $targetDirectory = Join-Path $cacheRoot (Join-Path 'Logs' $DiagnosticSessionId)
        Ensure-Directory -Path $targetDirectory
        Write-Log "Persisting WinPE session logs to '$targetDirectory'." -Level Debug -Component 'LogPersistence'

        foreach ($sourceFile in Get-ChildItem -Path $sourceDirectory -Filter 'Foundry*.log' -File -ErrorAction Stop) {
            $destinationPath = Join-Path $targetDirectory $sourceFile.Name
            $temporaryPath = "$destinationPath.$([Guid]::NewGuid().ToString('N')).tmp"
            $sourceStream = $null
            $destinationStream = $null
            try {
                $sourceStream = New-Object System.IO.FileStream(
                    $sourceFile.FullName,
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read,
                    ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
                )
                $destinationStream = New-Object System.IO.FileStream(
                    $temporaryPath,
                    [System.IO.FileMode]::CreateNew,
                    [System.IO.FileAccess]::Write,
                    [System.IO.FileShare]::None
                )
                $sourceStream.CopyTo($destinationStream)
            }
            finally {
                if ($null -ne $destinationStream) {
                    $destinationStream.Dispose()
                }
                if ($null -ne $sourceStream) {
                    $sourceStream.Dispose()
                }
            }

            if (Test-Path -Path $destinationPath -PathType Leaf) {
                Remove-Item -Path $destinationPath -Force
            }
            Move-Item -Path $temporaryPath -Destination $destinationPath -Force
        }
    }
    catch {
        try {
            Write-Log "Failed to persist WinPE session logs: $($_.Exception.Message)" -Level Warning -Component 'LogPersistence' -ConsoleMessage 'Log persistence to the cache volume failed.'
        }
        catch {
            # Log persistence must never replace the bootstrap outcome.
        }
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
            Get-ChildItem -Path $targetDirectory -Filter '*.tmp' -File -ErrorAction SilentlyContinue |
                Remove-Item -Force -ErrorAction SilentlyContinue
        }
    }
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

#endregion

#region Network And Time Helpers

function New-BootstrapWebRequest {
    param([Uri]$Uri)
    return [Net.HttpWebRequest]::Create($Uri)
}

function Save-WebFile {
    param([string]$SourceUrl, [string]$DestinationPath, [string]$ExpectedSha256, [long]$ExpectedLength,
        [int]$OverallTimeoutSeconds = 900, [int]$NoProgressTimeoutSeconds = 30)
    if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$' -or $ExpectedLength -le 0 -or
        $OverallTimeoutSeconds -le 0 -or $OverallTimeoutSeconds -gt 900 -or
        $NoProgressTimeoutSeconds -le 0 -or $NoProgressTimeoutSeconds -gt 30) { throw 'A bounded authenticated download identity is required.' }
    Assert-NoReparsePath $DestinationPath
    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($DestinationPath))
    $null = [IO.Directory]::CreateDirectory($directory)
    $temporary = Join-Path $directory ([Guid]::NewGuid().ToString('N') + '.partial')
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        for ($attempt = 0; $attempt -lt 3; $attempt++) {
            $request = $null; $response = $null; $input = $null; $output = $null
            try {
                $uri = [Uri]$SourceUrl
                for ($redirect = 0; $redirect -le 5; $redirect++) {
                    if ($uri.Scheme -cne 'https' -or $uri.UserInfo -or $uri.Fragment) { throw 'Only authenticated HTTPS downloads are supported.' }
                    $remaining = $OverallTimeoutSeconds * 1000 - $clock.ElapsedMilliseconds
                    if ($remaining -le 0) { throw 'Runtime download deadline expired.' }
                    $request = New-BootstrapWebRequest $uri
                    $request.AllowAutoRedirect = $false
                    $request.Timeout = [int][Math]::Min(30000, $remaining)
                    $request.ReadWriteTimeout = [int][Math]::Min($NoProgressTimeoutSeconds * 1000, $remaining)
                    $request.UserAgent = 'FoundryBootstrap/1.0'
                    $pending = $request.BeginGetResponse($null, $null)
                    if (-not $pending.AsyncWaitHandle.WaitOne($request.Timeout)) {
                        $request.Abort()
                        if ($pending.IsCompleted) { try { $null = $request.EndGetResponse($pending) } catch {} }
                        throw [TimeoutException]::new('Runtime download header deadline expired.')
                    }
                    $response = $request.EndGetResponse($pending)
                    if ([int]$response.StatusCode -in @(301,302,303,307,308)) {
                        if ($redirect -eq 5 -or [string]::IsNullOrWhiteSpace([string]$response.Headers['Location'])) { throw 'Invalid runtime download redirect.' }
                        $uri = [Uri]::new($uri,[string]$response.Headers['Location'])
                        $response.Dispose(); $response = $null; $request.Abort(); $request = $null
                        continue
                    }
                    if ([int]$response.StatusCode -ne 200) { throw 'Runtime download status is not successful.' }
                    break
                }
                if ($response.ContentLength -ge 0 -and $response.ContentLength -ne $ExpectedLength) { throw 'Runtime archive length mismatch.' }
                $input = $response.GetResponseStream()
                if (-not $input.CanTimeout) { throw 'Runtime transport cannot enforce a read deadline.' }
                $output = [IO.File]::Open($temporary,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
                $buffer = New-Object byte[] 65536
                $received = 0L
                while ($true) {
                    $remaining = $OverallTimeoutSeconds * 1000 - $clock.ElapsedMilliseconds
                    if ($remaining -le 0) { throw [TimeoutException]::new('Runtime download deadline expired.') }
                    if ($input.CanTimeout) { $input.ReadTimeout = [int][Math]::Min($remaining,$NoProgressTimeoutSeconds * 1000) }
                    $count = $input.Read($buffer,0,$buffer.Length)
                    if ($count -eq 0) { break }
                    $received += $count
                    if ($received -gt $ExpectedLength) { throw 'Runtime archive exceeded its expected length.' }
                    $output.Write($buffer,0,$count)
                }
                $output.Dispose(); $output = $null
                if ($received -ne $ExpectedLength) { throw 'Runtime archive is truncated.' }
                Assert-ExpectedSha256 (Get-FileSha256 $temporary) $ExpectedSha256 'runtime archive'
                if ($clock.Elapsed.TotalSeconds -ge $OverallTimeoutSeconds) { throw [TimeoutException]::new('Runtime download deadline expired.') }
                if ([IO.File]::Exists($DestinationPath)) { [IO.File]::Replace($temporary,$DestinationPath,[NullString]::Value) }
                else { [IO.File]::Move($temporary,$DestinationPath) }
                return [IO.FileInfo]::new($DestinationPath)
            }
            catch {
                $retry = $false; $delay = [Math]::Pow(2,$attempt)
                $cause = $_.Exception
                while ($null -ne $cause) {
                    if ($cause -is [TimeoutException]) { $retry = $true }
                    if ($cause -is [Net.WebException]) {
                        $retry = $cause.Status -in @([Net.WebExceptionStatus]::Timeout,[Net.WebExceptionStatus]::ConnectFailure,[Net.WebExceptionStatus]::ConnectionClosed,[Net.WebExceptionStatus]::ReceiveFailure,[Net.WebExceptionStatus]::SendFailure,[Net.WebExceptionStatus]::NameResolutionFailure)
                        if ($null -ne $cause.Response) {
                            try {
                                $retry = [int]$cause.Response.StatusCode -in @(408,429,500,502,503,504)
                                $seconds = 0.0
                                if ([double]::TryParse([string]$cause.Response.Headers['Retry-After'],[ref]$seconds)) { $delay = [Math]::Max($delay,$seconds) }
                                else {
                                    $date = [DateTimeOffset]::MinValue
                                    if ([DateTimeOffset]::TryParse([string]$cause.Response.Headers['Retry-After'],[ref]$date)) { $delay = [Math]::Max($delay,($date-[DateTimeOffset]::UtcNow).TotalSeconds) }
                                }
                            } finally { $cause.Response.Dispose() }
                        }
                        break
                    }
                    $cause = $cause.InnerException
                }
                if (-not $retry -or $attempt -eq 2 -or $clock.Elapsed.TotalSeconds + $delay -ge $OverallTimeoutSeconds) { throw }
            }
            finally {
                if ($null -ne $request) { $request.Abort() }
                if ($null -ne $input) { $input.Dispose() }
                if ($null -ne $output) { $output.Dispose() }
                if ($null -ne $response) { $response.Dispose() }
            }
            Start-Sleep -Milliseconds ([int]($delay * 1000))
        }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
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
            Write-Log "Requesting internet time from '$(Format-LogUri -Value $probeUrl)'." -Level Debug
            $response = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $probeUrl -TimeoutSec 10 -MaximumRedirection 0 -ErrorAction Stop
            $dateHeader = [string]($response.Headers['Date'] | Select-Object -First 1)

            if (-not [string]::IsNullOrWhiteSpace($dateHeader)) {
                $internetDateTime = Get-Date $dateHeader
                break
            }

            Write-Log "The time probe '$(Format-LogUri -Value $probeUrl)' did not return an HTTP Date header." -Level Debug
        }
        catch {
            Write-Log "The time probe '$(Format-LogUri -Value $probeUrl)' failed: $($_.Exception.Message)." -Level Debug
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
    if (-not [string]::IsNullOrWhiteSpace($convertedTimeZoneId) -and (Test-WindowsTimeZoneId -TimeZoneId $convertedTimeZoneId)) {
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

function Get-FileSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Assert-NoReparsePath $Path
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $sha.Dispose(); $stream.Dispose() }
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

function Promote-StagedCache {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StagingRoot,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeCacheRoot
    )

    $backupRoot = "$RuntimeCacheRoot.previous-$([Guid]::NewGuid().ToString('N'))"
    $activeMoved = $false

    try {
        if (Test-Path -LiteralPath $RuntimeCacheRoot -PathType Container) {
            [System.IO.Directory]::Move($RuntimeCacheRoot, $backupRoot)
            $activeMoved = $true
        }

        [System.IO.Directory]::Move($StagingRoot, $RuntimeCacheRoot)
    }
    catch {
        $promotionError = $_
        if ($activeMoved) {
            try {
                [System.IO.Directory]::Move($backupRoot, $RuntimeCacheRoot)
            }
            catch {
                $promotionError.Exception.Data['PublicationRecoveryRequired'] = $true
                $promotionError.Exception.Data['PublicationRetainedPaths'] = @($StagingRoot, $backupRoot)
                $promotionError.Exception.Data['PublicationRollbackFailure'] = $_.Exception
            }
        }
        throw $promotionError
    }

    if ($activeMoved) {
        try {
            Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning 'Runtime publication succeeded, but its previous cache could not be removed.'
        }
    }
}


#endregion

#region Release Resolution Helpers

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

function Start-DeployExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Executable,
        [switch]$Offline
    )

    Write-Log "Launching '$($Executable.FullName)'." -Component 'Process' -ConsoleMessage 'Foundry.Deploy: launching...'
    if (-not (Test-RuntimeFiles $Executable.DirectoryName $script:ExecutionIdentities[$Executable.DirectoryName])) { throw 'Deploy RAM runtime changed before launch.' }
    $arguments = @('--config', $EmbeddedDeployConfigurationPath)
    if ($Offline) { $arguments += '--offline' }
    $process = Start-Process -FilePath $Executable.FullName -WorkingDirectory $Executable.DirectoryName -ArgumentList (($arguments | ForEach-Object { ConvertTo-BootstrapArgument $_ }) -join ' ') -PassThru
    Write-Log "Foundry.Deploy handoff completed. ProcessId=$($process.Id)." -Component 'Process'
}

function Invoke-ConnectExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$Executable,
        [string]$ConfigurationPath,
        [string]$OfflineReadinessPath,
        [string]$OfflineNonce
    )

    $argumentList = @()
    if (-not [string]::IsNullOrWhiteSpace($ConfigurationPath) -and (Test-Path -Path $ConfigurationPath -PathType Leaf)) {
        $argumentList = @('--config', $ConfigurationPath)
        Write-Log "Launching '$($Executable.FullName)' with configuration '$ConfigurationPath'." -ConsoleMessage 'Foundry.Connect: launching...'
    }
    else {
        Write-Log "Launching '$($Executable.FullName)' without an external configuration file." -ConsoleMessage 'Foundry.Connect: launching...'
    }

    if (-not [string]::IsNullOrWhiteSpace($OfflineReadinessPath)) { $argumentList += @('--offline-readiness', $OfflineReadinessPath, '--offline-nonce', $OfflineNonce) }
    if (-not (Test-RuntimeFiles $Executable.DirectoryName $script:ExecutionIdentities[$Executable.DirectoryName])) { throw 'Connect RAM runtime changed before launch.' }
    $process = Start-Process `
        -FilePath $Executable.FullName `
        -WorkingDirectory $Executable.DirectoryName `
        -ArgumentList (($argumentList | ForEach-Object { ConvertTo-BootstrapArgument $_ }) -join ' ') `
        -Wait `
        -PassThru

    Write-Log "Foundry.Connect exited. ProcessId=$($process.Id), ExitCode=$($process.ExitCode)." -Component 'Process'
    return $process.ExitCode
}


#endregion

#region Bootstrap Execution
$script:SelectedUsbRuntimeRoot = $null
$script:ExecutionIdentities = @{}
$handoff = $null
try {
    Ensure-Directory -Path $WinPeRoot
    Write-ConsoleBanner -Title 'Foundry Bootstrap'
    $runtimeIdentifier = Get-TargetRuntimeIdentifier
    $script:TrustedMediaManifest = Read-TrustedMediaManifest (Join-Path $WinPeRoot 'Config\foundry.media.manifest.json') $runtimeIdentifier
    $script:SelectedUsbRuntimeRoot = Get-UsbCacheRuntimeRoot -MediaManifest $script:TrustedMediaManifest
    $deploymentMode = if ($script:TrustedMediaManifest.target -eq 'Iso') { 'Iso' } else { 'Usb' }
    $env:FOUNDRY_DEPLOYMENT_MODE = $deploymentMode
    $env:FOUNDRY_VERIFIED_CACHE_ROOT = $null
    $env:FOUNDRY_DIAGNOSTIC_PERSISTENCE_DIRECTORY = $null
    if ($script:SelectedUsbRuntimeRoot) {
        $env:FOUNDRY_VERIFIED_CACHE_ROOT = Split-Path -Path $script:SelectedUsbRuntimeRoot -Parent
        $env:FOUNDRY_DIAGNOSTIC_PERSISTENCE_DIRECTORY = Join-Path $env:FOUNDRY_VERIFIED_CACHE_ROOT (Join-Path 'Logs' $DiagnosticSessionId)
    }
    $ramRoot = Join-Path $WinPeRoot ('Sessions\' + [Guid]::NewGuid().ToString('N'))
    Ensure-Directory $ramRoot
    $embeddedRuntime = Join-Path $WinPeRoot 'Runtime'
    $connectExecutable = Resolve-PinnedRuntime 'Foundry.Connect' $script:TrustedMediaManifest $script:SelectedUsbRuntimeRoot $embeddedRuntime $ramRoot
    $deployExecutable = $null
    try { $deployExecutable = Resolve-PinnedRuntime 'Foundry.Deploy' $script:TrustedMediaManifest $script:SelectedUsbRuntimeRoot $embeddedRuntime $ramRoot }
    catch { Write-Log 'A boot-pinned Deploy runtime is unavailable; offline continuation cannot be offered.' -Level Warning }
    if ($null -ne $deployExecutable) {
        Write-Log 'Checking local deployment content before offering offline continuation.'
        try { $handoff = New-OfflineReadinessHandoff $deployExecutable $EmbeddedDeployConfigurationPath $script:TrustedMediaManifest $ramRoot }
        catch {
            if ($_.Exception.Data['ProcessRootExitConfirmed'] -eq $false) { throw }
            Write-Log 'Local content readiness could not be confirmed; offline continuation is unavailable.' -Level Warning
        }
    }
    [void](Ensure-ServiceRunning -ServiceName 'dot3svc' -FriendlyName 'Wired AutoConfig')
    Start-WinPeWirelessServiceIfSupported
    $connectArguments = @{Executable=$connectExecutable;ConfigurationPath=$EmbeddedConnectConfigurationPath}
    if ($null -ne $handoff) { $connectArguments.OfflineReadinessPath=$handoff.Path; $connectArguments.OfflineNonce=$handoff.Nonce }
    $connectExitCode = Invoke-ConnectExecutable @connectArguments
    if ($connectExitCode -eq 23) {
        if ($null -eq $handoff -or $null -eq $deployExecutable) { throw 'Offline continuation was not established for this run.' }
        $current = Read-OfflineReadinessEnvelope $handoff.Path $handoff.Nonce $script:TrustedMediaManifest $EmbeddedDeployConfigurationPath $handoff.Digest
        if (-not $current.Envelope.CanBrowse) { throw 'Offline browsing is unavailable.' }
        Start-DeployExecutable -Executable $deployExecutable -Offline
    }
    elseif ($connectExitCode -eq 0) {
        Sync-WinPeInternetDateTime -ThresholdMinutes 5
        Set-WinPeTimeZone -FallbackTimeZoneId $DefaultWinPeTimeZoneId
        $headers = @{'User-Agent'='FoundryBootstrap/1.0';'Accept'='application/vnd.github+json'}
        $null = Resolve-AuthenticatedRuntime 'Foundry.Connect' $runtimeIdentifier $ramRoot $headers $connectExecutable
        $deployExecutable = Resolve-AuthenticatedRuntime 'Foundry.Deploy' $runtimeIdentifier $ramRoot $headers $deployExecutable
        Start-DeployExecutable -Executable $deployExecutable
    }
    elseif ($connectExitCode -eq 20) { throw 'Foundry.Connect was closed by the operator. Bootstrap will not continue.' }
    else { throw "Foundry.Connect exited with code $connectExitCode." }
    Write-Log 'Foundry bootstrap completed successfully.'
}
catch {
    Write-Log "Foundry bootstrap failed: $($_.Exception.Message)" -Level Fatal -Component 'Lifecycle' -ConsoleMessage 'Foundry bootstrap failed.'
    $script:BootstrapExitCode = 1
}
finally {
    if ($null -ne $handoff -and [IO.File]::Exists($handoff.Path)) {
        try { [IO.File]::Delete($handoff.Path); [IO.Directory]::Delete([IO.Path]::GetDirectoryName($handoff.Path),$false) } catch {}
    }
    Copy-BootstrapLogsToCache
}
if ($script:BootstrapExitCode -eq 1) { exit 1 }
#endregion
