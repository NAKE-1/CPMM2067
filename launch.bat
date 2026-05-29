@echo off
setlocal EnableDelayedExpansion

REM CPMM2067 dev launcher
REM - Auto-installs the .NET 8 SDK via winget if missing
REM - Builds the app on first run (or after a clean)
REM - Launches CPMM2067.App.exe

set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
set "ROOT=%~dp0"
set "EXE=%ROOT%src\CPMM2067.App\bin\Debug\net8.0\CPMM2067.App.exe"

cd /d "%ROOT%"

REM --- Dependency: .NET 8 SDK ----------------------------------------
if not exist "%DOTNET%" (
  echo .NET 8 SDK not found at "%DOTNET%".
  where winget >nul 2>&1
  if errorlevel 1 (
    echo winget is not available. Install the .NET 8 SDK manually:
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
  )
  echo Installing Microsoft.DotNet.SDK.8 via winget ^(may show UAC^)...
  winget install --id Microsoft.DotNet.SDK.8 --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
  if errorlevel 1 (
    echo winget install failed. Try installing manually from:
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
  )
  if not exist "%DOTNET%" (
    echo .NET still not found after install. Reboot and re-run launch.bat.
    pause
    exit /b 1
  )
)

REM --- Build if needed ----------------------------------------------
if not exist "%EXE%" (
  echo Building CPMM2067 ^(first run^)...
  "%DOTNET%" build CPMM2067.sln -c Debug
  if errorlevel 1 (
    echo Build failed. See errors above.
    pause
    exit /b 1
  )
)

REM --- Launch -------------------------------------------------------
echo Launching CPMM2067...
start "" "%EXE%"
endlocal
