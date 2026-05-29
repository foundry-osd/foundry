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

function Test-RegistrationAlreadyCompleted {
    if (-not (Test-Path -LiteralPath $ResultPath)) {
        return $false
    }

    try {
        $result = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
        return $result.status -eq 'completed'
    }
    catch {
        Write-FoundryLog -Message "Failed to read existing registration result. $($_.Exception.Message)"
        return $false
    }
}

if (Test-RegistrationAlreadyCompleted) {
    Write-FoundryLog -Message 'Autopilot registration is already completed.'
    exit 0
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

    if (-not [string]::IsNullOrWhiteSpace($ErrorRecord.ErrorDetails.Message)) {
        return [string]$ErrorRecord.ErrorDetails.Message
    }

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
        $responseText = $reader.ReadToEnd()
        if (-not [string]::IsNullOrWhiteSpace($responseText)) {
            return $responseText
        }

        return $ErrorRecord.Exception.Message
    }
    catch {
        return $ErrorRecord.Exception.Message
    }
}

function Request-DeviceCode {
    param([Parameter(Mandatory = $true)]$Config)

    $tenant = if ([string]::IsNullOrWhiteSpace($Config.tenant)) { 'common' } else { [string]$Config.tenant }
    $scope = ($Config.scopes | ForEach-Object { [string]$_ }) -join ' '
    $deviceCodeUri = "https://login.microsoftonline.com/$tenant/oauth2/v2.0/devicecode"
    $deviceCodeBody = ConvertTo-FormBody -Values @{
        client_id = [string]$Config.clientId
        scope = $scope
    }

    Write-State -Stage 'authentication' -Message 'Requesting Microsoft device code.'
    $deviceCode = Invoke-RestMethod -Method Post -Uri $deviceCodeUri -Body $deviceCodeBody -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing
    Write-FoundryLog -Message 'Device code authentication prompt displayed. Device code value was not logged.'
    return $deviceCode
}

function Request-DeviceCodeToken {
    param(
        [Parameter(Mandatory = $true)]$Config,
        [Parameter(Mandatory = $true)]$DeviceCode
    )

    $tenant = if ([string]::IsNullOrWhiteSpace($Config.tenant)) { 'common' } else { [string]$Config.tenant }
    $tokenUri = "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token"
    $tokenBody = ConvertTo-FormBody -Values @{
        grant_type = 'urn:ietf:params:oauth:grant-type:device_code'
        client_id = [string]$Config.clientId
        device_code = [string]$DeviceCode.device_code
    }

    try {
        $token = Invoke-RestMethod -Method Post -Uri $tokenUri -Body $tokenBody -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing
        Write-FoundryLog -Message 'Device code authentication completed.'
        return [pscustomobject]@{
            Status = 'Success'
            AccessToken = [string]$token.access_token
        }
    }
    catch {
        $errorText = Get-ErrorResponseText -ErrorRecord $_
        if ($errorText -match 'authorization_pending') {
            return [pscustomobject]@{
                Status = 'Pending'
                AccessToken = $null
            }
        }

        if ($errorText -match 'slow_down') {
            return [pscustomobject]@{
                Status = 'SlowDown'
                AccessToken = $null
            }
        }

        $response = $_.Exception.Response
        if ($null -ne $response -and [int]$response.StatusCode -eq 400 -and
            ([string]::IsNullOrWhiteSpace($errorText) -or $errorText -match 'Bad Request|\(400\)')) {
            return [pscustomobject]@{
                Status = 'Pending'
                AccessToken = $null
            }
        }

        throw "Device code authentication failed. $errorText"
    }
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
    $path = 'deviceManagement/windowsAutopilotDeviceIdentities'

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

function Import-AutopilotDeviceIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $false)][string]$GroupTag
    )

    $importId = [Guid]::NewGuid().ToString('D')
    $device = @{
        '@odata.type' = '#microsoft.graph.importedWindowsAutopilotDeviceIdentity'
        serialNumber = $Identity.SerialNumber
        importId = $importId
        hardwareIdentifier = $Identity.HardwareHash
    }

    if (-not [string]::IsNullOrWhiteSpace($GroupTag)) {
        $device['groupTag'] = $GroupTag
    }

    $body = @{
        importedWindowsAutopilotDeviceIdentities = @($device)
    }

    Write-State -Stage 'import' -Message 'Uploading device hardware hash to Microsoft Intune.'
    $response = Invoke-GraphRequest -Method Post -Path 'deviceManagement/importedWindowsAutopilotDeviceIdentities/import' -AccessToken $AccessToken -Body $body
    $importedIdentity = @($response.value)[0]
    if ($null -eq $importedIdentity) {
        throw 'Microsoft Graph did not return an imported Autopilot device identity.'
    }

    return [pscustomobject]@{
        ImportId = $importId
        ImportedIdentityId = [string]$importedIdentity.id
        ImportedIdentity = $importedIdentity
    }
}

