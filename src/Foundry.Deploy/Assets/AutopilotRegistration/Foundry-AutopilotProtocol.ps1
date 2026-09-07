function Get-FoundryRemainingSeconds {
    param($Context, [DateTimeOffset]$Deadline)
    $Context.Token.ThrowIfCancellationRequested()
    $remaining = ($Deadline - (& $Context.Now)).TotalSeconds
    if ($remaining -le 0) { throw 'TimedOut' }
    return $remaining
}

function Wait-FoundryAutopilot {
    param($Context, [double]$Seconds, [DateTimeOffset]$Deadline)
    $remaining = Get-FoundryRemainingSeconds $Context $Deadline
    & $Context.Sleep ([Math]::Min($remaining, [Math]::Max(0, $Seconds))) $Context.Token
    $null = Get-FoundryRemainingSeconds $Context $Deadline
}

function Resolve-FoundryGraphUri {
    param($Context, [string]$Path)
    $base = [Uri](([string]$Context.Config.graphBaseUri).TrimEnd('/') + '/')
    if ($base.Scheme -cne 'https' -or $base.Host -ine 'graph.microsoft.com' -or -not $base.IsDefaultPort -or
        $base.UserInfo -or $base.Query -or $base.Fragment -or $base.AbsolutePath -notin @('/v1.0/', '/beta/')) { throw 'InvalidGraphEndpoint' }
    $uri = [Uri]::new($base, $Path)
    if ($uri.Scheme -cne 'https' -or $uri.Authority -ine $base.Authority -or $uri.UserInfo -or $uri.Fragment -or
        -not $uri.AbsolutePath.StartsWith($base.AbsolutePath, [StringComparison]::Ordinal)) { throw 'InvalidGraphEndpoint' }
    return $uri.AbsoluteUri
}

function Invoke-FoundryHttp {
    param($Context, [string]$Method, [string]$Uri, $Body, [DateTimeOffset]$Deadline)
    Add-Type -AssemblyName System.Net.Http
    $remaining = Get-FoundryRemainingSeconds $Context $Deadline
    $requestDeadline = [Threading.CancellationTokenSource]::CreateLinkedTokenSource($Context.Token)
    $requestDeadline.CancelAfter([TimeSpan]::FromSeconds([Math]::Min(30, $remaining)))
    if ($null -ne $Context.HttpClientFactory) { $client = & $Context.HttpClientFactory }
    else {
        $handler = [Net.Http.HttpClientHandler]::new()
        $handler.AllowAutoRedirect = $false
        $client = [Net.Http.HttpClient]::new($handler)
    }
    $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $Uri)
    $response = $null
    try {
        $isGraph = ([Uri]$Uri).Host -ieq 'graph.microsoft.com'
        if ($isGraph) {
            $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', [string]$Context.AccessToken)
        }
        if ($null -ne $Body) {
            if ($isGraph) {
                $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 10 -Compress), [Text.Encoding]::UTF8, 'application/json')
            }
            else {
                $form = [Collections.Generic.Dictionary[string,string]]::new()
                foreach ($key in $Body.Keys) { $form.Add($key, [string]$Body[$key]) }
                $request.Content = [Net.Http.FormUrlEncodedContent]::new($form)
            }
        }
        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $requestDeadline.Token).GetAwaiter().GetResult()
        $retryAfter = 0.0
        if ($null -ne $response.Headers.RetryAfter) {
            if ($response.Headers.RetryAfter.Delta) { $retryAfter = $response.Headers.RetryAfter.Delta.TotalSeconds }
            elseif ($response.Headers.RetryAfter.Date) { $retryAfter = ($response.Headers.RetryAfter.Date - (& $Context.Now)).TotalSeconds }
        }
        if (-not $response.IsSuccessStatusCode -and ($isGraph -or [int]$response.StatusCode -ne 400)) {
            return @{statusCode=[int]$response.StatusCode;body=$null;retryAfter=[Math]::Max(0, $retryAfter)}
        }
        $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $buffer = New-Object byte[] 8192
        $memory = [IO.MemoryStream]::new()
        try {
            while (($count = $stream.ReadAsync($buffer, 0, $buffer.Length, $requestDeadline.Token).GetAwaiter().GetResult()) -gt 0) {
                if ($memory.Length + $count -gt 2097152) { throw 'ResponseTooLarge' }
                $memory.Write($buffer, 0, $count)
            }
            $text = [Text.Encoding]::UTF8.GetString($memory.ToArray())
        }
        finally { $memory.Dispose(); $stream.Dispose() }
        $bodyObject = $null
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            try { $bodyObject = $text | ConvertFrom-Json } catch { throw 'InvalidResponse' }
        }
        return @{statusCode=[int]$response.StatusCode; body=$bodyObject; retryAfter=[Math]::Max(0, $retryAfter)}
    }
    catch {
        $Context.Token.ThrowIfCancellationRequested()
        if ($requestDeadline.IsCancellationRequested) { throw 'TimedOut' }
        if ($_.Exception.Message -in @('ResponseTooLarge','InvalidResponse')) { throw }
        $cause = $_.Exception
        while ($null -ne $cause) {
            if ($cause -is [Security.Authentication.AuthenticationException] -or
                ($cause -is [Net.WebException] -and $cause.Status -in @([Net.WebExceptionStatus]::TrustFailure, [Net.WebExceptionStatus]::SecureChannelFailure))) { throw 'SecureChannelFailed' }
            $cause = $cause.InnerException
        }
        throw 'RequestFailed'
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        $request.Dispose(); $client.Dispose(); $requestDeadline.Dispose()
    }
}

