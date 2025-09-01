@echo off
echo Simple Desktop Mascot Build Script
echo.

echo Building project...
dotnet build -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo BUILD SUCCESS!
    echo.
    echo Executable location:
    echo bin\Release\net6.0-windows\DesktopMascot.exe
    echo.
    echo To run:
    echo cd bin\Release\net6.0-windows
    echo DesktopMascot.exe
    echo.
    echo Before running:
    echo - Prepare a mascot image (PNG, JPG, etc.)
    echo - Check RSS feed URLs in settings
    echo - Ensure not blocked by antivirus
) else (
    echo.
    echo BUILD FAILED!
    echo Please check errors above.
    echo.
    echo Common solutions:
    echo 1. Install .NET 6.0 SDK from https://dotnet.microsoft.com/download
    echo 2. Run: dotnet --version to verify installation
)

pause