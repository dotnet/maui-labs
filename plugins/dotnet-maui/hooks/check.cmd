@echo off
rem plugins/dotnet-maui/hooks/check.cmd
rem
rem Tiny Windows wrapper for the dotnet-maui plugin's hook. The real work
rem lives in `maui devflow hook check <event>`. This script exists solely
rem to detect when the `maui` CLI isn't on PATH and surface a one-line
rem install nudge instead of letting the hook fail silently.

setlocal
set "EVT=%~1"
if "%EVT%"=="" set "EVT=SessionStart"

where maui >nul 2>&1
if errorlevel 1 (
  echo {"context":"📱 MAUI DevFlow plugin is active but the `maui` CLI is not installed. Run: dotnet tool install -g Microsoft.Maui.Cli --prerelease","hookSpecificOutput":{"hookEventName":"%EVT%","additionalContext":"📱 MAUI DevFlow plugin is active but the `maui` CLI is not installed. Run: dotnet tool install -g Microsoft.Maui.Cli --prerelease"}}
  endlocal
  exit /b 0
)

endlocal & maui devflow hook check %*
