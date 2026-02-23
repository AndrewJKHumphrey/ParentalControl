@echo off
:: Self-elevate to Administrator if not already elevated
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

:: Run the installer, bypassing execution policy for this invocation only
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"

if %errorLevel% neq 0 (
    echo.
    echo Installation failed. See errors above.
    pause
    exit /b %errorLevel%
)

pause
