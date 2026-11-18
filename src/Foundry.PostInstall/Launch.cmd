@echo off
setlocal
"%SystemRoot%\Temp\Foundry\Runtime\PreOobe\Foundry.PostInstall.exe" --setup
exit /b %ERRORLEVEL%
