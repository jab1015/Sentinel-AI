@echo off
setlocal
set "SCRIPT=%~dp0Install-SentinelAI-Test.ps1"

if not exist "%SCRIPT%" (
  echo ERROR: Install-SentinelAI-Test.ps1 was not found next to this launcher.
  pause
  exit /b 2
)

echo Sentinel AI Windows VM test installer
echo Requesting Administrator permission...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$p = Start-Process -FilePath 'powershell.exe' -Verb RunAs -PassThru -Wait -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File','"%SCRIPT%"'); exit $p.ExitCode"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo.
  echo Sentinel AI installation or launch verification failed.
  echo A SentinelAI-LaunchDiagnostics-*.txt file should be in the extracted package folder if launch diagnostics were available.
  echo Please send that diagnostics file for review.
  pause
  exit /b %RC%
)

exit /b 0