function Invoke-FoundryAutopilotRequest {
    param($Context, [string]$Method, [string]$Path, $Body, [DateTimeOffset]$Deadline, [switch]$OAuth)
    $uri = if ($OAuth) { $Path } else { Resolve-FoundryGraphUri $Context $Path }
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $null = Get-FoundryRemainingSeconds $Context $Deadline
        try { $response = & $Context.Transport $Context $Method $uri $Body $Deadline }
        catch {
            $Context.Token.ThrowIfCancellationRequested()
            if ($Method -ne 'GET' -or $attempt -eq 3 -or $_.Exception.Message -notin @('RequestFailed','TimedOut')) { throw }
            Wait-FoundryAutopilot $Context ([Math]::Pow(2, $attempt)) $Deadline
            continue
        }
        $null = Get-FoundryRemainingSeconds $Context $Deadline
        $status = [int]$response.statusCode
        $retryable = $status -eq 429 -or ($Method -eq 'GET' -and ($status -eq 408 -or $status -in @(500,502,503,504)))
        if (-not $retryable -or $attempt -eq 3) { return $response }
        $delay = [Math]::Max([Math]::Pow(2, $attempt), [double]$response.retryAfter)
        Wait-FoundryAutopilot $Context $delay $Deadline
    }
}

function Test-FoundryImportedIdentity {
    param($Imported, $Request, [string]$ImportedId = '')
    if ($null -eq $Imported -or [string]::IsNullOrWhiteSpace([string]$Imported.id) -or
        -not [string]::Equals([string]$Imported.importId, [string]$Request.importId, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Imported.serialNumber, [string]$Request.serialNumber, [StringComparison]::Ordinal) -or
        ($ImportedId -and -not [string]::Equals([string]$Imported.id, $ImportedId, [StringComparison]::Ordinal))) { return $false }
    try {
        $actual = [Convert]::FromBase64String([string]$Imported.hardwareIdentifier)
        $expected = [Convert]::FromBase64String([string]$Request.hardwareIdentifier)
        if ($expected.Length -eq 0 -or $actual.Length -ne $expected.Length) { return $false }
        for ($i = 0; $i -lt $expected.Length; $i++) { if ($actual[$i] -ne $expected[$i]) { return $false } }
        return $true
    }
    catch { return $false }
}

function Get-FoundrySingleIdentity {
    param($Body)
    if ($null -ne $Body.value) {
        if ($Body.value -is [Array]) { throw 'IdentityUnconfirmed' }
        return $Body.value
    }
    return $Body
}

