@echo off
setlocal
cd /d "%~dp0"

echo Building RomWBW Disk Explorer...
dotnet publish "RomWBWDE\RomWBWDE.csproj" -c Release -r win-x64 --self-contained false -o "Release" --nologo
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

rem The program is in Release\. Drop the per-project build folders.
rmdir /s /q "RomWBWDE\bin" 2>nul
rmdir /s /q "RomWBWDE\obj" 2>nul
rmdir /s /q "RomWBWDE.Core\bin" 2>nul
rmdir /s /q "RomWBWDE.Core\obj" 2>nul

set "EXE=%~dp0Release\RomWBWDE.exe"
if not exist "%EXE%" (
    echo Could not find:
    echo   %EXE%
    pause
    exit /b 1
)

start "" "%EXE%"
