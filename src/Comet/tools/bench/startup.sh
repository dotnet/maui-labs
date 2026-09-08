#!/usr/bin/env bash
# Measures Android cold-start time for a Comet app via `am start -W`.
# Usage: tools/bench/startup.sh <apk-path> <package-id> [runs]
# Prints each run's TotalTime and the median.
set -euo pipefail

APK="${1:?usage: startup.sh <apk-path> <package-id> [runs]}"
PKG="${2:?usage: startup.sh <apk-path> <package-id> [runs]}"
RUNS="${3:-10}"

adb install -r "$APK" > /dev/null

# Resolve the launcher activity from the APK's package.
ACTIVITY=$(adb shell cmd package resolve-activity --brief "$PKG" | tail -1 | tr -d '\r')

times=()
for i in $(seq 1 "$RUNS"); do
	adb shell am force-stop "$PKG"
	# Drop app from page cache as much as the emulator allows between runs.
	sleep 2
	out=$(adb shell am start -W -n "$ACTIVITY" | tr -d '\r')
	t=$(echo "$out" | awk -F': ' '/TotalTime/ {print $2}')
	times+=("$t")
	echo "run $i: ${t}ms"
done

adb shell am force-stop "$PKG"

median=$(printf '%s\n' "${times[@]}" | sort -n | awk '{a[NR]=$1} END {print (NR%2==1) ? a[(NR+1)/2] : int((a[NR/2]+a[NR/2+1])/2)}')
echo "median (${RUNS} runs): ${median}ms"
