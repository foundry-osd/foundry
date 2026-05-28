param(
    [Parameter(Mandatory = $false)]
    [string]$ConfigPath = "$env:SystemRoot\Temp\Foundry\AutopilotRegistration\config.json"
)

$ErrorActionPreference = 'Stop'

function Expand-FoundryPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [Environment]::ExpandEnvironmentVariables($Path)
}

function Read-FoundryConfig {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Configuration file was not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

$Config = Read-FoundryConfig -Path $ConfigPath
$RegistrationRoot = Expand-FoundryPath -Path $Config.registrationRootPath
$LogRoot = Expand-FoundryPath -Path $Config.logRootPath
$StateRoot = Expand-FoundryPath -Path $Config.stateRootPath
$RegistrationLogPath = Join-Path $LogRoot 'registration.log'
$GraphLogPath = Join-Path $LogRoot 'graph.log'
$StatePath = Join-Path $StateRoot 'registration-state.json'
$ResultPath = Join-Path $StateRoot 'registration-result.json'

New-Item -Path $RegistrationRoot -ItemType Directory -Force | Out-Null
New-Item -Path $LogRoot -ItemType Directory -Force | Out-Null
New-Item -Path $StateRoot -ItemType Directory -Force | Out-Null

function Write-FoundryLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [Parameter(Mandatory = $false)][string]$Path = $RegistrationLogPath
    )

    $timestamp = [DateTimeOffset]::Now.ToString('o')
    Add-Content -LiteralPath $Path -Value "[$timestamp] $Message"
}

function Write-State {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$Message
    )

    [pscustomobject]@{
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        stage = $Stage
        message = $Message
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $StatePath -Encoding UTF8
}

function Write-Result {
    param(
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Message,
        [Parameter(Mandatory = $false)][object]$Details = $null
    )

    [pscustomobject]@{
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        status = $Status
        message = $Message
        details = $Details
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
}

function ConvertTo-FormBody {
    param([Parameter(Mandatory = $true)][hashtable]$Values)

    $pairs = foreach ($key in $Values.Keys) {
        '{0}={1}' -f [System.Net.WebUtility]::UrlEncode($key), [System.Net.WebUtility]::UrlEncode([string]$Values[$key])
    }

    return ($pairs -join '&')
}

function Get-ErrorResponseText {
    param([Parameter(Mandatory = $true)]$ErrorRecord)

    try {
        $response = $ErrorRecord.Exception.Response
        if ($null -eq $response) {
            return $ErrorRecord.Exception.Message
        }

        $stream = $response.GetResponseStream()
        if ($null -eq $stream) {
            return $ErrorRecord.Exception.Message
        }

        $reader = New-Object System.IO.StreamReader($stream)
        return $reader.ReadToEnd()
    }
    catch {
        return $ErrorRecord.Exception.Message
    }
}

function Invoke-DeviceCodeAuthentication {
    param([Parameter(Mandatory = $true)]$Config)

    $tenant = if ([string]::IsNullOrWhiteSpace($Config.tenant)) { 'organizations' } else { [string]$Config.tenant }
    $scope = ($Config.scopes | ForEach-Object { [string]$_ }) -join ' '
    $deviceCodeUri = "https://login.microsoftonline.com/$tenant/oauth2/v2.0/devicecode"
    $tokenUri = "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token"

    Write-State -Stage 'authentication' -Message 'Requesting Microsoft device code.'
    $deviceCodeBody = ConvertTo-FormBody -Values @{
        client_id = [string]$Config.clientId
        scope = $scope
    }

    $deviceCode = Invoke-RestMethod -Method Post -Uri $deviceCodeUri -Body $deviceCodeBody -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing

    Write-Host ''
    Write-Host 'Microsoft device code sign-in is required.'
    Write-Host $deviceCode.message
    Write-Host ''
    Write-FoundryLog -Message 'Device code authentication prompt displayed. Device code value was not logged.'

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds([int]$deviceCode.expires_in)
    $interval = [Math]::Max(5, [int]$deviceCode.interval)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds $interval

        $tokenBody = ConvertTo-FormBody -Values @{
            grant_type = 'urn:ietf:params:oauth:grant-type:device_code'
            client_id = [string]$Config.clientId
            device_code = [string]$deviceCode.device_code
        }

        try {
            $token = Invoke-RestMethod -Method Post -Uri $tokenUri -Body $tokenBody -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing
            Write-FoundryLog -Message 'Device code authentication completed.'
            return [string]$token.access_token
        }
        catch {
            $errorText = Get-ErrorResponseText -ErrorRecord $_
            if ($errorText -match 'authorization_pending') {
                continue
            }

            if ($errorText -match 'slow_down') {
                $interval += 5
                continue
            }

            throw "Device code authentication failed. $errorText"
        }
    }

    throw 'Device code authentication timed out.'
}

