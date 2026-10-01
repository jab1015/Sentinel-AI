@echo off
setlocal
set "SCRIPT=%~dp0Build-SentinelAI-TestPackage-Local.ps1"

if not exist "%SCRIPT%" (
  echo ERROR: Build-SentinelAI-TestPackage-Local.ps1 was not found.
  pause
  exit /b 2
)

echo Sentinel AI LocalDev build + install + launch
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Install
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo.
  echo BUILD / INSTALL / LAUNCH FAILED.
  echo Review the error above. If a SentinelAI-LaunchDiagnostics-*.txt file was created,
  echo send that file for diagnosis.
  pause
  exit /b %RC%
)

exit /b 0
