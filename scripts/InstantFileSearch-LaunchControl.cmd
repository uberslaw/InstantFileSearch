@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem Master-facing Launch Control. Master Launch Control scans *LaunchControl*.cmd.
rem Invoke the GUI exe directly (no `start ""`) so an elevated Master Launch Control
rem keeps its token. `start ""` uses ShellExecute and can drop elevation.

set "EXE=%~dp0..\launch-control\bin\Release\net8.0-windows\InstantFileSearch.LaunchControl.exe"
if not exist "%EXE%" set "EXE=%~dp0..\launch-control\bin\Debug\net8.0-windows\InstantFileSearch.LaunchControl.exe"
if not exist "%EXE%" (
  echo Building Instant File Search Launch Control...
  where dotnet >nul 2>&1
  if errorlevel 1 (
    echo .NET 8 SDK is required. Install from https://dotnet.microsoft.com/download
    echo Then run this launcher again.
    pause
    exit /b 1
  )
  rem LaunchControl.Standard lives in the master-launch-control repo.
  rem Resolve order: MLC_ROOT / LcStandard, sibling folder, %%USERPROFILE%%\Projects\master-launch-control.
  dotnet build "%~dp0..\launch-control\InstantFileSearch.LaunchControl.csproj" -c Release
  if errorlevel 1 (
    echo Build failed. Clone https://github.com/uberslaw/master-launch-control next to this repo
    echo or at %%USERPROFILE%%\Projects\master-launch-control, or set MLC_ROOT to that clone.
    pause
    exit /b 1
  )
  set "EXE=%~dp0..\launch-control\bin\Release\net8.0-windows\InstantFileSearch.LaunchControl.exe"
)

if not exist "%EXE%" (
  echo Could not find InstantFileSearch.LaunchControl.exe
  pause
  exit /b 1
)

"%EXE%" %*
exit /b 0
