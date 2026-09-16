@echo off
echo =============================================
echo   HerdWoW Installer - Build Script
echo =============================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not installed!
    pause
    exit /b 1
)

echo Building installer...
echo.

dotnet publish HerdWowInstaller\HerdWowInstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\build

if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    pause
    exit /b 1
)

echo.
echo =============================================
echo   BUILD SUCCESSFUL!
echo =============================================
echo.
echo Your installer is: build\HerdWoW-Setup.exe
echo.
echo BEFORE distributing:
echo   1. Build WowLauncher.exe from the WowLauncher project
echo   2. Upload WowLauncher.exe to your GitHub release with tag "launcher-v1"
echo      at: https://github.com/wizardofth3moon/herd-wow/releases/new
echo   3. Give friends HerdWoW-Setup.exe - thats all they need!
echo.
pause
