@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-webgl.ps1" -Server 101.43.181.45
set "result=%errorlevel%"
if not "%result%"=="0" echo Deployment failed. See the details above.
pause
exit /b %result%
