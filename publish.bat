@echo off
echo ===================================================
echo   Publishing Autonomuse for Inno Setup Installer
echo ===================================================
echo.

dotnet publish -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -o bin\publish

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Publish failed with exit code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo   Publish complete! Output saved to bin\publish
echo   You can now build setup.iss with Inno Setup.
echo ===================================================
echo.
pause
