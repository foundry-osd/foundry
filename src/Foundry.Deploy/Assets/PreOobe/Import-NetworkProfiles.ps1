$ErrorActionPreference = 'Stop'
if (-not (Get-Command Invoke-FoundryInstaller -ErrorAction SilentlyContinue)) {
    . (Join-Path (Split-Path -Parent $PSScriptRoot) 'Foundry-PreOobeFunctions.ps1')
}
$DataDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'Data'

function Resolve-FoundryDataPath {
    param([string]$RelativePath)
    return Resolve-FoundryOwnedPath -Root $DataDirectory -RelativePath $RelativePath
}

function Invoke-FoundryNetsh {
    param([string[]]$Arguments, [string]$Description)
    $code = Invoke-FoundryInstaller -FilePath (Join-Path $env:SystemRoot 'System32\netsh.exe') -Arguments $Arguments
    if ($code -ne 0) { throw 'network_profile_import_failed' }
}

function Import-FoundryCertificate {
    param($Certificate)
    $path = Resolve-FoundryDataPath ([string]$Certificate.relativePath)
    if (-not [IO.File]::Exists($path)) { throw 'certificate_input_missing' }
    if ($Certificate.kind -ieq 'pfx') {
        $password = $null
        try {
            if ($Certificate.passwordRelativePath) {
                $passwordPath = Resolve-FoundryDataPath ([string]$Certificate.passwordRelativePath)
                if (-not [IO.File]::Exists($passwordPath)) { throw 'secret_reentry_required' }
                $password = ConvertTo-SecureString -String ([IO.File]::ReadAllText($passwordPath)) -AsPlainText -Force
            }
            $arguments = @{FilePath=$path;CertStoreLocation='Cert:\LocalMachine\My';Exportable=$false;ErrorAction='Stop'}
            if ($null -ne $password) { $arguments.Password=$password }
            Import-PfxCertificate @arguments | Out-Null
        }
        catch { throw 'certificate_import_failed' }
        finally { if ($null -ne $password) { $password.Dispose() } }
    }
    else {
        $store = [string]$Certificate.storeName
        if ($store -notin @('Root','CA','My','TrustedPublisher')) { throw 'unsupported_certificate_store' }
        $code = Invoke-FoundryInstaller -FilePath (Join-Path $env:SystemRoot 'System32\certutil.exe') -Arguments @('-addstore','-f',$store,$path)
        if ($code -ne 0) { throw 'certificate_import_failed' }
    }
}

function Get-FoundryWifiProfileName {
    param([string]$ProfilePath)
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $document.PreserveWhitespace = $true
    $document.Load($ProfilePath)
    $manager = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $manager.AddNamespace('wlan','http://www.microsoft.com/networking/WLAN/profile/v1')
    $name = $document.SelectSingleNode('/wlan:WLANProfile/wlan:name',$manager)
    if ($null -eq $name -or $name.InnerText.Length -eq 0) { throw 'invalid_wifi_profile' }
    return $name.InnerText
}

function Set-FoundryWifiProfileConnectionMode {
    param([string]$ProfilePath, [string]$ConnectivityExpectation)
    if ($ConnectivityExpectation -ine 'preOobeConnectable') { return }
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $document.PreserveWhitespace = $true
    $document.Load($ProfilePath)
    $manager = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $manager.AddNamespace('wlan','http://www.microsoft.com/networking/WLAN/profile/v1')
    $mode = $document.SelectSingleNode('/wlan:WLANProfile/wlan:connectionMode',$manager)
    if ($null -eq $mode) {
        $connection = $document.SelectSingleNode('/wlan:WLANProfile/wlan:connectionType',$manager)
        if ($null -eq $connection) { throw 'invalid_wifi_profile' }
        $mode = $document.CreateElement('connectionMode','http://www.microsoft.com/networking/WLAN/profile/v1')
        [void]$document.DocumentElement.InsertAfter($mode,$connection)
    }
    $mode.InnerText='auto'
    $document.Save($ProfilePath)
}

function Import-FoundryWifiProfile {
    param([string]$RelativePath, [string]$ConnectivityExpectation)
    if ([string]::IsNullOrEmpty($RelativePath)) { return }
    $path = Resolve-FoundryDataPath $RelativePath
    if (-not [IO.File]::Exists($path)) { throw 'network_profile_input_missing' }
    Set-FoundryWifiProfileConnectionMode $path $ConnectivityExpectation
    Invoke-FoundryNetsh @('wlan','add','profile',"filename=$path",'user=all')
    if ($ConnectivityExpectation -ieq 'preOobeConnectable') {
        $name = Get-FoundryWifiProfileName $path
        Invoke-FoundryNetsh @('wlan','connect',"name=$name")
    }
}

function Import-FoundryWiredProfile {
    param([string]$RelativePath)
    if ([string]::IsNullOrEmpty($RelativePath)) { return }
    $path = Resolve-FoundryDataPath $RelativePath
    if (-not [IO.File]::Exists($path)) { throw 'network_profile_input_missing' }
    Invoke-FoundryNetsh @('lan','add','profile',"filename=$path")
}

try {
    $settingsPath = Resolve-FoundryDataPath 'NetworkProfiles\import-settings.json'
    if (-not [IO.File]::Exists($settingsPath)) { throw 'network_settings_missing' }
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    foreach ($certificate in @($settings.certificates)) { Import-FoundryCertificate $certificate }
    Import-FoundryWiredProfile ([string]$settings.wiredDot1xProfileRelativePath)
    Import-FoundryWifiProfile ([string]$settings.wifiProfileRelativePath) ([string]$settings.wifiProfileConnectivityExpectation)
}
finally {
    $root = Split-Path -Parent $PSScriptRoot
    $manifest = Get-Content -LiteralPath (Join-Path $root 'pre-oobe-manifest.json') -Raw | ConvertFrom-Json
    $action = @($manifest.scripts | Where-Object id -eq 'network-profile-roaming')
    if ($action.Count -ne 1) { throw 'invalid_network_action' }
    Remove-FoundryActionInputs -Action $action[0] -Root $root
}