function Invoke-GraphRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $false)]$Body = $null
    )

    $baseUri = ([string]$Config.graphBaseUri).TrimEnd('/')
    $uri = if ($Path.StartsWith('http', [StringComparison]::OrdinalIgnoreCase)) { $Path } else { "$baseUri/$($Path.TrimStart('/'))" }
    $headers = @{
        Authorization = "Bearer $AccessToken"
        Accept = 'application/json'
    }

    try {
        if ($null -eq $Body) {
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -UseBasicParsing
        }

        $json = $Body | ConvertTo-Json -Depth 10
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType 'application/json' -Body $json -UseBasicParsing
    }
    catch {
        $errorText = Get-ErrorResponseText -ErrorRecord $_
        Write-FoundryLog -Path $GraphLogPath -Message "Graph $Method $Path failed. $errorText"
        throw "Microsoft Graph request failed: $errorText"
    }
}

function Get-AutopilotHardwareIdentity {
    Write-State -Stage 'hardware' -Message 'Collecting Autopilot hardware identity.'

    $bios = Get-CimInstance -ClassName Win32_BIOS
    $serialNumber = [string]$bios.SerialNumber
    if ([string]::IsNullOrWhiteSpace($serialNumber)) {
        throw 'The device serial number could not be read.'
    }

    $detail = Get-CimInstance -Namespace 'root/cimv2/mdm/dmmap' -ClassName 'MDM_DevDetail_Ext01' -Filter "InstanceID='Ext' AND ParentID='./DevDetail'"
    $hardwareHash = [string]$detail.DeviceHardwareData
    if ([string]::IsNullOrWhiteSpace($hardwareHash)) {
        throw 'The Autopilot hardware hash could not be read.'
    }

    return [pscustomobject]@{
        SerialNumber = $serialNumber.Trim()
        HardwareHash = $hardwareHash.Trim()
    }
}

function Get-ExistingGroupTags {
    param([Parameter(Mandatory = $true)][string]$AccessToken)

    $tags = New-Object System.Collections.Generic.List[string]
    $path = 'deviceManagement/windowsAutopilotDeviceIdentities?$select=groupTag&$top=100'

    try {
        while (-not [string]::IsNullOrWhiteSpace($path)) {
            $response = Invoke-GraphRequest -Method Get -Path $path -AccessToken $AccessToken
            foreach ($device in $response.value) {
                if (-not [string]::IsNullOrWhiteSpace($device.groupTag) -and -not $tags.Contains([string]$device.groupTag)) {
                    $tags.Add([string]$device.groupTag)
                }
            }

            $path = $response.'@odata.nextLink'
        }
    }
    catch {
        Write-FoundryLog -Message "Group tag discovery failed. Manual entry remains available. $($_.Exception.Message)"
    }

    return $tags | Sort-Object
}

