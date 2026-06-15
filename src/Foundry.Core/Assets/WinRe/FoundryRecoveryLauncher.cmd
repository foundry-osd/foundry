@echo off
setlocal

set "FOUNDRY_DEPLOYMENT_MODE=Recovery"
set "FOUNDRY_ROOT=X:\Foundry"
set "RUNTIME_ROOT=%FOUNDRY_ROOT%\Runtime"
set "CONFIG_ROOT=%FOUNDRY_ROOT%\Config"
set "TOOLS_ROOT=%FOUNDRY_ROOT%\Tools"
set "LOG_ROOT=%FOUNDRY_ROOT%\Logs"
set "LOG_PATH=%LOG_ROOT%\FoundryRecoveryLauncher.log"

if /I "%PROCESSOR_ARCHITECTURE%"=="ARM64" (
    set "RID=win-arm64"
    set "SEVENZIP_RID=arm64"
) else (
    set "RID=win-x64"
    set "SEVENZIP_RID=x64"
)

set "CONNECT_ROOT=%RUNTIME_ROOT%\Foundry.Connect\%RID%"
set "CONNECT_EXE=%CONNECT_ROOT%\Foundry.Connect.exe"
set "CONNECT_CONFIG=%CONFIG_ROOT%\foundry.connect.config.json"
set "DEPLOY_ROOT=%RUNTIME_ROOT%\Foundry.Deploy\%RID%"
set "DEPLOY_ARCHIVE=%RUNTIME_ROOT%\Foundry.Deploy\Foundry.Deploy-%RID%.zip"
set "DEPLOY_EXE=%DEPLOY_ROOT%\Foundry.Deploy.exe"
set "DEPLOY_URL=https://github.com/foundry-osd/foundry/releases/latest/download/Foundry.Deploy-%RID%.zip"
set "CURL_EXE=%SystemRoot%\System32\curl.exe"
set "SEVENZIP_EXE=%TOOLS_ROOT%\7zip\%SEVENZIP_RID%\7za.exe"

mkdir "%LOG_ROOT%" >nul 2>&1
mkdir "%RUNTIME_ROOT%\Foundry.Deploy" >nul 2>&1
echo [%date% %time%] Starting Foundry OS Recovery launcher.>"%LOG_PATH%"
echo [%date% %time%] Runtime identifier: %RID%.>>"%LOG_PATH%"

if not exist "%CONNECT_EXE%" (
    echo Missing Foundry.Connect runtime: %CONNECT_EXE%
    echo [%date% %time%] Missing Foundry.Connect runtime: %CONNECT_EXE%.>>"%LOG_PATH%"
    exit /b 1
)

if not exist "%CURL_EXE%" (
    echo Missing curl.exe: %CURL_EXE%
    echo [%date% %time%] Missing curl.exe: %CURL_EXE%.>>"%LOG_PATH%"
    exit /b 1
)

if not exist "%SEVENZIP_EXE%" (
    echo Missing 7-Zip runtime: %SEVENZIP_EXE%
    echo [%date% %time%] Missing 7-Zip runtime: %SEVENZIP_EXE%.>>"%LOG_PATH%"
    exit /b 1
)

echo Starting Foundry.Connect...
echo [%date% %time%] Starting Foundry.Connect.>>"%LOG_PATH%"
if exist "%CONNECT_CONFIG%" (
    start /wait "" /d "%CONNECT_ROOT%" "%CONNECT_EXE%" --config "%CONNECT_CONFIG%"
) else (
    start /wait "" /d "%CONNECT_ROOT%" "%CONNECT_EXE%"
)

set "CONNECT_EXIT=%ERRORLEVEL%"
echo [%date% %time%] Foundry.Connect exited with %CONNECT_EXIT%.>>"%LOG_PATH%"
if not "%CONNECT_EXIT%"=="0" (
    exit /b %CONNECT_EXIT%
)

if not exist "%DEPLOY_EXE%" (
    echo Downloading Foundry.Deploy...
    echo [%date% %time%] Downloading Foundry.Deploy from %DEPLOY_URL%.>>"%LOG_PATH%"
    "%CURL_EXE%" --fail --location --show-error --output "%DEPLOY_ARCHIVE%" --url "%DEPLOY_URL%" >>"%LOG_PATH%" 2>&1
    if errorlevel 1 (
        echo Foundry.Deploy download failed.
        echo [%date% %time%] Foundry.Deploy download failed.>>"%LOG_PATH%"
        exit /b 1
    )

    if exist "%DEPLOY_ROOT%" rd /s /q "%DEPLOY_ROOT%"
    mkdir "%DEPLOY_ROOT%" >nul 2>&1
    echo Extracting Foundry.Deploy...
    echo [%date% %time%] Extracting Foundry.Deploy.>>"%LOG_PATH%"
    "%SEVENZIP_EXE%" x -y "%DEPLOY_ARCHIVE%" "-o%DEPLOY_ROOT%" >>"%LOG_PATH%" 2>&1
    if errorlevel 1 (
        echo Foundry.Deploy extraction failed.
        echo [%date% %time%] Foundry.Deploy extraction failed.>>"%LOG_PATH%"
        exit /b 1
    )
)

if not exist "%DEPLOY_EXE%" (
    echo Missing Foundry.Deploy runtime: %DEPLOY_EXE%
    echo [%date% %time%] Missing Foundry.Deploy runtime after download: %DEPLOY_EXE%.>>"%LOG_PATH%"
    exit /b 1
)

echo Starting Foundry.Deploy...
echo [%date% %time%] Starting Foundry.Deploy.>>"%LOG_PATH%"
start "" /d "%DEPLOY_ROOT%" "%DEPLOY_EXE%"

exit /b %ERRORLEVEL%