function Invoke-FoundryAutopilotImport {
    param($Context, $Request, [DateTimeOffset]$Deadline = [DateTimeOffset]::MinValue)
    if ($Deadline -eq [DateTimeOffset]::MinValue) { $Deadline = (& $Context.Now).AddMinutes(15) }
    try {
        if ([string]::IsNullOrWhiteSpace([string]$Request.importId) -or [string]::IsNullOrWhiteSpace([string]$Request.serialNumber)) { throw 'IdentityUnconfirmed' }
        try { if ([Convert]::FromBase64String([string]$Request.hardwareIdentifier).Length -eq 0) { throw 'IdentityUnconfirmed' } }
        catch { throw 'IdentityUnconfirmed' }
        $response = Invoke-FoundryAutopilotRequest $Context POST 'deviceManagement/importedWindowsAutopilotDeviceIdentities/import' @{importedWindowsAutopilotDeviceIdentities=@($Request)} $deadline
        if ([int]$response.statusCode -notin @(200,201)) { throw 'ImportFailed' }
        $rows = @($response.body.value)
        if ($response.body.value -isnot [Array] -or $rows.Count -ne 1 -or $null -ne $response.body.'@odata.nextLink' -or
            -not (Test-FoundryImportedIdentity $rows[0] $Request)) { throw 'IdentityUnconfirmed' }
        $imported = $rows[0]
        $importedId = [string]$imported.id
        while ($true) {
            $null = Get-FoundryRemainingSeconds $Context $deadline
            if (-not (Test-FoundryImportedIdentity $imported $Request $importedId)) { throw 'IdentityUnconfirmed' }
            $status = [string]$imported.state.deviceImportStatus
            $registrationId = [string]$imported.state.deviceRegistrationId
            if ($status -ieq 'error') {
                $known = [string]::Equals([string]$imported.state.deviceErrorName, 'ZtdDeviceAlreadyAssigned', [StringComparison]::OrdinalIgnoreCase)
                if (-not $known -or ($null -ne $imported.state.deviceErrorCode -and [int]$imported.state.deviceErrorCode -ne 806)) { throw 'ImportFailed' }
                if ([string]::IsNullOrWhiteSpace($registrationId)) { throw 'IdentityUnconfirmed' }
                break
            }
            if ($status -ieq 'complete') {
                if ([string]::IsNullOrWhiteSpace($registrationId)) { throw 'IdentityUnconfirmed' }
                break
            }
            if ($status -notin @('pending','unknown','partial')) { throw 'IdentityUnconfirmed' }
            Wait-FoundryAutopilot $Context 15 $deadline
            $response = Invoke-FoundryAutopilotRequest $Context GET ('deviceManagement/importedWindowsAutopilotDeviceIdentities/' + [Uri]::EscapeDataString($importedId)) $null $deadline
            if ([int]$response.statusCode -ne 200) { throw 'ImportFailed' }
            $imported = Get-FoundrySingleIdentity $response.body
        }
        $visibilityDeadline = (& $Context.Now).AddMinutes(10)
        if ($visibilityDeadline -gt $deadline) { $visibilityDeadline = $deadline }
        $devicePath = 'deviceManagement/windowsAutopilotDeviceIdentities/' + [Uri]::EscapeDataString($registrationId)
        $updated = $false
        while ($true) {
            $response = Invoke-FoundryAutopilotRequest $Context GET $devicePath $null $visibilityDeadline
            if ([int]$response.statusCode -eq 404) { Wait-FoundryAutopilot $Context 15 $visibilityDeadline; continue }
            if ([int]$response.statusCode -ne 200) { throw 'ImportFailed' }
            $device = Get-FoundrySingleIdentity $response.body
            if (-not [string]::Equals([string]$device.id, $registrationId, [StringComparison]::Ordinal) -or
                -not [string]::Equals([string]$device.serialNumber, [string]$Request.serialNumber, [StringComparison]::Ordinal)) { throw 'IdentityUnconfirmed' }
            if ([string]::Equals(([string]$device.groupTag).Trim(), ([string]$Request.groupTag).Trim(), [StringComparison]::OrdinalIgnoreCase)) {
                return [pscustomobject]@{status='completed';code=$null;registrationId=$registrationId}
            }
            if (-not $updated) {
                $response = Invoke-FoundryAutopilotRequest $Context POST ($devicePath + '/updateDeviceProperties') @{groupTag=([string]$Request.groupTag).Trim()} $visibilityDeadline
                if ([int]$response.statusCode -notin @(200,204)) { throw 'ImportFailed' }
                $updated = $true
            }
            Wait-FoundryAutopilot $Context 15 $visibilityDeadline
        }
    }
    catch {
        $Context.Token.ThrowIfCancellationRequested()
        $code = [string]$_.Exception.Message
        if ($code -notin @('IdentityUnconfirmed','ImportFailed','TimedOut')) { $code = 'RequestFailed' }
        return [pscustomobject]@{status='failed';code=$code;registrationId=$null}
    }
}

function Send-FoundryAutopilotProgress {
    param($Context, [string]$Kind, $Data)
    $Context.Events.Enqueue((@{kind=$Kind;data=$Data} | ConvertTo-Json -Depth 6 -Compress))
}

