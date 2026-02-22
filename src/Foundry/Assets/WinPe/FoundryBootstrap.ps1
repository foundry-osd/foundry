Clear-Host

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$LogPath = 'X:\Windows\Temp\FoundryBootstrap.log'
$Owner = 'mchave3'
$Repository = 'Foundry'
$ReleaseApiBaseUrl = "https://api.github.com/repos/$Owner/$Repository/releases"
$BootstrapRoot = 'X:\ProgramData\Foundry\Deploy'
$EmbeddedArchivePath = Join-Path $BootstrapRoot 'Seed\Foundry.Deploy.zip'
$DownloadPath = Join-Path $BootstrapRoot 'Foundry.Deploy.zip'
$ExtractPath = Join-Path $BootstrapRoot 'current'

function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $Entry = "[$(Get-Date -Format o)] $Message"

    try {
        $Directory = Split-Path -Path $LogPath -Parent
        if (-not (Test-Path -Path $Directory)) {
            New-Item -Path $Directory -ItemType Directory -Force | Out-Null
        }

        $Entry | Out-File -FilePath $LogPath -Encoding utf8 -Append
    }
    catch {
        # Keep bootstrap resilient even if logging fails.
    }

    try {
        Write-Host $Entry
    }
    catch {
        # Keep bootstrap resilient even if console output fails.
    }
}

function Get-TargetRuntimeIdentifier {
    $Architecture = [string]$env:PROCESSOR_ARCHITECTURE
    if ($null -eq $Architecture) {
        $Architecture = ''
    }
    $Architecture = $Architecture.Trim().ToUpperInvariant()
    switch ($Architecture) {
        'AMD64' { return 'win-x64' }
        'ARM64' { return 'win-arm64' }
        Default { throw "Unsupported architecture '$Architecture'." }
    }
}

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,
        [int]$MaxAttempts = 3,
        [int]$InitialDelaySeconds = 2
    )

    $Attempt = 1
    $Delay = $InitialDelaySeconds

    while ($Attempt -le $MaxAttempts) {
        try {
            return & $Action
        }
        catch {
            if ($Attempt -ge $MaxAttempts) {
                throw
            }

            Write-Log "Attempt $Attempt failed: $($_.Exception.Message). Retrying in $Delay second(s)."
            Start-Sleep -Seconds $Delay
            $Delay = [Math]::Min($Delay * 2, 20)
            $Attempt++
        }
    }
}