function Get-ImportedAutopilotIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)]$Import,
        [Parameter(Mandatory = $true)][string]$SerialNumber
    )

    $filter = [Uri]::EscapeDataString(("importId eq '{0}'" -f ([string]$Import.ImportId).Replace("'", "''")))
    $path = 'deviceManagement/importedWindowsAutopilotDeviceIdentities?$filter={0}' -f $filter

    while (-not [string]::IsNullOrWhiteSpace($path)) {
        $response = Invoke-GraphRequest -Method Get -Path $path -AccessToken $AccessToken
        foreach ($identity in $response.value) {
            if ([string]$identity.importId -eq [string]$Import.ImportId -or [string]$identity.serialNumber -eq $SerialNumber) {
                return $identity
            }
        }

        $path = $response.'@odata.nextLink'
    }

    return $null
}

function Find-AutopilotDeviceBySerialNumber {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)][string]$SerialNumber
    )

    $matches = New-Object System.Collections.Generic.List[object]
    $path = 'deviceManagement/windowsAutopilotDeviceIdentities?$top=100'

    while (-not [string]::IsNullOrWhiteSpace($path)) {
        $response = Invoke-GraphRequest -Method Get -Path $path -AccessToken $AccessToken
        foreach ($device in $response.value) {
            if ([string]$device.serialNumber -eq $SerialNumber) {
                $matches.Add($device)
            }
        }

        $path = $response.'@odata.nextLink'
    }

    if ($matches.Count -eq 0) {
        return $null
    }

    if ($matches.Count -gt 1) {
        throw 'Multiple Windows Autopilot devices matched the captured serial number; group tag reconciliation was skipped to avoid updating the wrong device.'
    }

    return $matches[0]
}

function Normalize-GroupTag {
    param([Parameter(Mandatory = $false)][string]$GroupTag)

    if ([string]::IsNullOrWhiteSpace($GroupTag)) {
        return ''
    }

    return $GroupTag.Trim()
}

function Should-UpdateGroupTag {
    param(
        [Parameter(Mandatory = $false)][string]$CurrentGroupTag,
        [Parameter(Mandatory = $false)][string]$RequestedGroupTag
    )

    return -not [string]::Equals(
        (Normalize-GroupTag -GroupTag $CurrentGroupTag),
        (Normalize-GroupTag -GroupTag $RequestedGroupTag),
        [StringComparison]::OrdinalIgnoreCase)
}

