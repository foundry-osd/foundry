@echo off
setlocal

set "FOUNDRY_DEPLOYMENT_MODE=Recovery"
set "BOOTSTRAP_PATH=%SystemRoot%\System32\FoundryBootstrap.ps1"

if not exist "%BOOTSTRAP_PATH%" (
    echo Missing Foundry bootstrap: %BOOTSTRAP_PATH%
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%BOOTSTRAP_PATH%"

exit /b %ERRORLEVEL%
