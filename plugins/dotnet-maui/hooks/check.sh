#!/usr/bin/env sh
# plugins/dotnet-maui/hooks/check.sh
#
# Tiny platform wrapper for the dotnet-maui plugin's hook. The real work
# lives in `maui devflow hook check <event>`. This script exists solely to
# detect when the `maui` CLI isn't on PATH and surface a one-line install
# nudge instead of letting the hook fail silently.
#
# All other logic — MSBuild project evaluation, MAUI detection, wiring
# classification, debounce, JSON emission — lives inside the CLI so it
# stays typed, tested, and version-locked.

if ! command -v maui >/dev/null 2>&1; then
  printf '{"context":"📱 MAUI DevFlow plugin is active but the `maui` CLI is not installed. Run: dotnet tool install -g Microsoft.Maui.Cli --prerelease","hookSpecificOutput":{"hookEventName":"%s","additionalContext":"📱 MAUI DevFlow plugin is active but the `maui` CLI is not installed. Run: dotnet tool install -g Microsoft.Maui.Cli --prerelease"}}' "${1:-SessionStart}"
  exit 0
fi

exec maui devflow hook check "$@"
