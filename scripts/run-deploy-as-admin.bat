@echo off
NET SESSION >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo === PLEASE RIGHT-CLICK and "Run as Administrator" ===
    echo.
    pause
    exit /b 1
)

cd /d "%~dp0.."
echo === ALT_ICS Deployment ===
echo.
powershell -ExecutionPolicy Bypass -File "scripts\run_deploy_elevated.ps1"
echo.
echo === Done ===
echo Check %%TEMP%%\deploy_output.log for details.
pause
