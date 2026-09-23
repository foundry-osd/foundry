$stateRoot = Join-Path $env:SystemRoot 'Temp\Foundry\State\PreOobe'
$payloadRoot = Join-Path $env:SystemRoot 'Temp\Foundry\Payloads'
$resultPath = Join-Path $stateRoot 'execution-result.json'
$manifest = Get-Content -LiteralPath (Join-Path $stateRoot 'pre-oobe-manifest.json') -Raw | ConvertFrom-Json
$runnerLease = [IO.File]::Open((Join-Path $stateRoot 'runner.lease'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$script:attempt = $null
$script:attemptFailed = $false
$script:attemptActive = $false

function Write-FoundryResult {
    $temporaryPath = Join-Path $stateRoot ('.execution-result.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($script:attempt | ConvertTo-Json -Depth 12))
        $stream = [IO.File]::Open($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if ([IO.File]::Exists($resultPath)) { [IO.File]::Replace($temporaryPath, $resultPath, [NullString]::Value) }
        else { [IO.File]::Move($temporaryPath, $resultPath) }
    }
    finally { if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) } }
}

function Get-FoundryInputPath {
    param($InputRecord)
    $root = [IO.Path]::GetFullPath($payloadRoot).TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath((Join-Path $payloadRoot $InputRecord.relativePath))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Input is outside the owned payload root.' }
    # Never follow a junction or symbolic link during disposal.
    $current = $path
    while ($current.Length -ge $root.TrimEnd('\').Length) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Input contains a reparse point.' }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    return $path
}

function Dispose-FoundryInputs {
    param($Record, [switch]$Consumed)
    if (@($Record.inputs | Where-Object { $_.disposition -ne 'disposed' }).Count -eq 0) { return }
    foreach ($inputRecord in @($Record.inputs)) { $inputRecord.disposition = 'disposal-pending' }
    try { Write-FoundryResult }
    finally {
        foreach ($inputRecord in @($Record.inputs)) {
            try {
                $path = Get-FoundryInputPath $inputRecord
                if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
                $inputRecord.disposition = if ([IO.File]::Exists($path)) { 'disposal-pending' } else { 'disposed' }
                $inputRecord.requiresRestaging = -not $Consumed
            }
            catch {
                $inputRecord.disposition = 'disposal-pending'
                $inputRecord.requiresRestaging = $true
                Write-Warning 'An owned provisioning input could not be disposed.'
            }
        }
    }
    Write-FoundryResult
    if (@($Record.inputs | Where-Object { $_.disposition -eq 'disposal-pending' }).Count -gt 0) {
        throw 'Owned input disposal remains incomplete.'
    }
}

function Initialize-FoundryAttempt {
    $script:attempt = [pscustomobject]@{
        operationId = $manifest.operationId
        attemptId = [Guid]::NewGuid().ToString('N')
        outcome = 'running'
        scripts = @($manifest.scripts | ForEach-Object {
            [pscustomobject]@{
                scriptId = $_.id
                outcome = 'staged'
                inputs = @($_.inputs | ForEach-Object {
                    [pscustomobject]@{ relativePath = $_.relativePath; sensitive = $_.sensitive; disposition = 'staged'; requiresRestaging = $false }
                })
            }
        })
    }
    if (Test-Path -LiteralPath $resultPath) {
        $previous = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        if ($previous.operationId -ne $manifest.operationId) { throw 'A different operation owns the existing first-boot result; explicit restaging is required.' }
        $script:attempt = $previous
        if ($previous.outcome -eq 'completed') { return $false }
        foreach ($record in @($previous.scripts)) {
            if ($record.outcome -in @('running', 'staged')) { $record.outcome = 'interrupted' }
            if (@($record.inputs | Where-Object { $_.disposition -eq 'disposal-pending' }).Count -gt 0) { $record.outcome = 'interrupted' }
            # Reconcile every record even when recording one disposal fails. The final
            # write still reports failure; it must not leave later sensitive inputs behind.
            try { Dispose-FoundryInputs $record } catch { Write-Warning 'Recovered input disposal or result recording did not complete.' }
            foreach ($inputRecord in @($record.inputs)) { $inputRecord.requiresRestaging = $true }
        }
        $previous.outcome = 'interrupted'
        Write-FoundryResult
        throw 'Previous first-boot execution did not complete. Inputs require restaging; scripts were not replayed.'
    }
    $script:attemptActive = $true
    Write-FoundryResult
    return $true
}

function Invoke-FoundryScript {
    param([string]$ScriptId, [string]$ScriptPath, [string[]]$Arguments = @(), [int]$TimeoutSeconds = 0, [switch]$ContinueOnError)
    $record = @($script:attempt.scripts | Where-Object { $_.scriptId -eq $ScriptId })[0]
    $record.outcome = 'running'
    $scriptOutcome = 'failed'
    try {
        Write-FoundryResult
        foreach ($inputRecord in @($record.inputs)) {
            if (-not [IO.File]::Exists((Get-FoundryInputPath $inputRecord))) {
                $inputRecord.requiresRestaging = $true
                throw 'Provisioning input is unavailable and requires restaging.'
            }
        }
        # Importers and installers promptly delete their own input in finally blocks.
        # Record the disposal intent before those consumers can remove any bytes.
        foreach ($inputRecord in @($record.inputs)) { $inputRecord.disposition = 'disposal-pending' }
        Write-FoundryResult
        Invoke-FoundryProcess -ScriptPath $ScriptPath -Arguments $Arguments -TimeoutSeconds $TimeoutSeconds -ContinueOnError:$ContinueOnError
        $scriptOutcome = 'completed'
    }
    catch {
        $script:attemptFailed = $true
        if ($ContinueOnError) { Write-Warning $_ } else { throw }
    }
    finally {
        Dispose-FoundryInputs $record -Consumed:($scriptOutcome -eq 'completed')
        $record.outcome = $scriptOutcome
        Write-FoundryResult
    }
}

function Remove-FoundryRetiredGeneration {
    $legacyRoot = Join-Path $env:SystemRoot 'Temp\Foundry\PreOobe'
    $legacyManifest = Join-Path $legacyRoot 'pre-oobe-manifest.json'
    if (-not [IO.File]::Exists($legacyManifest)) { return }
    foreach ($hookName in @('SetupComplete.cmd', 'OOBE.cmd')) {
        $hook = Join-Path $env:SystemRoot ('Setup\Scripts\' + $hookName)
        if ([IO.File]::Exists($hook) -and ([IO.File]::ReadAllText($hook)).IndexOf('\Temp\Foundry\PreOobe', [StringComparison]::OrdinalIgnoreCase) -ge 0) { return }
    }
    $legacy = Get-Content -LiteralPath $legacyManifest -Raw | ConvertFrom-Json
    if (@($legacy.scripts).Count -gt 256) { return }
    $ownedPaths = @('Invoke-FoundryPreOobe.ps1')
    foreach ($entry in @($legacy.scripts)) {
        if ([IO.Path]::GetFileName($entry.fileName) -ne $entry.fileName -or @($entry.dataFiles).Count -gt 256) { return }
        $ownedPaths += 'Scripts\' + $entry.fileName
        foreach ($dataFile in @($entry.dataFiles)) { $ownedPaths += 'Data\' + $dataFile }
    }
    $rootPrefix = [IO.Path]::GetFullPath($legacyRoot).TrimEnd('\') + '\'
    foreach ($relative in $ownedPaths) {
        $path = [IO.Path]::GetFullPath((Join-Path $legacyRoot $relative))
        if (-not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { return }
        $parent = $path
        while ($parent.Length -ge $legacyRoot.Length) {
            if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { return }
            $parent = [IO.Path]::GetDirectoryName($parent)
        }
    }
    foreach ($relative in $ownedPaths) {
        $path = Join-Path $legacyRoot $relative
        if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
    }
    [IO.File]::Delete($legacyManifest)
}

function Complete-FoundryAttempt {
    if (-not $script:attemptActive) { return }
    $recordingFailure = $null
    foreach ($record in @($script:attempt.scripts)) {
        if ($record.outcome -in @('staged', 'running')) { $record.outcome = 'interrupted' }
        try { Dispose-FoundryInputs $record } catch { $recordingFailure = $_ }
    }
    $script:attempt.outcome = if ($script:attemptFailed -or $recordingFailure -or @($script:attempt.scripts | Where-Object { $_.outcome -ne 'completed' }).Count -gt 0) { 'failed' } else { 'completed' }
    Write-FoundryResult
    if ($recordingFailure) { throw $recordingFailure }
    if ($script:attempt.outcome -eq 'completed') {
        try { Remove-FoundryRetiredGeneration } catch { Write-Warning 'Retired runtime cleanup could not complete.' }
    }
}