function Get-FoundryAutopilotToken {
    param($Context)
    $deadline = (& $Context.Now).AddMinutes(15)
    $tenant = [string]$Context.Config.tenant
    if ([string]::IsNullOrWhiteSpace($tenant)) { $tenant = 'common' }
    $tenant = [Uri]::EscapeDataString($tenant)
    $authority = 'https://login.microsoftonline.com/' + $tenant + '/oauth2/v2.0/'
    $deviceRequestStarted = & $Context.Now
    $response = Invoke-FoundryAutopilotRequest $Context POST ($authority + 'devicecode') @{
        client_id=[string]$Context.Config.clientId;scope=(@($Context.Config.scopes) -join ' ')
    } $deadline -OAuth
    if ([int]$response.statusCode -ne 200) { throw 'AuthenticationFailed' }
    $deviceCode = $response.body
    if ([string]::IsNullOrWhiteSpace([string]$deviceCode.device_code) -or [string]::IsNullOrWhiteSpace([string]$deviceCode.user_code) -or
        [double]$deviceCode.expires_in -le 0) { throw 'AuthenticationFailed' }
    $expires = $deviceRequestStarted.AddSeconds([double]$deviceCode.expires_in)
    if ($expires -lt $deadline) { $deadline = $expires }
    $interval = [Math]::Max(5, [double]$deviceCode.interval)
    Send-FoundryAutopilotProgress $Context 'deviceCode' @{code=[string]$deviceCode.user_code;expires=$deadline.ToString('o')}
    while ($true) {
        Wait-FoundryAutopilot $Context $interval $deadline
        $response = Invoke-FoundryAutopilotRequest $Context POST ($authority + 'token') @{
            grant_type='urn:ietf:params:oauth:grant-type:device_code';client_id=[string]$Context.Config.clientId;device_code=[string]$deviceCode.device_code
        } $deadline -OAuth
        if ([int]$response.statusCode -eq 200 -and -not [string]::IsNullOrWhiteSpace([string]$response.body.access_token)) { return [string]$response.body.access_token }
        if ([int]$response.statusCode -eq 400 -and [string]$response.body.error -ceq 'authorization_pending') { continue }
        if ([int]$response.statusCode -eq 400 -and [string]$response.body.error -ceq 'slow_down') { $interval += 5; continue }
        if ([int]$response.statusCode -eq 400 -and [string]$response.body.error -ceq 'expired_token') { throw 'TimedOut' }
        if ([int]$response.statusCode -in @(408,429,500,502,503,504)) { continue }
        throw 'AuthenticationFailed'
    }
}

function Get-FoundryAutopilotGroupTags {
    param($Context)
    $deadline = (& $Context.Now).AddMinutes(2)
    $path = 'deviceManagement/windowsAutopilotDeviceIdentities?$select=groupTag&$top=100'
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $tags = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($page = 0; $page -lt 100; $page++) {
        $uri = Resolve-FoundryGraphUri $Context $path
        if (-not $visited.Add($uri)) { throw 'InvalidPagination' }
        $parsed = [Uri]$uri
        if ($parsed.AbsolutePath -cne (([Uri]([string]$Context.Config.graphBaseUri)).AbsolutePath.TrimEnd('/') + '/deviceManagement/windowsAutopilotDeviceIdentities')) { throw 'InvalidPagination' }
        $response = Invoke-FoundryAutopilotRequest $Context GET $uri $null $deadline
        if ([int]$response.statusCode -ne 200) { throw 'GroupDiscoveryFailed' }
        if ($response.body.value -isnot [Array]) { throw 'GroupDiscoveryFailed' }
        foreach ($row in $response.body.value) {
            if (-not [string]::IsNullOrWhiteSpace([string]$row.groupTag)) { $null = $tags.Add([string]$row.groupTag) }
            if ($tags.Count -gt 10000) { throw 'GroupDiscoveryFailed' }
        }
        $path = [string]$response.body.'@odata.nextLink'
        if ([string]::IsNullOrWhiteSpace($path)) { return @($tags | Sort-Object) }
    }
    throw 'GroupDiscoveryFailed'
}

function Get-FoundryAutopilotHardwareIdentity {
    param($Context, [DateTimeOffset]$Deadline)
    $timeout = [uint32][Math]::Max(1, [Math]::Min(30, [Math]::Floor((Get-FoundryRemainingSeconds $Context $Deadline))))
    $bios = Get-CimInstance -ClassName Win32_BIOS -OperationTimeoutSec $timeout -ErrorAction Stop
    $null = Get-FoundryRemainingSeconds $Context $Deadline
    $timeout = [uint32][Math]::Max(1, [Math]::Min(30, [Math]::Floor((Get-FoundryRemainingSeconds $Context $Deadline))))
    $detail = Get-CimInstance -Namespace 'root/cimv2/mdm/dmmap' -ClassName MDM_DevDetail_Ext01 -Filter "InstanceID='Ext' AND ParentID='./DevDetail'" -OperationTimeoutSec $timeout -ErrorAction Stop
    $null = Get-FoundryRemainingSeconds $Context $Deadline
    $serial = ([string]$bios.SerialNumber).Trim()
    $hash = ([string]$detail.DeviceHardwareData).Trim()
    if ([string]::IsNullOrWhiteSpace($serial) -or [string]::IsNullOrWhiteSpace($hash)) { throw 'CaptureUnavailable' }
    try { if ([Convert]::FromBase64String($hash).Length -eq 0) { throw 'CaptureUnavailable' } }
    catch { throw 'CaptureUnavailable' }
    return @{serialNumber=$serial;hardwareIdentifier=$hash}
}

