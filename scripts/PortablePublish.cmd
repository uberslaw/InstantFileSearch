@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

rem Portable pack: prompts for a destination folder, then publishes self-contained
rem single-file InstantFileSearch.exe + InstantFileSearch.Cli.exe. -STA helps the
rem folder picker. Stay attached so the exit code is visible.

set "SCRIPT=%~dp0PortablePublish.ps1"
if not exist "%SCRIPT%" (
  echo Missing %SCRIPT%
  exit /b 1
)

where powershell.exe >nul 2>&1
if not errorlevel 1 (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%SCRIPT%" %*
  exit /b %ERRORLEVEL%
)

where pwsh >nul 2>&1
if not errorlevel 1 (
  pwsh -NoProfile -ExecutionPolicy Bypass -STA -File "%SCRIPT%" %*
  exit /b %ERRORLEVEL%
)

echo Windows PowerShell or pwsh is required to build the portable folder.
exit /b 1