function Select-GroupTag {
    param([Parameter(Mandatory = $true)][string[]]$AvailableGroupTags)

    Write-Host ''
    Write-Host 'Select an Autopilot group tag.'
    Write-Host '0. None'
    for ($index = 0; $index -lt $AvailableGroupTags.Count; $index++) {
        Write-Host ("{0}. {1}" -f ($index + 1), $AvailableGroupTags[$index])
    }

    Write-Host 'M. Enter a group tag manually'
    $choice = Read-Host 'Choice'

    if ([string]::IsNullOrWhiteSpace($choice) -or $choice -eq '0') {
        return $null
    }

    if ($choice -match '^[mM]$') {
        $manual = Read-Host 'Group tag'
        if ([string]::IsNullOrWhiteSpace($manual)) {
            return $null
        }

        return $manual.Trim()
    }

    $number = 0
    if ([int]::TryParse($choice, [ref]$number) -and $number -ge 1 -and $number -le $AvailableGroupTags.Count) {
        return $AvailableGroupTags[$number - 1]
    }

    throw 'Invalid group tag selection.'
}

function Import-AutopilotDeviceIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $false)][string]$GroupTag
    )

    $importId = [Guid]::NewGuid().ToString('D')
    $body = @{
        '@odata.type' = '#microsoft.graph.importedWindowsAutopilotDeviceIdentity'
        serialNumber = $Identity.SerialNumber
        productKey = ''
        importId = $importId
        hardwareIdentifier = $Identity.HardwareHash
        state = @{
            '@odata.type' = '#microsoft.graph.importedWindowsAutopilotDeviceIdentityState'
            deviceImportStatus = 'pending'
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($GroupTag)) {
        $body['groupTag'] = $GroupTag
    }

    Write-State -Stage 'import' -Message 'Importing device identity into Windows Autopilot.'
    $response = Invoke-GraphRequest -Method Post -Path 'deviceManagement/importedWindowsAutopilotDeviceIdentities' -AccessToken $AccessToken -Body $body
    $importedIdentityId = [string]$response.id

    return [pscustomobject]@{
        ImportId = $importId
        ImportedIdentityId = $importedIdentityId
    }
}

function Wait-AutopilotImport {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)][string]$ImportedIdentityId
    )

    $timeoutSeconds = if ($Config.importPollingTimeoutSeconds) { [int]$Config.importPollingTimeoutSeconds } else { 900 }
    $intervalSeconds = if ($Config.importPollingIntervalSeconds) { [int]$Config.importPollingIntervalSeconds } else { 15 }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($timeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $response = Invoke-GraphRequest -Method Get -Path "deviceManagement/importedWindowsAutopilotDeviceIdentities/$ImportedIdentityId" -AccessToken $AccessToken
        $status = [string]$response.state.deviceImportStatus
        Write-FoundryLog -Path $GraphLogPath -Message "Import state: $status"

        if ($status -match 'complete') {
            return $response
        }

        if ($status -match 'error') {
            $errorName = [string]$response.state.deviceErrorName
            throw "Autopilot import failed. $errorName"
        }

        Start-Sleep -Seconds $intervalSeconds
    }

    throw 'Timed out while waiting for Autopilot import completion.'
}

try {
    Write-FoundryLog -Message 'Starting Foundry Autopilot registration assistant.'
    $accessToken = Invoke-DeviceCodeAuthentication -Config $Config
    $identity = Get-AutopilotHardwareIdentity
    $groupTags = @(Get-ExistingGroupTags -AccessToken $accessToken)
    $selectedGroupTag = Select-GroupTag -AvailableGroupTags $groupTags
    $import = Import-AutopilotDeviceIdentity -AccessToken $accessToken -Identity $identity -GroupTag $selectedGroupTag
    $completedImport = Wait-AutopilotImport -AccessToken $accessToken -ImportedIdentityId $import.ImportedIdentityId

    $details = [pscustomobject]@{
        serialNumber = $identity.SerialNumber
        groupTag = $selectedGroupTag
        importId = $import.ImportId
        importedIdentityId = $import.ImportedIdentityId
        deviceImportStatus = $completedImport.state.deviceImportStatus
    }
    Write-Result -Status 'completed' -Message 'Autopilot registration completed.' -Details $details
    Write-FoundryLog -Message 'Autopilot registration completed.'
    Write-Host ''
    Write-Host 'Autopilot registration completed.'
    exit 0
}
catch {
    $message = $_.Exception.Message
    Write-Result -Status 'failed' -Message $message
    Write-FoundryLog -Message "Autopilot registration failed. $message"
    Write-Error $message
    exit 1
}