function Write-FoundryAutopilotOutcome {
    param($Config, $Result)
    $root = [Environment]::ExpandEnvironmentVariables([string]$Config.stateRootPath)
    $path = Join-Path $root 'registration-result.json'
    $temporary = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $json = @{completedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');status=$Result.status;code=$Result.code} | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
        if ([IO.File]::Exists($path)) { [IO.File]::Replace($temporary, $path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $path) }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Get-FoundryAutopilotFailureCode {
    param($ErrorRecord, [string]$Fallback)
    $code = [string]$ErrorRecord.Exception.Message
    if ($code -in @('TimedOut','CaptureUnavailable','AuthenticationFailed','RequestFailed')) { return $code }
    return $Fallback
}

function Start-FoundryAutopilotWorker {
    param([string]$ConfigJson, $Events, $Commands, [Threading.CancellationToken]$Token)
    $config = $ConfigJson | ConvertFrom-Json
    $context = @{Config=$config;Token=$Token;Events=$Events;AccessToken=$null;
        Now={ [DateTimeOffset]::UtcNow };
        Sleep={ param($seconds,$token) if ($token.WaitHandle.WaitOne([TimeSpan]::FromSeconds($seconds))) { $token.ThrowIfCancellationRequested() } };
        Transport={ param($context,$method,$uri,$body,$deadline) Invoke-FoundryHttp $context $method $uri $body $deadline }}
    try {
        $context.AccessToken = Get-FoundryAutopilotToken $context
        $tags = @()
        try { $tags = @(Get-FoundryAutopilotGroupTags $context) }
        catch { $Token.ThrowIfCancellationRequested(); Send-FoundryAutopilotProgress $context 'notice' 'Group tags could not be loaded. Enter a tag manually.' }
        Send-FoundryAutopilotProgress $context 'ready' @{tags=$tags}
        while ($true) {
            $command = $Commands.Take($Token) | ConvertFrom-Json
            if ($command.kind -ne 'upload') { continue }
            try {
                $deadline = (& $context.Now).AddMinutes(15)
                Send-FoundryAutopilotProgress $context 'progress' 'Collecting hardware hash.'
                try { $identity = Get-FoundryAutopilotHardwareIdentity $context $deadline }
                catch {
                    $Token.ThrowIfCancellationRequested()
                    throw (Get-FoundryAutopilotFailureCode $_ 'CaptureUnavailable')
                }
                $request = @{importId=[Guid]::NewGuid().ToString();serialNumber=$identity.serialNumber;hardwareIdentifier=$identity.hardwareIdentifier;groupTag=[string]$command.groupTag}
                Send-FoundryAutopilotProgress $context 'progress' 'Waiting for device registration in Microsoft Intune.'
                $result = Invoke-FoundryAutopilotImport $context $request $deadline
                $Token.ThrowIfCancellationRequested()
                Write-FoundryAutopilotOutcome $config $result
                Send-FoundryAutopilotProgress $context 'result' @{status=$result.status;code=$result.code}
                if ($result.status -eq 'completed') {
                    Wait-FoundryAutopilot $context 10 ((& $context.Now).AddSeconds(15))
                    Start-Process -FilePath "$env:SystemRoot\System32\shutdown.exe" -ArgumentList '/r /t 0 /f' -WindowStyle Hidden
                    return
                }
            }
            catch {
                $Token.ThrowIfCancellationRequested()
                $result = @{status='failed';code=(Get-FoundryAutopilotFailureCode $_ 'RequestFailed')}
                Write-FoundryAutopilotOutcome $config $result
                Send-FoundryAutopilotProgress $context 'result' $result
            }
        }
    }
    catch {
        if (-not $Token.IsCancellationRequested) {
            $result = @{status='failed';code=(Get-FoundryAutopilotFailureCode $_ 'AuthenticationFailed')}
            Write-FoundryAutopilotOutcome $config $result
            Send-FoundryAutopilotProgress $context 'fatal' $result
        }
    }
    finally { $context.AccessToken = $null }
}
