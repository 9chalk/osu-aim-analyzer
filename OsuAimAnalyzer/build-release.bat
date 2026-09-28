@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK was not found.
  echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

dotnet restore
if errorlevel 1 goto :fail

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :fail

echo.
echo Build complete:
echo %~dp0bin\Release\net8.0-windows\win-x64\publish\OsuAimAnalyzer.exe
pause
exit /b 0

:fail
echo.
echo Build failed. Scroll up for the compiler error.
pause
exit /b 1
