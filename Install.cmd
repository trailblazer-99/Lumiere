@echo off
setlocal
cd /d "%~dp0"
echo ==========================================================
echo        Lumiere Media Player - Universal Launcher
echo ==========================================================
echo.
set "SCRIPT_PATH=%~dp0scripts\Install-Lumiere.ps1"
if not exist "%SCRIPT_PATH%" set "SCRIPT_PATH=%~dp0Install-Lumiere.ps1"
if not exist "%SCRIPT_PATH%" set "SCRIPT_PATH=%~dp0install.ps1"

echo Launching installation with PowerShell...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_PATH%"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Installation script exited with error code %ERRORLEVEL%.
    pause
)
endlocal
