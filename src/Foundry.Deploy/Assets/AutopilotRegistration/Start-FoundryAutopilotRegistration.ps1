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

function Test-RegistrationAlreadyCompleted {
    if (-not (Test-Path -LiteralPath $ResultPath)) {
        return $false
    }

    try {
        $result = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
        return $result.status -eq 'completed'
    }
    catch {
        Write-FoundryLog -Message 'Failed to read existing registration result.'
        return $false
    }
}

if (Test-RegistrationAlreadyCompleted) {
    Write-FoundryLog -Message 'Autopilot registration is already completed.'
    exit 0
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

    $foundryLogoBase64 = 'iVBORw0KGgoAAAANSUhEUgAAADIAAAAyCAYAAAAeP4ixAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAA8DSURBVGhD7Zh5bJT5fYfdpkk2adptqqSnorRN1ChtlD+arVq1UROlihqpuaru9kiTLmR32WWbTbJgMBiDuQKsgYUFlstgDAbWYwxrwNiAsY3vsWc8h+e05z7emXfue+ad632q8bJJdlSjVNpt1YpHemek0Tv6fZ73+/0dehsaHvGIRzziEe8iwK/I09MfSvZPfDQ3b/k9EonfqL/nXYEe3jd6zvVYX9/kr129M/VbYwbDJ6aNtk8ParV/ekMz9+c3NHNfetOg+7sres23OlXKf+2cU645Paf88Wn1XPNxpXL34amZQ4cmp84cnFBebBubvbp7SHmr5dbk/ZabU6pN/ZOG9X2TSz+8Mu5dqxiLfO/8cOZI/3zA5fJ9uT7HQ5m2Gf5dH3IeUHrNx2c95i5NcLFXK1pvzYuLI6qgWTUfsho0MdviXHTRPRO1BCdEY3IsbMqPRczl4YiZoYSVuyk7t7NOBrIe+vN+bhYErksBrhWCXJXC9BQidOejXM7F6MrEOZ+KczYR52Q0xrFghIPeEDvtIs2LIVZPOjg6bMe44Nhcn3VFWlvPPabxWRK8AxmoAGUqFClSIEeOJGliJBCJ4kVkEQEDPubxMSf7mK56maj4uF/yMVz0cUfyMVDwcTPnpy8r0JsWUKQCXE6KXIgF6YwEOSMGeM0bYJc9yBazSLMpxOrhRU7cMaPXWlbV512RswMDH7eKzgciOSBLmQwSKfIkyZIgRZwYUSJECCDikH2Yq24WKi40FTeqspuZspupkoexoodRycs9ycudgpfBvI/+ByJXMwF60iJvJENciot0REK86g+z0xWj1R6jxRpjiyXGqrtLtN82YtBZvlGfd0WuK5V/aI94irUKyGSokqFMmiIpJJLkSCzLJIkRlIO4ZC+LVTcW2YNR9qCvepmvepmreJkp+5gs+Rgr+RktCtyTBO5KAQYLAfrzQa7nQvRlwyhSYU5ForQFE/zEl2K3J80OR4qt1iQt1iRPDy7RMaDDanV8sT7vilydnv4zbyoAlN4hUiJFkeSyTFqOEpAFXLIHp+zFhg+r7MMo+9DLPuarfuaqfpQVgalygIlykPvlICPFIEOSyB1J5HYhzEA+Qnc6yuuxBAfCGdrEHHuFPLs9OXY4srRY07RYszx9c4lzN1UsLFg/X593RQb0838bLsYBaVmiQpoyqeWrJpKQwwhVP178ePDjxI8NPxb8GGUBnSygkQOo5ACz1SDTVZHJSojxSojRUoiRUpiRYphb+TBd6SjHkymOJLIciknsDxfZJ5TY7Smw3Z5flmixFvj+dRsXb85JJpPpk/V5V6RfPf1PCTJAYbkSb18FEkTkIH7Zj4BA7dODgBOBJQQsCBgJoCeIRg6ilkVmZZGZaojJaoiJSpjJSoTRUpi+fIjObITTmSQn0jmOJoscipU5EK6yT6iw211ku12ixVKg2VLk2TftKAZUcbXa+rH6vCtyx6B+MUWBCjmk5ZbKkJPjBGXhgUQAPwG8BHAhYCfAIgHMBDESRI+IBnFZZE4OoayGmalGmK5EGCqFuFIIcjEXpjMX50w2w4mMxNFkhUMxmf1h2CdU2eUqs32pxBaTxGZThReu2ekdmPVOKxQfqs+7IkMmbUtmeYHNUCBNWo4tt5IP/3IVfAh4EXATwEEAG0GsBDEjYiCEnhBawszLYdRyZPmaKofplwIoCgG6CyEuFWKcy6dpzxY4nilzJClzMAptIdjjk9nprNC6WGaLsUTTQpUf9Nq5MaQ21Gd9KIMmzZHaGpWt7RFyFG/Vi1f2PZgTNQEB189JLCJiIYSJ0AORMFo5gk6OMl+NMlISuVYQuFII0COFeEOK0lVI0ZHPcypX4vW0zOEEHIjAK0H4iRd22Ktss1RoNlRo1FZ5uddO/z3VdH3WhzK0qO+qtVaUKK6KB1fVg0v24Vqe2AKOB+20tLwBilgJYSaMkTALRFggxoIcQ1kJc6so0Cv5uSoFuSKF6JaiXJKSnC9kac8XOZGtcjQFr8beqsbeAOx2w3YbbDXLbNbLvKyu0nTVzu1hVX991ocyaNXeipLBXnWzVHFjq3qxyT5ssn95UtfmQ62VrA8q8ZZEBANRjMTQVaOMloL0Ff1cKwa4WhTpKYZR1CSKSS4Uc3QUJE7lKhxLs1yN/VHYJ8IeP+x0QusitBhhsw5emqmw7U07I2OarvqsD6V/UTPjIISx7MRcdmGuejBXfZhlP2ZZwCQHMMlBjLKIQQ5jkCMY5SgmOcZMJUT/ssDPJBTFMN1SbLkSF4pZOqQip/MVXs/Aayk4GINXwrCnVo1aWzlgmwWaDbBJCy+Ol9j9po3h+6pj9VkfyjWb1rSAgLbkRFdyoSt70FV86Kp+dFUBvRxALwfRyyH0cnhZQlMNc68ocE3ycbXWTsUgPcUQ3cUIl4sxLhaTdEpZzhZLnC5UOZ6DI2l4NQFtUdgrwu5aNTyw3Q4tZmjWwyYNPD8i0XZ9ibEJ1Y76rCuiPiV8WLGoFlT4mJUczBadzJXczJW9qMp+VBWB+WqA+WoQbfUtkclSgL6ChysFH1eKAopikO5iiMvFCF3FBJ3FDB1SnvZCkZMFmddz8FoaDiahLQZ7w7A7ADt9sN0F25Zgiwk2a2HTPDxzO8eh61YmprU/rs+7IjMzC7992a5JTZQ9jOXtjBecjEtuJore5TPTVMnPTDmAuioyWw4ykPfQnXPRnffSXRC4JIlclMJckGKcLybpKGZplyROFsocz8kczcHhzAOJOOyNwO4g7PBDqwe2OWCrFZoX3pLYpIbVN1Ic6zMyOjbzi598r02qPtW5NF++W3BxJ2PjbtbBUN7FcMHDiORlrOhnqiRwr+BFkXHSlXZyKeflYl7gQl6ksxChQ4pzVkrTLuU4WZB4PV/9qcCradifhFfisCcCu0TY7odtHtjqhBYbND+oxsY52DQHT/fGOdmnY2pK/fX6vCtyeXT2iQ6njhsZO9eTS9xI2+nPOBnMubmb9zJU8NCbcnAusURn0kFn2s25jJ+OXJAzuQin80lOFbKcLOR5PV/kSK76U4EDKXglAXtisDsCO0VoFWCrB7Y4obkmYYFNC9Ckhg0z0DQDT78R4dQ1FcNjU39Tn3dFFErdV856TPTEl+iJLnIlbuNaysHNjHv5+2zEyonIIu1xO6cTLk4lfZxKBziRiXA8l+RYPsfRvMSRXJlDmSoH367A2wJR2BmC7UHY5ocWDzQ7YbMNNlthkwE2amCDEhonYOMkrOoSOaWYZGDg3i9+8j07pXzytN/KhbCVrpCVy5ElFHE758JWDvtNHA5YORqycTTi5GjMy5G4wGvJMIfTSQ5lcryakTiYLnMgJdOWhH21Foo+qEAYtouwTYAWHzR7YLMTNtmgyQpNJtiogw1z0DgF6+/Dxvuw+pxAu2JMGhwd/YP6vCvSPq18/kTQzlnRyjnRSodo5YjXxF6XgTavhf3CEgcCDg6IbvaH/eyPiLTFYrySyPBKosC+eJk98So/ebt9ak+/1kIB2PrzAi5ocsCGJdhggUYjbNBBoxoap6FxDNbfg8Z78MwZL2cVIwnFwMDH6/OuyMnxqaYdRi1bNdNs1U6zTj3FDzUzrDfOs8GqZ5PdTLPbRovPzbZAgFYxyo5wip2RPLsiJXZFqstPviaw422BWgt5YUutjdxvVaHJDht/TqLxbYlaS41D4zCsvw3rB2DNaTfneka8CsXBX/zka/OHXvUkkszaPdy32hkwWXnTYOHygokzWj3H5vW0qbTsUOloURtoVJn5oWqJF+dcrFH5eFYt8owmxjPaFM/qcjy3ILHGUOJ5Y4W1JpmXzPAjC7xsgXUWWG+GRsNb1djwYIJvGIMNQ7BhANbfhBdPOblw5Z65oaHhl+rzrogrmDhce+VQqb7zHcp/Re2eTL5EOJXHE0lh8kdRucOM20IMmAL0aP10zHo5Oull/5iPXSMCzXeCvHwrxH/cCPN8X4Tn+uI805fi+9cyrO7NskqRZ/UbBZ65XOK5SxWevSjz/NFFLvTcnqvP+lB6htSP353Q/+WIcuHvNRbvd9Qm54/MjuAum1c8bvdHenzh1JAQSWlC8awzmsxH4ulCIZsvUe9dKVUoF8sU8xL5TJ5MKksykSYWTSOG07j9MazOCLpFEaUhwIjaz81JD4phF52DDk5cd3Cwx8aOCzaOXLFwsXfobn3Wd4dPf+2Dm45f+uiZ3uFP3pnSfW5UbfzinMXzDZ0t8D3lgqNz1uBiQm1lUrPIjM7G7IKDeZMbrdldsrrF1KJblN1ikmA8TyRVJJYpEUsXCSfzBKMpvMEoDm8Qq8OLO5iks2foRn2E95xZo+t3FmyByozezrDSyOC4luv3VNwYmed4V3+05dD5z57vn/ls963pvx64r/tG/5h21e0JXeOdSf3eW2Pqs3cn9dcG7mvGBkbnFwbHNJ7+kXnvgZOKtfXjvCcAj5XgC8AXZFn+XE6qhPOlKqFEDncwgcUVYmFJYHBcz/fWHfjj+v+vyCe/9FhDw+9+uP7ndxXg8bIsf7NUrrZn8yVHplAmlZVqbVEwO4SK0Sags3pRG10o9Q5mF5wMTRtoVwz2Gp3Ccws277fdodRfSbL8GeC9DVsP8BFZlr8ly3JXqVKtvcVbplCGaCqPEE7i9IVZWPSg1C0xOmvk9riWG8MqOnvvsf9kN1dvTzNv8WFyiDj8MRY9Ybqvj1yoH+s9xSNEBt65Pv2MUm05LpSJpXL4xDhLHhGjzce8ycHdCQ1Xbo1z894sw9N6ZrSLqAxO+u7O0HqgfWnVS1vXPffSlk81NTU93tra+sv1477rXO4b+Yv+kbk1bw5O7Lg7rj41ptTfVOoss2qDza23uJPGJa/sEmIEYjnERIFQUsIfyS6vPIFYlkAkjVOIMTxjpO34JfHpH2w9/uVvr/rWN/9lzRP/9sKPPrNm3bqPrVmz5v314/5P8pGvPPny769tbPv85n3tX91ztOvpgycVTcc6rh062z3Y80bf6P2bQzNG9YLD23dnxv7Cxn3nv/oPzz75pX/8/hNPrl7/J/+8dt0nvrN27Uefam39wH9rB/9f4gMNDX/0eEPDb/56Q0PD+7++Zs2Hv/vd9b/6tZde+uBTTz31vv8LAo94xCMe8Yj/f/wnsp7aWPosj38AAAAASUVORK5CYII='

    $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Foundry OSD - Interactive hardware hash upload"
        Width="420"
        Height="560"
        ResizeMode="NoResize"
        Topmost="True"
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

    $script:WorkerState = @{
        Events = [Collections.Concurrent.ConcurrentQueue[string]]::new()
        Commands = [Collections.Concurrent.BlockingCollection[string]]::new(1)
        Cancellation = [Threading.CancellationTokenSource]::new()
        Worker = $null; Runspace = $null; Pending = $null; Stopping = $null
        Closing = $false; AllowClose = $false; Busy = $false; Observed = $false; ExitCode = 1
    }
    $state = $script:WorkerState

    function Set-UploadEnabled {
        param([bool]$Enabled)
        $groupTagCombo.IsEnabled = $Enabled
        $customGroupTagTextBox.IsEnabled = $Enabled -and ([string]$groupTagCombo.SelectedItem -eq 'Custom')
        $uploadButton.IsEnabled = $Enabled
    }

    $groupTagCombo.Add_SelectionChanged({
        $customGroupTagTextBox.IsEnabled = $groupTagCombo.IsEnabled -and ([string]$groupTagCombo.SelectedItem -eq 'Custom')
        if (-not $customGroupTagTextBox.IsEnabled) { $customGroupTagTextBox.Text = '' }
    })
    $uploadButton.Add_Click({
        if ($state.Busy -or $state.Closing -or $state.Observed) { return }
        $tag = Get-SelectedGroupTag -GroupTagCombo $groupTagCombo -CustomGroupTagTextBox $customGroupTagTextBox
        $command = @{kind='upload';groupTag=$tag} | ConvertTo-Json -Compress
        if ($state.Commands.TryAdd($command)) {
            $state.Busy = $true
            Set-UploadEnabled $false
            $uploadStatusTextBlock.Text = 'Starting registration.'
            $uploadProgressBar.IsIndeterminate = $true
        }
    })

    $timer = [System.Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds(100)
    $timer.Add_Tick({
        $messageJson = $null
        for ($drained = 0; $drained -lt 50 -and $state.Events.TryDequeue([ref]$messageJson); $drained++) {
            $message = $messageJson | ConvertFrom-Json
            switch ($message.kind) {
                deviceCode {
                    $authenticationInstructionTextBlock.Text = 'Go to https://microsoft.com/devicelogin in a browser and enter this code:'
                    $deviceCodeTextBlock.Text = [string]$message.data.code
                    $authenticationStatusTextBlock.Text = 'Waiting for sign-in.'
                    $authenticationProgressBar.IsIndeterminate = $true
                }
                notice { $uploadStatusTextBlock.Text = [string]$message.data }
                ready {
                    $deviceCodeTextBlock.Text = ''
                    $authenticationStep.Visibility = 'Collapsed'
                    $uploadStep.Visibility = 'Visible'
                    $groupTagCombo.Items.Clear()
                    [void]$groupTagCombo.Items.Add('None')
                    foreach ($tag in $message.data.tags) { [void]$groupTagCombo.Items.Add([string]$tag) }
                    [void]$groupTagCombo.Items.Add('Custom')
                    $groupTagCombo.SelectedIndex = 0
                    $uploadStatusTextBlock.Text = 'Ready to upload.'
                    if (-not $state.Closing) { Set-UploadEnabled $true }
                }
                progress { $uploadStatusTextBlock.Text = [string]$message.data }
                result {
                    $state.Busy = $false
                    $uploadProgressBar.IsIndeterminate = $false
                    if ($message.data.status -eq 'completed') {
                        $state.ExitCode = 0
                        $uploadProgressBar.Value = 100
                        $uploadStatusTextBlock.Text = 'Registration completed. Restarting in 10 seconds.'
                    }
                    else {
                        $uploadStatusTextBlock.Text = 'Registration failed (' + [string]$message.data.code + '). Review the current import in Intune before retrying.'
                        if (-not $state.Closing) { Set-UploadEnabled $true }
                    }
                }
                fatal {
                    $deviceCodeTextBlock.Text = ''
                    $authenticationProgressBar.IsIndeterminate = $false
                    $authenticationStatusTextBlock.Text = if ($message.data.code -eq 'TimedOut') {
                        'Sign-in timed out. Close the assistant and request a new code.'
                    } else { 'Authentication failed. Close the assistant and try again.' }
                }
            }
        }
        if ($null -ne $state.Pending -and $state.Pending.IsCompleted -and -not $state.Observed -and
            ($null -eq $state.Stopping -or $state.Stopping.IsCompleted)) {
            try {
                if ($null -ne $state.Stopping) { $state.Worker.EndStop($state.Stopping) }
                $null = $state.Worker.EndInvoke($state.Pending)
            }
            catch {
                if (-not $state.Closing -and $state.ExitCode -ne 0) {
                    $authenticationStatusTextBlock.Text = 'The registration worker stopped. Close the assistant and try again.'
                    $uploadStatusTextBlock.Text = 'The registration worker stopped. Close the assistant and try again.'
                }
            }
            finally {
                $state.Worker.Dispose()
                $state.Runspace.Dispose()
                $state.Commands.Dispose()
                $state.Cancellation.Dispose()
                $state.Observed = $true
                Set-UploadEnabled $false
            }
        }
        if ($state.Closing -and ($state.Observed -or $null -eq $state.Pending)) {
            $timer.Stop()
            $state.AllowClose = $true
            $window.Close()
        }
    })
    $window.Add_Closing({
        param($sender, $eventArgs)
        if ($state.AllowClose) { return }
        $eventArgs.Cancel = $true
        if ($state.Closing) { return }
        $state.Closing = $true
        Set-UploadEnabled $false
        $authenticationStatusTextBlock.Text = 'Stopping registration safely.'
        $uploadStatusTextBlock.Text = 'Stopping registration safely.'
        if (-not $state.Observed) {
            $state.Cancellation.Cancel()
            if ($null -eq $state.Pending) {
                $state.Commands.Dispose()
                $state.Cancellation.Dispose()
                $state.Observed = $true
            }
            if ($null -ne $state.Pending -and -not $state.Pending.IsCompleted) {
                try { $state.Stopping = $state.Worker.BeginStop($null, $null) } catch { $authenticationStatusTextBlock.Text = 'Waiting for the registration worker to stop.' }
            }
        }
    })
    $window.Add_ContentRendered({
        if ($null -ne $state.Pending -or $state.Closing -or $state.Observed) { return }
        $window.Topmost = $true
        [void]$window.Activate()
        $authenticationStatusTextBlock.Text = 'Requesting Microsoft sign-in.'
        $authenticationProgressBar.IsIndeterminate = $true
        try {
            $state.Runspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
            $state.Runspace.Open()
            $state.Worker = [System.Management.Automation.PowerShell]::Create()
            $state.Worker.Runspace = $state.Runspace
            $workerScript = 'param($protocol,$config,$events,$commands,$token); $ErrorActionPreference="Stop"; . $protocol; Start-FoundryAutopilotWorker $config $events $commands $token'
            $null = $state.Worker.AddScript($workerScript).AddArgument((Join-Path $PSScriptRoot 'Foundry-AutopilotProtocol.ps1')).AddArgument(($Config | ConvertTo-Json -Depth 10 -Compress)).AddArgument($state.Events).AddArgument($state.Commands).AddArgument($state.Cancellation.Token)
            $state.Pending = $state.Worker.BeginInvoke()
        }
        catch {
            if ($null -ne $state.Worker) { $state.Worker.Dispose() }
            if ($null -ne $state.Runspace) { $state.Runspace.Dispose() }
            $state.Commands.Dispose()
            $state.Cancellation.Dispose()
            $state.Observed = $true
            $authenticationStatusTextBlock.Text = 'The registration worker could not start. Close the assistant and try again.'
            $authenticationProgressBar.IsIndeterminate = $false
        }
    })
    $authenticationStep.Visibility = 'Visible'
    $uploadStep.Visibility = 'Collapsed'
    Set-UploadEnabled $false
    $timer.Start()
    [void]$window.ShowDialog()
    $timer.Stop()
    return $state.ExitCode
}

try {
    Write-FoundryLog -Message 'Starting Foundry Autopilot registration assistant.'
    exit (Start-FoundryAutopilotRegistrationUi)
}
catch {
    Write-FoundryLog -Message 'The Autopilot registration assistant could not start.'
    exit 1
}
