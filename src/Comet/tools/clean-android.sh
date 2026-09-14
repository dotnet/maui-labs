#!/usr/bin/env bash
# One-keystroke clean rebuild + deploy for the Comet Android dev loop.
#
# Normally NOT needed: the "no Java peer" crash after facade edits was fixed at
# the root (see Directory.Build.targets `_CometFixFastDevApkInputs` and
# docs/research/upstream-issue-fastdev-typemap-staleness.md). Keep this as the
# fallback if an SDK update regresses the incremental path.
#
# Usage: tools/clean-android.sh [app-project-dir]   (default: sample/CometComposeProbe)
set -euo pipefail

COMET_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="${1:-sample/CometComposeProbe}"

cd "$COMET_ROOT"
rm -rf \
  src/vendor/Microsoft.AndroidX.Compose/obj src/vendor/Microsoft.AndroidX.Compose/bin \
  src/Comet/obj src/Comet/bin \
  "$APP/obj" "$APP/bin"

exec dotnet build "$APP" -f net11.0-android -t:Run -p:AndroidPackageFormat=apk
