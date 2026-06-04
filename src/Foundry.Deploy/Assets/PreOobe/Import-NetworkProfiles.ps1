$ErrorActionPreference = 'Stop'
$DataDirectory = Join-Path $env:SystemRoot 'Temp\Foundry\PreOobe\Data'
$NetworkDataDirectory = Join-Path $DataDirectory 'NetworkProfiles'
$SettingsPath = Join-Path $NetworkDataDirectory 'import-settings.json'
$LogDirectory = Join-Path $env:SystemRoot 'Temp\Foundry\Logs\PreOobe'
$TranscriptPath = Join-Path $LogDirectory 'Import-NetworkProfiles.transcript.log'
$TranscriptStarted = $false
$ScriptStartedAt = [DateTimeOffset]::Now

function Start-FoundryTranscript {
    New-Item -Path $LogDirectory -ItemType Directory -Force | Out-Null
    Start-Transcript -Path $TranscriptPath -Force | Out-Null
    $script:TranscriptStarted = $true
}

function Stop-FoundryTranscript {
    if ($script:TranscriptStarted) {
        Stop-Transcript | Out-Null
    }
}

function Write-FoundryLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $now = [DateTimeOffset]::Now
    $elapsed = $now - $script:ScriptStartedAt
    Write-Host ("[{0}] [+{1:c}] {2}" -f $now.ToString('yyyy-MM-ddTHH:mm:ss'), $elapsed, $Message)
}

function Resolve-FoundryDataPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "Data path must be relative."
    }

    $combinedPath = Join-Path $DataDirectory $RelativePath
    $fullDataDirectory = [System.IO.Path]::GetFullPath($DataDirectory)
    $fullPath = [System.IO.Path]::GetFullPath($combinedPath)
    if (-not $fullPath.StartsWith($fullDataDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Data path is outside the Foundry pre-OOBE data directory."
    }

    return $fullPath
}

function Invoke-FoundryNetsh {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    Write-FoundryLog $Description
    & netsh.exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-FoundryLog "WARNING: netsh exited with code $LASTEXITCODE while running: $Description"
    }
}

function Import-FoundryCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Certificate
    )

    $certificatePath = Resolve-FoundryDataPath -RelativePath ([string]$Certificate.relativePath)
    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
        Write-FoundryLog "Skipping missing certificate file: $certificatePath"
        return
    }

    $storeName = [string]$Certificate.storeName
    if ([string]::IsNullOrWhiteSpace($storeName)) {
        $storeName = 'Root'
    }

    $kind = [string]$Certificate.kind
    if ($kind -ieq 'pfx') {
        Import-FoundryPfxCertificate -CertificatePath $certificatePath -Certificate $Certificate
        return
    }

    Write-FoundryLog "Importing public certificate into LocalMachine\$storeName."
    & certutil.exe -addstore -f $storeName $certificatePath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-FoundryLog "WARNING: certutil exited with code $LASTEXITCODE while importing certificate into $storeName."
    }
}

function Import-FoundryPfxCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CertificatePath,

        [Parameter(Mandatory = $true)]
        [pscustomobject]$Certificate
    )

    $passwordRelativePath = [string]$Certificate.passwordRelativePath
    $passwordPath = $null
    $securePassword = $null
    if (-not [string]::IsNullOrWhiteSpace($passwordRelativePath)) {
        $passwordPath = Resolve-FoundryDataPath -RelativePath $passwordRelativePath
        if (-not (Test-Path -LiteralPath $passwordPath -PathType Leaf)) {
            Write-FoundryLog "Skipping PFX import because the staged password file is missing."
            return
        }

        $password = Get-Content -LiteralPath $passwordPath -Raw
        $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
    }

    try {
        Write-FoundryLog "Importing PFX certificate into LocalMachine\My."
        if (Get-Command -Name Import-PfxCertificate -ErrorAction SilentlyContinue) {
            try {
                $importArguments = @{
                    FilePath = $CertificatePath
                    CertStoreLocation = 'Cert:\LocalMachine\My'
                    Exportable = $false
                    ErrorAction = 'Stop'
                }
                if ($securePassword -ne $null) {
                    $importArguments.Password = $securePassword
                }

                Import-PfxCertificate @importArguments | Out-Null
            }
            catch {
                Write-FoundryLog "WARNING: PFX certificate import failed: $($_.Exception.Message)"
            }

            return
        }

        Write-FoundryLog "WARNING: Import-PfxCertificate is unavailable; skipping PFX import to avoid exposing the password on a process command line."
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($passwordPath)) {
            Remove-Item -LiteralPath $passwordPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-FoundryWifiProfileName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProfilePath
    )

    try {
        [xml]$profile = Get-Content -LiteralPath $ProfilePath -Raw
        $namespaceManager = New-Object System.Xml.XmlNamespaceManager($profile.NameTable)
        $namespaceManager.AddNamespace('wlan', 'http://www.microsoft.com/networking/WLAN/profile/v1')
        $nameNode = $profile.SelectSingleNode('/wlan:WLANProfile/wlan:name', $namespaceManager)
        if ($null -eq $nameNode -or [string]::IsNullOrWhiteSpace($nameNode.InnerText)) {
            return $null
        }

        return $nameNode.InnerText.Trim()
    }
    catch {
        Write-FoundryLog "WARNING: Wi-Fi profile name could not be read: $($_.Exception.Message)"
        return $null
    }
}

