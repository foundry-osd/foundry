@echo off
setlocal
set "DOTNET_BUNDLE_EXTRACT_BASE_DIR=%SystemRoot%\Temp\Foundry\Runtime\PreOobe\Bundle"
"%SystemRoot%\Temp\Foundry\Runtime\PreOobe\Foundry.PostInstall.exe" --setup
exit /b %ERRORLEVEL%
