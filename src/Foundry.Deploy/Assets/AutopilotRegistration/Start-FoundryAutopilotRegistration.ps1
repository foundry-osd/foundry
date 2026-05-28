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

function Wait-DeviceCodeToken {
    param(
        [Parameter(Mandatory = $true)]$Config,
        [Parameter(Mandatory = $true)]$DeviceCode
    )

    $tenant = if ([string]::IsNullOrWhiteSpace($Config.tenant)) { 'common' } else { [string]$Config.tenant }
    $tokenUri = "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token"
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds([int]$DeviceCode.expires_in)
    $interval = [Math]::Max(5, [int]$DeviceCode.interval)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds $interval

        $tokenBody = ConvertTo-FormBody -Values @{
            grant_type = 'urn:ietf:params:oauth:grant-type:device_code'
            client_id = [string]$Config.clientId
            device_code = [string]$DeviceCode.device_code
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

function Invoke-DeviceCodeAuthentication {
    param([Parameter(Mandatory = $true)]$Config)

    $deviceCode = Request-DeviceCode -Config $Config
    return Wait-DeviceCodeToken -Config $Config -DeviceCode $deviceCode
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

    $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="420"
        Height="560"
        ResizeMode="NoResize"
        WindowStartupLocation="CenterScreen">
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0" Margin="0,0,0,12">
            <TextBlock Text="Register this device with Windows Autopilot using technician sign-in." TextWrapping="Wrap" />
        </StackPanel>

        <Grid Grid.Row="1">
            <Grid x:Name="AuthenticationStep">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <StackPanel Grid.Row="0">
                    <TextBlock Text="1. Authenticate" FontWeight="SemiBold" />
                    <TextBlock Text="Use the Microsoft device code prompt to sign in with an account allowed to import Windows Autopilot devices." TextWrapping="Wrap" Margin="0,6,0,12" />
                </StackPanel>
                <TextBox x:Name="DeviceCodeTextBox" Grid.Row="1" IsReadOnly="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto" />
                <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
                    <Button x:Name="AuthenticateButton" Content="Authenticate" />
                </StackPanel>
            </Grid>

            <Grid x:Name="UploadStep" Visibility="Collapsed">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <StackPanel Grid.Row="0">
                    <TextBlock Text="2. Group tag and upload" FontWeight="SemiBold" />
                    <TextBlock Text="Choose an existing group tag, enter a custom group tag, or leave the device without a group tag." TextWrapping="Wrap" Margin="0,6,0,12" />
                </StackPanel>
                <Grid Grid.Row="1" Margin="0,0,0,12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="Group tag" VerticalAlignment="Center" Margin="0,0,12,0" />
                    <StackPanel Grid.Column="1">
                        <ComboBox x:Name="GroupTagCombo" />
                        <TextBox x:Name="CustomGroupTagTextBox" Margin="0,8,0,0" Visibility="Collapsed" />
                    </StackPanel>
                </Grid>
                <TextBox x:Name="UploadStatusTextBox" Grid.Row="2" IsReadOnly="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto" />
                <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
                    <Button x:Name="UploadButton" Content="Upload" />
                </StackPanel>
            </Grid>
        </Grid>

        <TextBlock x:Name="StatusTextBlock" Grid.Row="2" Text="Ready." Margin="0,12,0,0" TextWrapping="Wrap" />
    </Grid>
</Window>
'@

    $xmlReader = New-Object System.Xml.XmlNodeReader ([xml]$xaml)
    $window = [Windows.Markup.XamlReader]::Load($xmlReader)

    $authenticationStep = $window.FindName('AuthenticationStep')
    $uploadStep = $window.FindName('UploadStep')
    $deviceCodeTextBox = $window.FindName('DeviceCodeTextBox')
    $authenticateButton = $window.FindName('AuthenticateButton')
    $groupTagCombo = $window.FindName('GroupTagCombo')
    $customGroupTagTextBox = $window.FindName('CustomGroupTagTextBox')
    $uploadStatusTextBox = $window.FindName('UploadStatusTextBox')
    $uploadButton = $window.FindName('UploadButton')
    $statusTextBlock = $window.FindName('StatusTextBlock')

    $script:AccessToken = $null
    $script:DiscoveredGroupTags = @()
    $script:ExitCode = 1

    function Set-StatusText {
        param([Parameter(Mandatory = $true)][string]$Message)
        $statusTextBlock.Text = $Message
        Write-FoundryLog -Message $Message
    }

    function Show-AuthenticationStep {
        $authenticationStep.Visibility = 'Visible'
        $uploadStep.Visibility = 'Collapsed'
        $deviceCodeTextBox.Text = ''
        Set-StatusText -Message 'Ready to authenticate.'
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
        $uploadStatusTextBox.Text = 'Authentication completed. Choose a group tag and select Upload.'
        Set-StatusText -Message 'Authentication completed.'
    }

    $groupTagCombo.Add_SelectionChanged({
        if ([string]$groupTagCombo.SelectedItem -eq 'Custom') {
            $customGroupTagTextBox.Visibility = 'Visible'
            $customGroupTagTextBox.Focus() | Out-Null
        }
        else {
            $customGroupTagTextBox.Visibility = 'Collapsed'
            $customGroupTagTextBox.Text = ''
        }
    })

    $authenticateButton.Add_Click({
        try {
            $authenticateButton.IsEnabled = $false
            Set-StatusText -Message 'Requesting Microsoft device code.'
            $deviceCode = Request-DeviceCode -Config $Config
            $deviceCodeTextBox.Text = [string]$deviceCode.message
            Set-StatusText -Message 'Waiting for Microsoft sign-in to complete.'

            $authWorker = New-Object System.ComponentModel.BackgroundWorker
            $authWorker.add_DoWork({
                param($sender, $eventArgs)
                $eventArgs.Result = Wait-DeviceCodeToken -Config $Config -DeviceCode $deviceCode
            })
            $authWorker.add_RunWorkerCompleted({
                param($sender, $eventArgs)
                if ($eventArgs.Error -ne $null) {
                    $authenticateButton.IsEnabled = $true
                    $message = $eventArgs.Error.Message
                    Write-Result -Status 'failed' -Message $message
                    Set-StatusText -Message $message
                    return
                }

                $script:AccessToken = [string]$eventArgs.Result
                Set-StatusText -Message 'Loading available group tags.'
                $script:DiscoveredGroupTags = @(Get-ExistingGroupTags -AccessToken $script:AccessToken)
                Show-UploadStep
            })
            $authWorker.RunWorkerAsync()
        }
        catch {
            $authenticateButton.IsEnabled = $true
            $message = $_.Exception.Message
            Write-Result -Status 'failed' -Message $message
            Set-StatusText -Message $message
        }
    })

    $uploadButton.Add_Click({
        try {
            $uploadButton.IsEnabled = $false
            $selectedGroupTag = Get-SelectedGroupTag -GroupTagCombo $groupTagCombo -CustomGroupTagTextBox $customGroupTagTextBox
            $uploadStatusTextBox.Text = 'Collecting hardware identity and uploading to Windows Autopilot.'
            Set-StatusText -Message 'Uploading Autopilot hardware hash.'

            $uploadWorker = New-Object System.ComponentModel.BackgroundWorker
            $uploadWorker.add_DoWork({
                param($sender, $eventArgs)
                $identity = Get-AutopilotHardwareIdentity
                $import = Import-AutopilotDeviceIdentity -AccessToken $script:AccessToken -Identity $identity -GroupTag $selectedGroupTag
                $completedImport = Wait-AutopilotImport -AccessToken $script:AccessToken -ImportedIdentityId $import.ImportedIdentityId
                $eventArgs.Result = [pscustomobject]@{
                    Identity = $identity
                    Import = $import
                    CompletedImport = $completedImport
                    GroupTag = $selectedGroupTag
                }
            })
            $uploadWorker.add_RunWorkerCompleted({
                param($sender, $eventArgs)
                if ($eventArgs.Error -ne $null) {
                    $uploadButton.IsEnabled = $true
                    $message = $eventArgs.Error.Message
                    Write-Result -Status 'failed' -Message $message
                    $uploadStatusTextBox.Text = $message
                    Set-StatusText -Message $message
                    return
                }

                $result = $eventArgs.Result
                $details = [pscustomobject]@{
                    serialNumber = $result.Identity.SerialNumber
                    groupTag = $result.GroupTag
                    importId = $result.Import.ImportId
                    importedIdentityId = $result.Import.ImportedIdentityId
                    deviceImportStatus = $result.CompletedImport.state.deviceImportStatus
                }
                Write-Result -Status 'completed' -Message 'Autopilot registration completed.' -Details $details
                Write-FoundryLog -Message 'Autopilot registration completed.'
                $uploadStatusTextBox.Text = "Autopilot registration completed.`r`nSerial number: $($result.Identity.SerialNumber)`r`nGroup tag: $($result.GroupTag)"
                $script:ExitCode = 0
                Set-StatusText -Message 'Autopilot registration completed.'
            })
            $uploadWorker.RunWorkerAsync()
        }
        catch {
            $uploadButton.IsEnabled = $true
            $message = $_.Exception.Message
            Write-Result -Status 'failed' -Message $message
            $uploadStatusTextBox.Text = $message
            Set-StatusText -Message $message
        }
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
