@echo off
setlocal
cd /d "%~dp0"

echo Building RomWBW Disk Explorer...
dotnet build "RomWBWDE\RomWBWDE.csproj" -c Release --nologo
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

set "EXE=%~dp0RomWBWDE\bin\Release\net8.0-windows\RomWBWDE.exe"
if not exist "%EXE%" (
    echo Could not find:
    echo   %EXE%
    pause
    exit /b 1
)

start "" "%EXE%"