function Should-ContinueVisibilityWaitAfterImportError {
    param([Parameter(Mandatory = $false)]$State)

    $errorName = [string]$State.deviceErrorName
    return $errorName.IndexOf('AlreadyAssigned', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $errorName.IndexOf('AlreadyExists', [StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Update-AutopilotDeviceGroupTag {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)]$Device,
        [Parameter(Mandatory = $false)][string]$GroupTag
    )

    $deviceId = [string]$Device.id
    if ([string]::IsNullOrWhiteSpace($deviceId)) {
        throw 'Microsoft Graph returned an existing Autopilot device without an ID.'
    }

    $body = @{
        groupTag = Normalize-GroupTag -GroupTag $GroupTag
    }
    Invoke-GraphRequest -Method Post -Path "deviceManagement/windowsAutopilotDeviceIdentities/$deviceId/updateDeviceProperties" -AccessToken $AccessToken -Body $body | Out-Null
}

function Wait-AutopilotDeviceGroupTag {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)][string]$SerialNumber,
        [Parameter(Mandatory = $false)][string]$GroupTag
    )

    $timeoutSeconds = if ($Config.importPollingTimeoutSeconds) { [int]$Config.importPollingTimeoutSeconds } else { 900 }
    $intervalSeconds = if ($Config.importPollingIntervalSeconds) { [int]$Config.importPollingIntervalSeconds } else { 15 }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($timeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $device = Find-AutopilotDeviceBySerialNumber -AccessToken $AccessToken -SerialNumber $SerialNumber
        if ($null -ne $device -and -not (Should-UpdateGroupTag -CurrentGroupTag ([string]$device.groupTag) -RequestedGroupTag $GroupTag)) {
            return $device
        }

        Start-Sleep -Seconds $intervalSeconds
    }

    throw 'Timed out while waiting for Windows Autopilot group tag update.'
}

function Test-AutopilotDeviceReadiness {
    param(
        [Parameter(Mandatory = $true)][string]$AccessToken,
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $true)]$Import,
        [Parameter(Mandatory = $true)][ref]$ImportedIdentity,
        [Parameter(Mandatory = $true)][DateTimeOffset]$Deadline,
        [Parameter(Mandatory = $false)][string]$GroupTag
    )

    $visibleDevice = Find-AutopilotDeviceBySerialNumber -AccessToken $AccessToken -SerialNumber $Identity.SerialNumber
    if ($null -ne $visibleDevice) {
        if (Should-UpdateGroupTag -CurrentGroupTag ([string]$visibleDevice.groupTag) -RequestedGroupTag $GroupTag) {
            Write-State -Stage 'groupTag' -Message 'Updating Windows Autopilot group tag.'
            Write-FoundryLog -Path $GraphLogPath -Message 'Updating Windows Autopilot group tag.'
            Update-AutopilotDeviceGroupTag -AccessToken $AccessToken -Device $visibleDevice -GroupTag $GroupTag
            $visibleDevice = Wait-AutopilotDeviceGroupTag -AccessToken $AccessToken -SerialNumber $Identity.SerialNumber -GroupTag $GroupTag
        }

        return [pscustomobject]@{
            Status = 'Completed'
            ImportedIdentity = $ImportedIdentity.Value
            AutopilotDevice = $visibleDevice
        }
    }

    $status = [string]$ImportedIdentity.Value.state.deviceImportStatus
    Write-FoundryLog -Path $GraphLogPath -Message "Import state: $status"
    if ($status -eq 'error' -and -not (Should-ContinueVisibilityWaitAfterImportError -State $ImportedIdentity.Value.state)) {
        $errorName = [string]$ImportedIdentity.Value.state.deviceErrorName
        throw "Autopilot import failed. $errorName"
    }

    if ($status -ne 'complete' -and $status -ne 'error') {
        $currentImport = Get-ImportedAutopilotIdentity -AccessToken $AccessToken -Import $Import -SerialNumber $Identity.SerialNumber
        if ($null -ne $currentImport) {
            $ImportedIdentity.Value = $currentImport
        }
    }

    if ([DateTimeOffset]::UtcNow -ge $Deadline) {
        throw 'Imported Autopilot device did not appear in Windows Autopilot devices before the timeout.'
    }

    return [pscustomobject]@{
        Status = 'Pending'
        ImportedIdentity = $ImportedIdentity.Value
        AutopilotDevice = $null
    }
}

