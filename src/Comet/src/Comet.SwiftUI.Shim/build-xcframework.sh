#!/usr/bin/env bash
# Builds CometSwiftUIShim.xcframework (device + simulator) from the Swift package.
# The shim exposes SwiftUI behind an @objc-representable surface so .NET for iOS can
# bind it without Swift-ABI interop.
set -euo pipefail
cd "$(dirname "$0")"

rm -rf build CometSwiftUIShim.xcframework

xcodebuild archive -scheme CometSwiftUIShim -destination "generic/platform=iOS Simulator" \
  -archivePath build/sim.xcarchive SKIP_INSTALL=NO BUILD_LIBRARY_FOR_DISTRIBUTION=YES >/dev/null
xcodebuild archive -scheme CometSwiftUIShim -destination "generic/platform=iOS" \
  -archivePath build/dev.xcarchive SKIP_INSTALL=NO BUILD_LIBRARY_FOR_DISTRIBUTION=YES >/dev/null

xcodebuild -create-xcframework \
  -framework build/sim.xcarchive/Products/usr/local/lib/CometSwiftUIShim.framework \
  -framework build/dev.xcarchive/Products/usr/local/lib/CometSwiftUIShim.framework \
  -output CometSwiftUIShim.xcframework >/dev/null

echo "built CometSwiftUIShim.xcframework:"
ls CometSwiftUIShim.xcframework/
