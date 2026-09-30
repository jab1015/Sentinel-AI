@echo off
setlocal
set "SCRIPT=%~dp0Install-SentinelAI-Test.ps1"

if not exist "%SCRIPT%" (
  echo ERROR: Install-SentinelAI-Test.ps1 was not found next to this launcher.
  pause
  exit /b 2
)

net session >nul 2>&1
if not "%ERRORLEVEL%"=="0" (
  echo Sentinel AI Windows VM test installer
  echo Requesting Administrator permission...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'cmd.exe' -Verb RunAs -ArgumentList '/c ""%~f0""'"
  exit /b 0
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo.
  echo Sentinel AI installation or launch verification failed.
  echo A SentinelAI-LaunchDiagnostics-*.txt file should be in this folder if launch diagnostics were available.
  echo Please send that diagnostics file for review.
  pause
  exit /b %RC%
)

exit /b 0
