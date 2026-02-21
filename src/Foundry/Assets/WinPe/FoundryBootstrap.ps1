$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$logPath = 'X:\Windows\Temp\FoundryBootstrap.log'

try {
    "[$(Get-Date -Format o)] Foundry bootstrap started." | Out-File -FilePath $logPath -Encoding utf8 -Append
}
catch {
    # Keep startup resilient if logging fails.
}
