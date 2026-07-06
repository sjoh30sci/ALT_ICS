@echo off
NET SESSION >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo Running with administrator privileges.
    cd /d "%~dp0.."
    powershell -ExecutionPolicy Bypass -File "scripts\run_deploy_elevated.ps1"
    pause
    exit /b %ERRORLEVEL%
)

echo Requesting administrator privileges...
powershell -Command "Start-Process -FilePath 'powershell.exe' -ArgumentList '-ExecutionPolicy Bypass -File \"%~dp0run_deploy_elevated.ps1\"' -Verb RunAs -Wait"
echo.
echo Deployment finished. Check %TEMP%\deploy_output.log for details.
pause
