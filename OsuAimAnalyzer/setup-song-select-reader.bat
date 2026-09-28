@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup_native.ps1"
if %errorlevel% neq 0 (
  echo.
  echo Song-select reader setup failed.
  pause
  exit /b 1
)
echo.
echo Song-select reader installed.
pause