function Ensure-BitsAvailable {
    if (-not (Get-Command -Name Start-BitsTransfer -ErrorAction SilentlyContinue)) {
        throw 'Start-BitsTransfer is not available in this WinPE image.'
    }

    try {
        $Service = Get-Service -Name BITS -ErrorAction SilentlyContinue
        if ($null -ne $Service -and $Service.Status -ne 'Running') {
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
        $Uri = [System.Uri]$Value
        return $Uri.IsAbsoluteUri -and ($Uri.Scheme -eq 'http' -or $Uri.Scheme -eq 'https')
    }
    catch {
        return $false
    }
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
        if (Test-Path -Path $DestinationPath) {
            Remove-Item -Path $DestinationPath -Force -ErrorAction SilentlyContinue
        }

        Start-BitsTransfer `
            -Source $SourceUrl `
            -Destination $DestinationPath `
            -TransferType Download `
            -Description $Description `
            -ErrorAction Stop
    }
}

function Verify-DownloadDigestIfAvailable {
    param(
        [Parameter(Mandatory = $true)]
        $Asset,
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $Digest = [string]($Asset.digest)
    if ([string]::IsNullOrWhiteSpace($Digest)) {
        Write-Log 'No digest provided by release API for this asset; skipping hash verification.'
        return
    }

    if (-not $Digest.StartsWith('sha256:', [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Log "Unsupported digest format '$Digest'; skipping hash verification."
        return
    }

    $Expected = $Digest.Substring(7).Trim().ToUpperInvariant()
    $Actual = (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($Expected -ne $Actual) {
        throw "SHA256 mismatch. Expected '$Expected', actual '$Actual'."
    }

    Write-Log 'SHA256 digest verification succeeded.'
}

function Verify-Sha256IfProvided {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string]$ExpectedSha256
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedSha256)) {
        Write-Log 'No SHA256 override provided; skipping hash verification for override archive.'
        return
    }

    $Normalized = $ExpectedSha256.Trim().ToUpperInvariant()
    if ($Normalized -notmatch '^[0-9A-F]{64}$') {
        throw "Invalid SHA256 override '$ExpectedSha256'. Expected 64 hexadecimal characters."
    }

    $Actual = (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($Normalized -ne $Actual) {
        throw "SHA256 mismatch for override archive. Expected '$Normalized', actual '$Actual'."
    }

    Write-Log 'SHA256 override verification succeeded.'
}

try {
    Write-Log 'Foundry bootstrap started.'

    $RuntimeIdentifier = Get-TargetRuntimeIdentifier
    $AssetName = "Foundry.Deploy-$RuntimeIdentifier.zip"
    $ReleaseTagOverride = [string]$env:FOUNDRY_RELEASE_TAG
    if ($null -eq $ReleaseTagOverride) {
        $ReleaseTagOverride = ''
    }
    $ReleaseTagOverride = $ReleaseTagOverride.Trim()
    $ArchiveOverride = [string]$env:FOUNDRY_DEPLOY_ARCHIVE
    if ($null -eq $ArchiveOverride) {
        $ArchiveOverride = ''
    }
    $ArchiveOverride = $ArchiveOverride.Trim()
    $ArchiveOverrideSha256 = [string]$env:FOUNDRY_DEPLOY_ARCHIVE_SHA256
    if ($null -eq $ArchiveOverrideSha256) {
        $ArchiveOverrideSha256 = ''
    }
    $ArchiveOverrideSha256 = $ArchiveOverrideSha256.Trim()
    if ([string]::IsNullOrWhiteSpace($ArchiveOverride) -and (Test-Path -Path $EmbeddedArchivePath -PathType Leaf)) {
        $ArchiveOverride = $EmbeddedArchivePath
        Write-Log "Using embedded deploy archive from '$EmbeddedArchivePath'."
    }

    $Headers = @{
        'User-Agent' = 'FoundryBootstrap/1.0'
        'Accept' = 'application/vnd.github+json'
    }

    if (-not (Test-Path -Path $BootstrapRoot)) {
        New-Item -Path $BootstrapRoot -ItemType Directory -Force | Out-Null
    }

    $ReleaseDescriptor = ''
    if (-not [string]::IsNullOrWhiteSpace($ArchiveOverride)) {
        if (-not [string]::IsNullOrWhiteSpace($ReleaseTagOverride)) {
            Write-Log "FOUNDRY_DEPLOY_ARCHIVE is set; ignoring FOUNDRY_RELEASE_TAG '$ReleaseTagOverride'."
        }

        if (Test-HttpUrl -Value $ArchiveOverride) {
            Write-Log "Downloading override archive via BITS: $ArchiveOverride"
            Download-FileViaBits `
                -SourceUrl $ArchiveOverride `
                -DestinationPath $DownloadPath `
                -Description "Foundry Deploy bootstrap override download ($RuntimeIdentifier)"
            $ReleaseDescriptor = "archive-url:$ArchiveOverride"
        }
        else {
            if (-not (Test-Path -Path $ArchiveOverride -PathType Leaf)) {
                throw "Override archive path not found: '$ArchiveOverride'."
            }

            Write-Log "Copying override archive from '$ArchiveOverride'."
            if (Test-Path -Path $DownloadPath) {
                Remove-Item -Path $DownloadPath -Force -ErrorAction SilentlyContinue
            }
            Copy-Item -Path $ArchiveOverride -Destination $DownloadPath -Force
            $ReleaseDescriptor = "archive-file:$ArchiveOverride"
        }

        Verify-Sha256IfProvided -FilePath $DownloadPath -ExpectedSha256 $ArchiveOverrideSha256
    }
    else {
        $ReleaseApiUrl = "$ReleaseApiBaseUrl/latest"
        if (-not [string]::IsNullOrWhiteSpace($ReleaseTagOverride)) {
            $EncodedTag = [System.Uri]::EscapeDataString($ReleaseTagOverride)
            $ReleaseApiUrl = "$ReleaseApiBaseUrl/tags/$EncodedTag"
        }

        Write-Log "Resolving release metadata from $ReleaseApiUrl."
        $Release = Invoke-WithRetry -Action { Invoke-RestMethod -Uri $ReleaseApiUrl -Headers $Headers -Method Get }
        if ($null -eq $Release) {
            throw 'Unable to resolve release metadata.'
        }

        $Asset = $Release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
        if ($null -eq $Asset) {
            $Asset = $Release.assets | Where-Object { $_.name -like "*$RuntimeIdentifier*.zip" -and $_.name -like 'Foundry.Deploy*' } | Select-Object -First 1
        }

        if ($null -eq $Asset) {
            throw "No deploy asset found for runtime '$RuntimeIdentifier' in release '$($Release.tag_name)'."
        }

        Write-Log "Using release tag '$($Release.tag_name)' and asset '$($Asset.name)'."
        Write-Log "Downloading asset via BITS: $($Asset.browser_download_url)"
        Download-FileViaBits `
            -SourceUrl $Asset.browser_download_url `
            -DestinationPath $DownloadPath `
            -Description "Foundry Deploy bootstrap download ($RuntimeIdentifier)"

        Verify-DownloadDigestIfAvailable -Asset $Asset -FilePath $DownloadPath
        $ReleaseDescriptor = "$($Release.tag_name)"
    }

    if (-not (Test-Path -Path $DownloadPath)) {
        throw "Expected archive not found at '$DownloadPath' after download."
    }

    if (Test-Path -Path $ExtractPath) {
        Remove-Item -Path $ExtractPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Log "Extracting archive to '$ExtractPath'."
    Expand-Archive -Path $DownloadPath -DestinationPath $ExtractPath -Force

    $Executable = Get-ChildItem -Path $ExtractPath -Filter 'Foundry.Deploy.exe' -File -Recurse | Select-Object -First 1
    if ($null -eq $Executable) {
        throw "Unable to find 'Foundry.Deploy.exe' under '$ExtractPath'."
    }

    $ReleaseInfoPath = Join-Path $BootstrapRoot 'latest-release.txt'
    Set-Content -Path $ReleaseInfoPath -Value $ReleaseDescriptor -Encoding ascii

    Write-Log "Launching '$($Executable.FullName)'."
    Start-Process -FilePath $Executable.FullName -WorkingDirectory $Executable.DirectoryName | Out-Null

    Write-Log 'Foundry bootstrap completed successfully.'
}
catch {
    Write-Log "Foundry bootstrap failed: $($_.Exception.Message)"
}
