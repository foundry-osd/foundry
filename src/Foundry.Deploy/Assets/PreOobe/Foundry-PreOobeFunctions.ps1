$FoundryStateRoot = $PSScriptRoot

function Resolve-FoundryOwnedPath {
    param([string]$Root, [string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains(':')) { throw 'invalid_owned_path' }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    if (-not $path.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'invalid_owned_path' }
    for ($current = $path; -not [string]::IsNullOrEmpty($current); $current = [IO.Path]::GetDirectoryName($current)) {
        if (([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) -and (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'invalid_owned_path' }
    }
    return $path
}

function Read-FoundryResults {
    param([string]$ResultsPath)
    if (-not [IO.File]::Exists($ResultsPath)) { return @() }
    $parsed = Get-Content -LiteralPath $ResultsPath -Raw | ConvertFrom-Json
    foreach ($entry in $parsed) { Write-Output $entry }
}

function Write-FoundryResults {
    param([string]$ResultsPath, [object[]]$Results)
    $root = [IO.Path]::GetDirectoryName($ResultsPath)
    $checked = Resolve-FoundryOwnedPath -Root $root -RelativePath ([IO.Path]::GetFileName($ResultsPath))
    $temporary = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.results.tmp')
    try {
        $json = ConvertTo-Json -InputObject @($Results) -Depth 8
        [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
        if ([IO.File]::Exists($checked)) { [IO.File]::Replace($temporary, $checked, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $checked) }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Save-FoundryActionResult {
    param([string]$ResultsPath, $Result)
    $results = @(Read-FoundryResults $ResultsPath | Where-Object { $_.id -ne $Result.id }) + @($Result)
    Write-FoundryResults -ResultsPath $ResultsPath -Results $results
}

function Write-FoundryDriverOutcome {
    param($Outcome)
    Write-FoundryResults -ResultsPath (Join-Path $FoundryStateRoot 'driver-outcome.json') -Results @($Outcome)
}

function Join-FoundryProcessArguments {
    param([string[]]$Arguments)
    $values = foreach ($value in $Arguments) {
        $builder = [Text.StringBuilder]::new(); [void]$builder.Append('"'); $slashes = 0
        foreach ($character in $value.ToCharArray()) {
            if ($character -eq '\') { $slashes++; continue }
            if ($character -eq '"') { [void]$builder.Append(('\' * ($slashes * 2 + 1))) }
            else { [void]$builder.Append(('\' * $slashes)) }
            [void]$builder.Append($character); $slashes = 0
        }
        [void]$builder.Append(('\' * ($slashes * 2))); [void]$builder.Append('"'); $builder.ToString()
    }
    return $values -join ' '
}

function Invoke-FoundryInstaller {
    param([string]$FilePath, [string[]]$Arguments, [int]$TimeoutSeconds = 1800)
    $process = Start-Process -FilePath $FilePath -ArgumentList (Join-FoundryProcessArguments $Arguments) -WindowStyle Hidden -PassThru
    $confirmedExit = $false
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw [TimeoutException]::new('native_wait_timeout') }
        $confirmedExit = $true
        return $process.ExitCode
    }
    catch {
        if (-not $confirmedExit) {
            $error = [InvalidOperationException]::new('native_ownership_uncertain', $_.Exception)
            $error.Data['ProcessId'] = $process.Id
            try { $error.Data['ProcessStartUtc'] = $process.StartTime.ToUniversalTime().ToString('o') } catch { }
            $error.Data['OwnershipUncertain'] = $true
            $state = [pscustomobject]@{ processId=$process.Id; processStartUtc=$error.Data['ProcessStartUtc']; errorCode='native_ownership_uncertain'; timedOut=($_.Exception -is [TimeoutException]) }
            try { Write-FoundryResults -ResultsPath (Join-Path $FoundryStateRoot 'native-uncertainty.json') -Results @($state) }
            catch { $error.Data['JournalWriteFailed'] = $true }
            throw $error
        }
        throw
    }
    finally { $process.Dispose() }
}

function Remove-FoundryActionInputs {
    param($Action, [string]$Root, [bool]$Succeeded = $false)
    $failed = $false
    foreach ($inputFile in @($Action.dataFiles)) {
        if ($inputFile.owningActionId -ne $Action.id -or ($inputFile.isSensitive -and $inputFile.cleanupDisposition -ne 'SecretAlways')) { throw 'invalid_input_ownership' }
        if ($inputFile.cleanupDisposition -ne 'SecretAlways' -and -not $Succeeded) { continue }
        try {
            $path = Resolve-FoundryOwnedPath -Root (Join-Path $Root 'Data') -RelativePath ([string]$inputFile.fileName)
            if ([IO.File]::Exists($path)) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
        }
        catch { $failed = $true }
    }
    if ($failed) { throw 'input_cleanup_failed' }
}

function Invoke-FoundryAction {
    param($Action, [int]$Attempt, [string]$ResultsPath)
    if ($Attempt -lt 1 -or $Attempt -gt 3) { throw 'retry_limit' }
    $root = [IO.Path]::GetDirectoryName($ResultsPath)
    $previous = @(Read-FoundryResults $ResultsPath | Where-Object { $_.id -eq $Action.id })
    if ($previous.Count -gt 1) { throw 'invalid_results' }
    if ($previous.Count -eq 0 -and $Attempt -ne 1) { throw 'invalid_attempt' }
    if ($previous.Count -eq 1 -and ($previous[0].status -eq 'running' -or $previous[0].errorCode -eq 'native_ownership_uncertain' -or $previous[0].primaryErrorCode -eq 'native_ownership_uncertain')) { throw 'native_recovery_required' }
    if ($previous.Count -eq 1 -and $previous[0].status -eq 'succeeded') { return $previous[0] }
    if ($previous.Count -eq 1 -and $Attempt -ne ([int]$previous[0].attempt + 1)) { throw 'invalid_attempt' }
    $result = [pscustomobject]@{ id=$Action.id; status='running'; exitCode=$null; rebootRequired=$false; attempt=$Attempt;
        startedUtc=[DateTimeOffset]::UtcNow.ToString('o'); completedUtc=$null; errorCode=$null }
    Save-FoundryActionResult $ResultsPath $result
    try {
        foreach ($inputFile in @($Action.dataFiles)) {
            if ($inputFile.owningActionId -ne $Action.id -or ($inputFile.isSensitive -and $inputFile.cleanupDisposition -ne 'SecretAlways')) { throw 'invalid_input_ownership' }
            $inputPath = Resolve-FoundryOwnedPath -Root (Join-Path $root 'Data') -RelativePath ([string]$inputFile.fileName)
            if (-not [IO.File]::Exists($inputPath)) {
                if ($inputFile.cleanupDisposition -eq 'SecretAlways') { throw 'secret_reentry_required' }
                throw 'required_input_missing'
            }
        }
        $scriptPath = Resolve-FoundryOwnedPath -Root (Join-Path $root 'Scripts') -RelativePath ([string]$Action.fileName)
        if (-not [IO.File]::Exists($scriptPath)) { throw 'required_script_missing' }
        $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath) + @($Action.arguments)
        $driverOutcomePath = Resolve-FoundryOwnedPath -Root $root -RelativePath 'driver-outcome.json'
        if ($Action.id -eq 'driver-pack' -and [IO.File]::Exists($driverOutcomePath)) { [IO.File]::Delete($driverOutcomePath) }
        $code = Invoke-FoundryInstaller -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Arguments $arguments -TimeoutSeconds 1800
        if ([IO.File]::Exists((Join-Path $root 'native-uncertainty.json'))) { throw 'native_ownership_uncertain' }
        $result.exitCode = [int]$code
        $result.rebootRequired = $Action.id -eq 'driver-pack' -and $code -eq 3010
        if ($code -eq 0 -or $result.rebootRequired) { $result.status = 'succeeded' }
        else { $result.status = 'failed'; $result.errorCode = 'native_exit_failed' }
        if ($Action.id -eq 'driver-pack' -and [IO.File]::Exists($driverOutcomePath)) {
            $driver = @(Read-FoundryResults $driverOutcomePath)
            if ($driver.Count -ne 1) { throw 'invalid_driver_outcome' }
            $result.rebootRequired = [bool]$driver[0].rebootRequired
            if ($null -ne $driver[0].exitCode) { $result.exitCode = [int]$driver[0].exitCode }
            if ($driver[0].failed) { $result.status='failed'; $result.errorCode='driver_action_failed' }
            if ($driver[0].cleanupFailed) { $result.status='failed'; $result.errorCode='driver_registry_restore_failed' }
        }
    }
    catch {
        $result.status = 'failed'
        $code = $_.Exception.Message
        $result.errorCode = if ($code -in @('secret_reentry_required','required_input_missing','required_script_missing','invalid_input_ownership','invalid_owned_path','native_ownership_uncertain')) { $code } else { 'action_failed' }
        if ($_.Exception.Data['OwnershipUncertain']) {
            $result.errorCode = 'native_ownership_uncertain'
            $result | Add-Member -NotePropertyName processId -NotePropertyValue $_.Exception.Data['ProcessId']
            $result | Add-Member -NotePropertyName processStartUtc -NotePropertyValue $_.Exception.Data['ProcessStartUtc']
        }
        elseif ($result.errorCode -eq 'native_ownership_uncertain') {
            $native = @(Read-FoundryResults (Join-Path $root 'native-uncertainty.json'))
            if ($native.Count -eq 1) {
                $result | Add-Member -NotePropertyName processId -NotePropertyValue $native[0].processId
                $result | Add-Member -NotePropertyName processStartUtc -NotePropertyValue $native[0].processStartUtc
            }
        }
    }
    finally {
        try { Remove-FoundryActionInputs -Action $Action -Root $root -Succeeded ($result.status -eq 'succeeded') }
        catch {
            $result | Add-Member -NotePropertyName primaryErrorCode -NotePropertyValue $result.errorCode -Force
            $result.status = 'failed'; $result.errorCode = 'input_cleanup_failed'
        }
        $result.completedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Save-FoundryActionResult $ResultsPath $result
    }
    return $result
}

function Invoke-FoundryPlan {
    param([string]$ManifestPath, [string]$ResultsPath, [switch]$RetryFailed, [string]$ActionId, [switch]$CleanupSecretsOnly)
    $root = [IO.Path]::GetDirectoryName($ResultsPath)
    $lockPath = Resolve-FoundryOwnedPath -Root $root -RelativePath 'execution.lock'
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $actions = @()
    $planFailure = $null
    try {
        $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
        if ($manifest.version -ne 1) { throw 'unsupported_manifest' }
        $actions = @($manifest.scripts)
        if (@($actions | Group-Object id | Where-Object Count -ne 1).Count -gt 0) { throw 'duplicate_action' }
        if ($ActionId -and -not $RetryFailed) { throw 'explicit_retry_required' }
        if ($ActionId -and @($actions | Where-Object id -eq $ActionId).Count -ne 1) { throw 'unknown_action' }
        if (-not $CleanupSecretsOnly) {
            if ([IO.File]::Exists((Join-Path $root 'native-uncertainty.json'))) { throw 'native_recovery_required' }
            foreach ($old in @(Read-FoundryResults $ResultsPath)) {
                if ($old.status -eq 'running') { $old.status='interrupted'; $old.errorCode='native_ownership_uncertain'; Save-FoundryActionResult $ResultsPath $old }
                if ($old.errorCode -eq 'native_ownership_uncertain' -or $old.primaryErrorCode -eq 'native_ownership_uncertain') { throw 'native_recovery_required' }
            }
            foreach ($action in $actions) {
                $previous = @(Read-FoundryResults $ResultsPath | Where-Object { $_.id -eq $action.id })
                if ($ActionId -and $action.id -ne $ActionId) { continue }
                if ($previous.Count -eq 1 -and $previous[0].status -eq 'succeeded') { continue }
                $attempt = if ($previous.Count -eq 1) { [int]$previous[0].attempt + 1 } else { 1 }
                if ($RetryFailed) {
                    if ($previous.Count -ne 1 -or $previous[0].status -notin @('failed','interrupted')) { continue }
                } elseif ($previous.Count -eq 1 -and $previous[0].status -ne 'staged') { continue }
                if ($attempt -gt 3) { continue }
                $dependencyFailed = $false
                foreach ($dependency in @($action.dependsOn)) {
                    $dependencyResult = @(Read-FoundryResults $ResultsPath | Where-Object { $_.id -eq $dependency -and $_.status -eq 'succeeded' })
                    if ($dependencyResult.Count -ne 1) { $dependencyFailed=$true }
                }
                if ($dependencyFailed) {
                    $skipped = [pscustomobject]@{id=$action.id;status='skipped_dependency';exitCode=$null;rebootRequired=$false;attempt=($attempt-1);startedUtc=[DateTimeOffset]::UtcNow.ToString('o');completedUtc=[DateTimeOffset]::UtcNow.ToString('o');errorCode='dependency_failed'}
                    Save-FoundryActionResult $ResultsPath $skipped
                    continue
                }
                $result = Invoke-FoundryAction -Action $action -Attempt $attempt -ResultsPath $ResultsPath
                if ($result.errorCode -eq 'native_ownership_uncertain' -or $result.primaryErrorCode -eq 'native_ownership_uncertain') { break }
            }
        }
    }
    catch { $planFailure=$_; throw }
    finally {
        try {
            $cleanupFailed=$false
            foreach ($action in $actions) {
                try { Remove-FoundryActionInputs -Action $action -Root $root }
                catch {
                    $cleanupFailed=$true
                    $previous = @(Read-FoundryResults $ResultsPath | Where-Object { $_.id -eq $action.id })
                    if ($previous.Count -eq 1) {
                        $previous[0] | Add-Member -NotePropertyName primaryErrorCode -NotePropertyValue $previous[0].errorCode -Force
                        $previous[0].status='failed'; $previous[0].errorCode='input_cleanup_failed'; Save-FoundryActionResult $ResultsPath $previous[0]
                    }
                }
            }
            if ($cleanupFailed) {
                if ($null -ne $planFailure) { $planFailure.Exception.Data['SecretCleanupFailed']=$true }
                else { throw 'input_cleanup_failed' }
            }
        }
        finally { $lock.Dispose() }
    }
    return @(Read-FoundryResults $ResultsPath)
}

function Get-FoundryDriverPath {
    $path = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\UnattendSettings\PnPUnattend\DriverPaths\1'
    $key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($path,$false)
    try {
        if ($null -eq $key -or $key.GetValueNames() -notcontains 'Path') { return [pscustomobject]@{Exists=$false;Value=$null;Kind=$null} }
        return [pscustomobject]@{Exists=$true;Value=$key.GetValue('Path',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);Kind=$key.GetValueKind('Path').ToString()}
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
}

function Set-FoundryDriverPath {
    param($State)
    $path = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\UnattendSettings\PnPUnattend\DriverPaths\1'
    $key = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($path,$true)
    try {
        if ($State.Exists) { $key.SetValue('Path',$State.Value,[Microsoft.Win32.RegistryValueKind]$State.Kind) }
        else { $key.DeleteValue('Path',$false) }
    }
    finally { $key.Dispose() }
}