function Connect-FoundryWifiProfile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProfilePath,

        [string]$ConnectivityExpectation
    )

    if ($ConnectivityExpectation -ine 'preOobeConnectable') {
        Write-FoundryLog "Skipping Wi-Fi reconnect because connectivityExpectation='$ConnectivityExpectation'."
        return
    }

    $profileName = Get-FoundryWifiProfileName -ProfilePath $ProfilePath
    if ([string]::IsNullOrWhiteSpace($profileName)) {
        Write-FoundryLog "Skipping Wi-Fi reconnect because the imported profile name could not be resolved."
        return
    }

    Invoke-FoundryNetsh -Arguments @('wlan', 'connect', "name=$profileName") -Description "Connecting Wi-Fi profile '$profileName'."
}

function Import-FoundryWifiProfile {
    param(
        [string]$RelativePath,
        [string]$Source,
        [string]$ConnectivityExpectation
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return
    }

    $profilePath = Resolve-FoundryDataPath -RelativePath $RelativePath
    if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
        Write-FoundryLog "Skipping missing Wi-Fi profile: $profilePath"
        return
    }

    Write-FoundryLog "Wi-Fi profile source='$Source', connectivityExpectation='$ConnectivityExpectation'."
    Invoke-FoundryNetsh -Arguments @('wlan', 'add', 'profile', "filename=$profilePath", 'user=all') -Description 'Importing Wi-Fi profile for all users.'
    Connect-FoundryWifiProfile -ProfilePath $profilePath -ConnectivityExpectation $ConnectivityExpectation
}

function Import-FoundryWiredProfile {
    param(
        [string]$RelativePath,
        [string]$Source,
        [string]$ConnectivityExpectation
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return
    }

    $profilePath = Resolve-FoundryDataPath -RelativePath $RelativePath
    if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
        Write-FoundryLog "Skipping missing wired 802.1X profile: $profilePath"
        return
    }

    Write-FoundryLog "Wired 802.1X profile source='$Source', connectivityExpectation='$ConnectivityExpectation'."
    Invoke-FoundryNetsh -Arguments @('lan', 'add', 'profile', "filename=$profilePath") -Description 'Importing wired 802.1X profile.'
}

try {
    Start-FoundryTranscript
    Write-FoundryLog "Foundry network profile import started."

    if (-not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) {
        Write-FoundryLog "No network profile import settings were staged."
        return
    }

    $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    foreach ($certificate in @($settings.certificates)) {
        Import-FoundryCertificate -Certificate $certificate
    }

    Import-FoundryWiredProfile -RelativePath ([string]$settings.wiredDot1xProfileRelativePath) -Source ([string]$settings.wiredDot1xProfileSource) -ConnectivityExpectation ([string]$settings.wiredDot1xProfileConnectivityExpectation)
    Import-FoundryWifiProfile -RelativePath ([string]$settings.wifiProfileRelativePath) -Source ([string]$settings.wifiProfileSource) -ConnectivityExpectation ([string]$settings.wifiProfileConnectivityExpectation)

    Write-FoundryLog "Foundry network profile import completed."
}
finally {
    if (Test-Path -LiteralPath $NetworkDataDirectory -PathType Container) {
        Remove-Item -LiteralPath $NetworkDataDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }

    Stop-FoundryTranscript
}
