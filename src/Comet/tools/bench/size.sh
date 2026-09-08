#!/usr/bin/env bash
# Publishes an app in Release and reports package size (Android APK today;
# extend with iOS .ipa once the SwiftUI backend lands).
# Usage: tools/bench/size.sh <csproj> [extra msbuild args...]
set -euo pipefail

CSPROJ="${1:?usage: size.sh <csproj> [extra msbuild args...]}"
shift || true

dotnet publish "$CSPROJ" -f net11.0-android -c Release "$@" > /tmp/comet-size-publish.log 2>&1 || {
	tail -20 /tmp/comet-size-publish.log
	exit 1
}

DIR=$(dirname "$CSPROJ")
APK=$(find "$DIR/bin/Release/net11.0-android/publish" -name "*-Signed.apk" | head -1)
BYTES=$(stat -f %z "$APK" 2>/dev/null || stat -c %s "$APK")
echo "apk: $APK"
echo "size: $BYTES bytes ($(echo "scale=1; $BYTES/1048576" | bc) MiB)"

# Per-dex/per-lib breakdown if apkanalyzer is available.
APKANALYZER="$HOME/Library/Android/sdk/cmdline-tools/latest/bin/apkanalyzer"
if [ -x "$APKANALYZER" ]; then
	echo "--- top-level breakdown ---"
	"$APKANALYZER" files list --apk "$APK" 2>/dev/null | head -40 || true
fi