function Get-SelectedGroupTag {
    param(
        [Parameter(Mandatory = $true)]$GroupTagCombo,
        [Parameter(Mandatory = $true)]$CustomGroupTagTextBox
    )

    $selected = [string]$GroupTagCombo.SelectedItem
    if ([string]::IsNullOrWhiteSpace($selected) -or $selected -eq 'None') {
        return $null
    }

    if ($selected -eq 'Custom') {
        $custom = [string]$CustomGroupTagTextBox.Text
        if ([string]::IsNullOrWhiteSpace($custom)) {
            return $null
        }

        return $custom.Trim()
    }

    return $selected
}

function Start-FoundryAutopilotRegistrationUi {
    Add-Type -AssemblyName PresentationFramework
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase

    $foundryLogoBase64 = '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'

    $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Foundry OSD - Interactive hardware hash upload"
        Width="420"
        Height="560"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True">
    <Grid Margin="12">
        <Grid x:Name="AuthenticationStep">
            <StackPanel Orientation="Horizontal"
                        VerticalAlignment="Top"
                        HorizontalAlignment="Center"
                        Margin="0,0,0,12">
                <Image x:Name="AuthenticationLogoImage"
                       Width="50"
                       Height="50"
                       Margin="0,0,12,0"
                       Stretch="Uniform"
                       RenderOptions.BitmapScalingMode="HighQuality"
                       SnapsToDevicePixels="True"
                       VerticalAlignment="Center" />
                <TextBlock Text="Foundry OSD - Sign in to Microsoft"
                           FontSize="16"
                           FontWeight="SemiBold"
                           VerticalAlignment="Center"
                           TextWrapping="Wrap" />
            </StackPanel>

            <StackPanel VerticalAlignment="Center"
                        HorizontalAlignment="Center"
                        Width="330">
                <TextBlock x:Name="AuthenticationInstructionTextBlock"
                           TextAlignment="Center"
                           TextWrapping="Wrap">
                    <Run Text="Go to" />
                    <Run Text=" " />
                    <Run Text="https://microsoft.com/devicelogin" FontWeight="Bold" />
                    <Run Text=" " />
                    <Run Text="in a browser and enter this code:" />
                </TextBlock>

                <TextBlock x:Name="DeviceCodeTextBlock"
                           TextAlignment="Center"
                           FontSize="32"
                           FontWeight="Bold"
                           Margin="0,8,0,0" />
            </StackPanel>

            <TextBlock x:Name="AuthenticationStatusTextBlock"
                       TextAlignment="Center"
                       TextWrapping="Wrap"
                       Width="330"
                       HorizontalAlignment="Center"
                       VerticalAlignment="Bottom"
                       Margin="0,0,0,24" />

            <ProgressBar x:Name="AuthenticationProgressBar"
                         Minimum="0"
                         Maximum="100"
                         Value="0"
                         Height="4"
                         Width="330"
                         HorizontalAlignment="Center"
                         VerticalAlignment="Bottom"
                         Margin="0,0,0,12" />
        </Grid>

        <Grid x:Name="UploadStep" Visibility="Collapsed">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <StackPanel Grid.Row="0"
                        Orientation="Horizontal"
                        VerticalAlignment="Top"
                        HorizontalAlignment="Center"
                        Margin="0,0,0,12">
                <Image x:Name="UploadLogoImage"
                       Width="50"
                       Height="50"
                       Margin="0,0,12,0"
                       Stretch="Uniform"
                       RenderOptions.BitmapScalingMode="HighQuality"
                       SnapsToDevicePixels="True"
                       VerticalAlignment="Center" />
                <TextBlock Text="Foundry OSD - Upload hardware hash"
                           FontSize="16"
                           FontWeight="SemiBold"
                           VerticalAlignment="Center"
                           TextWrapping="Wrap" />
            </StackPanel>

            <StackPanel Grid.Row="1"
                        VerticalAlignment="Center"
                        HorizontalAlignment="Center"
                        Width="330">
                <TextBlock Text="Choose a group tag, then upload this device hardware hash to Microsoft Intune."
                           TextAlignment="Center"
                           TextWrapping="Wrap"
                           Margin="0,0,0,18" />

                <TextBlock Text="Group tag"
                           Margin="0,0,0,4" />

                <ComboBox x:Name="GroupTagCombo" />

                <TextBlock Text="Custom group tag"
                           Margin="0,12,0,4" />

                <TextBox x:Name="CustomGroupTagTextBox"
                         IsEnabled="False" />
            </StackPanel>

            <StackPanel Grid.Row="2"
                        VerticalAlignment="Bottom"
                        HorizontalAlignment="Center"
                        Width="330"
                        Margin="0,12,0,0">
                <TextBlock x:Name="UploadStatusTextBlock"
                           Text="Ready to upload."
                           TextAlignment="Center"
                           TextWrapping="Wrap"
                           Margin="0,0,0,8" />

                <ProgressBar x:Name="UploadProgressBar"
                             Minimum="0"
                             Maximum="100"
                             Value="0"
                             Height="4"
                             Margin="0,0,0,12" />

                <Button x:Name="UploadButton"
                        Content="Upload"
                        HorizontalAlignment="Center"
                        MinWidth="140"
                        MinHeight="32" />
            </StackPanel>
        </Grid>
    </Grid>
</Window>
'@

    $xmlReader = New-Object System.Xml.XmlNodeReader ([xml]$xaml)
    $window = [Windows.Markup.XamlReader]::Load($xmlReader)

    function New-FoundryBitmapImageFromBase64 {
        param([Parameter(Mandatory = $true)][string]$Base64)

        $bytes = [Convert]::FromBase64String($Base64)
        $stream = New-Object System.IO.MemoryStream(,$bytes)
        try {
            $image = New-Object System.Windows.Media.Imaging.BitmapImage
            $image.BeginInit()
            $image.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
            $image.StreamSource = $stream
            $image.EndInit()
            $image.Freeze()
            return $image
        }
        finally {
            $stream.Dispose()
        }
    }

    $authenticationStep = $window.FindName('AuthenticationStep')
    $uploadStep = $window.FindName('UploadStep')
    $authenticationLogoImage = $window.FindName('AuthenticationLogoImage')
    $uploadLogoImage = $window.FindName('UploadLogoImage')
    $authenticationInstructionTextBlock = $window.FindName('AuthenticationInstructionTextBlock')
    $authenticationProgressBar = $window.FindName('AuthenticationProgressBar')
    $authenticationStatusTextBlock = $window.FindName('AuthenticationStatusTextBlock')
    $deviceCodeTextBlock = $window.FindName('DeviceCodeTextBlock')
    $groupTagCombo = $window.FindName('GroupTagCombo')
    $customGroupTagTextBox = $window.FindName('CustomGroupTagTextBox')
    $uploadStatusTextBlock = $window.FindName('UploadStatusTextBlock')
    $uploadProgressBar = $window.FindName('UploadProgressBar')
    $uploadButton = $window.FindName('UploadButton')

    $logoImage = New-FoundryBitmapImageFromBase64 -Base64 $foundryLogoBase64
    $window.Icon = $logoImage
    $authenticationLogoImage.Source = $logoImage
    $uploadLogoImage.Source = $logoImage

    $script:AccessToken = $null
    $script:DiscoveredGroupTags = @()
    $script:ExitCode = 1
    $script:AuthenticationStarted = $false
    $script:AuthenticationPollTimer = $null
    $script:UploadPollTimer = $null

    function Set-AuthenticationError {
        param([Parameter(Mandatory = $true)][string]$Message)
        $authenticationInstructionTextBlock.Inlines.Clear()
        [void]$authenticationInstructionTextBlock.Inlines.Add((New-Object System.Windows.Documents.Run($Message)))
        $authenticationProgressBar.IsIndeterminate = $false
        $authenticationProgressBar.Value = 0
        $authenticationStatusTextBlock.Text = ''
    }

    function Set-UploadProgress {
        param(
            [Parameter(Mandatory = $true)][string]$Message,
            [Parameter(Mandatory = $false)][int]$Value = 0,
            [Parameter(Mandatory = $false)][bool]$IsIndeterminate = $false
        )

        $uploadStatusTextBlock.Text = $Message
        $uploadProgressBar.IsIndeterminate = $IsIndeterminate
        if (-not $IsIndeterminate) {
            $uploadProgressBar.Value = $Value
        }
        Write-FoundryLog -Message $Message
    }

    function Update-AuthenticationCountdown {
        if ($null -eq $script:AuthenticationDeadline -or $null -eq $script:AuthenticationStartedAt) {
            return
        }

        $remainingSeconds = [Math]::Max(0, [int][Math]::Ceiling(($script:AuthenticationDeadline - [DateTimeOffset]::UtcNow).TotalSeconds))
        $totalSeconds = [Math]::Max(1, [int][Math]::Ceiling(($script:AuthenticationDeadline - $script:AuthenticationStartedAt).TotalSeconds))
        $authenticationStatusTextBlock.Text = 'Code expires in {0} seconds.' -f $remainingSeconds
        $authenticationProgressBar.IsIndeterminate = $false
        $authenticationProgressBar.Value = [Math]::Max(0, [Math]::Min(100, ($remainingSeconds / $totalSeconds) * 100))
    }

    function Set-UploadControlsEnabled {
        param([Parameter(Mandatory = $true)][bool]$IsEnabled)

        $groupTagCombo.IsEnabled = $IsEnabled
        $customGroupTagTextBox.IsEnabled = $IsEnabled -and ([string]$groupTagCombo.SelectedItem -eq 'Custom')
        $uploadButton.IsEnabled = $IsEnabled
    }

    function Start-RebootCountdown {
        $script:RebootCountdownSeconds = 10
        Set-UploadProgress -Message ('Restarting in {0} seconds.' -f $script:RebootCountdownSeconds) -Value 0
        $script:RebootCountdownTimer = New-Object System.Windows.Threading.DispatcherTimer
        $script:RebootCountdownTimer.Interval = [TimeSpan]::FromSeconds(1)
        $script:RebootCountdownTimer.Add_Tick({
            $script:RebootCountdownSeconds--
            if ($script:RebootCountdownSeconds -le 0) {
                $script:RebootCountdownTimer.Stop()
                Set-UploadProgress -Message 'Restarting now.' -Value 100
                $script:RestartTimer = New-Object System.Windows.Threading.DispatcherTimer
                $script:RestartTimer.Interval = [TimeSpan]::FromSeconds(1)
                $script:RestartTimer.Add_Tick({
                    $script:RestartTimer.Stop()
                    Write-FoundryLog -Message 'Restarting device.'
                    Start-Process -FilePath "$env:SystemRoot\System32\shutdown.exe" -ArgumentList '/r /t 0 /f' -WindowStyle Hidden
                })
                $script:RestartTimer.Start()
                return
            }

            Set-UploadProgress -Message ('Restarting in {0} seconds.' -f $script:RebootCountdownSeconds) -Value ((10 - $script:RebootCountdownSeconds) * 10)
        })
        $script:RebootCountdownTimer.Start()
    }

    function Show-AuthenticationStep {
        $authenticationStep.Visibility = 'Visible'
        $uploadStep.Visibility = 'Collapsed'
        $deviceCodeTextBlock.Text = ''
        $authenticationStatusTextBlock.Text = ''
        $authenticationProgressBar.IsIndeterminate = $false
        $authenticationProgressBar.Value = 0
    }

    function Show-UploadStep {
        $authenticationStep.Visibility = 'Collapsed'
        $uploadStep.Visibility = 'Visible'
        $groupTagCombo.Items.Clear()
        [void]$groupTagCombo.Items.Add('None')
        foreach ($groupTag in $script:DiscoveredGroupTags) {
            [void]$groupTagCombo.Items.Add([string]$groupTag)
        }
        [void]$groupTagCombo.Items.Add('Custom')
        $groupTagCombo.SelectedIndex = 0
        $customGroupTagTextBox.Text = ''
        Set-UploadControlsEnabled -IsEnabled $true
        Set-UploadProgress -Message 'Ready to upload.' -Value 0 -IsIndeterminate $false
    }

    $groupTagCombo.Add_SelectionChanged({
        if (-not $groupTagCombo.IsEnabled) {
            return
        }

        if ([string]$groupTagCombo.SelectedItem -eq 'Custom') {
            $customGroupTagTextBox.IsEnabled = $true
            $customGroupTagTextBox.Focus() | Out-Null
        }
        else {
            $customGroupTagTextBox.IsEnabled = $false
            $customGroupTagTextBox.Text = ''
        }
    })

    function Start-AuthenticationFlow {
        try {
            Write-FoundryLog -Message 'Requesting Microsoft device code.'
            $script:AuthenticationDeviceCode = Request-DeviceCode -Config $Config
            $script:AuthenticationStartedAt = [DateTimeOffset]::UtcNow
            $script:AuthenticationDeadline = $script:AuthenticationStartedAt.AddSeconds([int]$script:AuthenticationDeviceCode.expires_in)
            $script:AuthenticationPollInterval = [Math]::Max(5, [int]$script:AuthenticationDeviceCode.interval)
            $deviceCodeTextBlock.Text = [string]$script:AuthenticationDeviceCode.user_code
            Write-FoundryLog -Message 'Device code was displayed to the technician.'
            Update-AuthenticationCountdown
            $script:NextAuthenticationPollAt = [DateTimeOffset]::UtcNow

            $script:AuthenticationPollTimer = New-Object System.Windows.Threading.DispatcherTimer
            $script:AuthenticationPollTimer.Interval = [TimeSpan]::FromSeconds(1)
            $script:AuthenticationPollTimer.Add_Tick({
                try {
                    Update-AuthenticationCountdown
                    if ([DateTimeOffset]::UtcNow -ge $script:AuthenticationDeadline) {
                        $script:AuthenticationPollTimer.Stop()
                        throw 'Device code authentication timed out.'
                    }

                    if ($script:NextAuthenticationPollAt -gt [DateTimeOffset]::UtcNow) {
                        return
                    }

                    $script:NextAuthenticationPollAt = [DateTimeOffset]::UtcNow.AddSeconds($script:AuthenticationPollInterval)
                    $tokenResult = Request-DeviceCodeToken -Config $Config -DeviceCode $script:AuthenticationDeviceCode
                    if ($tokenResult.Status -eq 'Pending') {
                        return
                    }

                    if ($tokenResult.Status -eq 'SlowDown') {
                        $script:AuthenticationPollInterval += 5
                        return
                    }

                    $script:AuthenticationPollTimer.Stop()
                    $script:AccessToken = [string]$tokenResult.AccessToken
                    Write-FoundryLog -Message 'Loading available group tags.'
                    $script:DiscoveredGroupTags = @(Get-ExistingGroupTags -AccessToken $script:AccessToken)
                    Show-UploadStep
                }
                catch {
                    $script:AuthenticationPollTimer.Stop()
                    $message = $_.Exception.Message
                    Write-Result -Status 'failed' -Message $message
                    Write-FoundryLog -Message $message
                    Set-AuthenticationError -Message 'Authentication failed. Check logs for details.'
                }
            })
            $script:AuthenticationPollTimer.Start()
        }
        catch {
            $message = $_.Exception.Message
            Write-Result -Status 'failed' -Message $message
            Write-FoundryLog -Message $message
            Set-AuthenticationError -Message 'Authentication failed. Check logs for details.'
        }
    }

    $uploadButton.Add_Click({
        try {
            Set-UploadControlsEnabled -IsEnabled $false
            $selectedGroupTag = Get-SelectedGroupTag -GroupTagCombo $groupTagCombo -CustomGroupTagTextBox $customGroupTagTextBox
            Set-UploadProgress -Message 'Collecting hardware hash.' -IsIndeterminate $true

            $script:UploadSelectedGroupTag = $selectedGroupTag
            $script:UploadIdentity = Get-AutopilotHardwareIdentity
            Set-UploadProgress -Message 'Uploading hardware hash to Microsoft Intune.' -IsIndeterminate $true
            $script:UploadImport = Import-AutopilotDeviceIdentity -AccessToken $script:AccessToken -Identity $script:UploadIdentity -GroupTag $script:UploadSelectedGroupTag
            $script:UploadImportedIdentity = $script:UploadImport.ImportedIdentity
            $timeoutSeconds = if ($Config.importPollingTimeoutSeconds) { [int]$Config.importPollingTimeoutSeconds } else { 900 }
            $intervalSeconds = if ($Config.importPollingIntervalSeconds) { [int]$Config.importPollingIntervalSeconds } else { 15 }
            $script:UploadDeadline = [DateTimeOffset]::UtcNow.AddSeconds($timeoutSeconds)
            Set-UploadProgress -Message 'Waiting for device registration in Microsoft Intune.' -IsIndeterminate $true

            $script:UploadPollTimer = New-Object System.Windows.Threading.DispatcherTimer
            $script:UploadPollTimer.Interval = [TimeSpan]::FromSeconds($intervalSeconds)
            $script:UploadPollTimer.Add_Tick({
                try {
                    $readiness = Test-AutopilotDeviceReadiness `
                        -AccessToken $script:AccessToken `
                        -Identity $script:UploadIdentity `
                        -Import $script:UploadImport `
                        -ImportedIdentity ([ref]$script:UploadImportedIdentity) `
                        -Deadline $script:UploadDeadline `
                        -GroupTag $script:UploadSelectedGroupTag

                    if ($readiness.Status -eq 'Pending') {
                        return
                    }

                    $script:UploadPollTimer.Stop()
                    $details = [pscustomobject]@{
                        serialNumber = $script:UploadIdentity.SerialNumber
                        groupTag = $script:UploadSelectedGroupTag
                        importId = $script:UploadImport.ImportId
                        importedIdentityId = $script:UploadImport.ImportedIdentityId
                        deviceImportStatus = $readiness.ImportedIdentity.state.deviceImportStatus
                        autopilotDeviceId = $readiness.AutopilotDevice.id
                    }
                    Write-Result -Status 'completed' -Message 'Autopilot registration completed.' -Details $details
                    Write-FoundryLog -Message 'Autopilot registration completed.'
                    Set-UploadProgress -Message 'Registration completed.' -Value 100 -IsIndeterminate $false
                    $script:ExitCode = 0
                    Start-RebootCountdown
                }
                catch {
                    $script:UploadPollTimer.Stop()
                    Set-UploadControlsEnabled -IsEnabled $true
                    $message = $_.Exception.Message
                    Write-Result -Status 'failed' -Message $message
                    Set-UploadProgress -Message 'Upload failed. Check logs for details.' -Value ([int]$uploadProgressBar.Value)
                    Write-FoundryLog -Message $message
                }
            })
            $script:UploadPollTimer.Start()
        }
        catch {
            Set-UploadControlsEnabled -IsEnabled $true
            $message = $_.Exception.Message
            Write-Result -Status 'failed' -Message $message
            Set-UploadProgress -Message 'Upload failed. Check logs for details.' -Value ([int]$uploadProgressBar.Value)
            Write-FoundryLog -Message $message
        }
    })

    $window.Add_ContentRendered({
        if ($script:AuthenticationStarted) {
            return
        }

        $script:AuthenticationStarted = $true
        Start-AuthenticationFlow
    })

    Show-AuthenticationStep
    [void]$window.ShowDialog()
    return $script:ExitCode
}

try {
    Write-FoundryLog -Message 'Starting Foundry Autopilot registration assistant.'
    $exitCode = Start-FoundryAutopilotRegistrationUi
    exit $exitCode
}
catch {
    $message = $_.Exception.Message
    Write-Result -Status 'failed' -Message $message
    Write-FoundryLog -Message "Autopilot registration failed. $message"
    Write-Error $message
    exit 1
}
