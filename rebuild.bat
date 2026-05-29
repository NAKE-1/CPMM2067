@echo off
setlocal
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
cd /d "%~dp0"
"%DOTNET%" build CPMM2067.sln -c Debug
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
"%DOTNET%" test tests\CPMM2067.Tests --no-build
pause
endlocal
