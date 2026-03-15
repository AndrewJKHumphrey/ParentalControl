@echo off
:: Builds the UI in Release configuration and launches it.
:: The Debug build skips the login window; this script ensures the full
:: authentication flow is active for testing.
::
:: The service must already be running (install.ps1 handles that separately).
:: Run this script as a normal user — no elevation required for the UI.

setlocal
set SOLUTION_DIR=%~dp0
set UI_PROJ=%SOLUTION_DIR%ParentalControl.UI\ParentalControl.UI.csproj
set OUT_DIR=%SOLUTION_DIR%publish\ui-release-test

echo.
echo Building UI in Release configuration...
dotnet publish "%UI_PROJ%" -c Release -r win-x64 --self-contained true -o "%OUT_DIR%"

if %errorLevel% neq 0 (
    echo.
    echo Build failed.
    pause
    exit /b %errorLevel%
)

echo.
echo Launching ParentalControl.UI (Release)...
start "" "%OUT_DIR%\ParentalControl.UI.exe"
endlocal
