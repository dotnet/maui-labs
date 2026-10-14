#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -eq 0 ]; then
  echo "Usage: bash install-android-sdk-with-retry.sh <sdk-package> [...]" >&2
  exit 64
fi

sdk_root="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
sdkmanager="$sdk_root/cmdline-tools/latest/bin/sdkmanager"
if [ -z "$sdk_root" ] || [ ! -x "$sdkmanager" ]; then
  echo "Android sdkmanager was not found. Set ANDROID_HOME to an SDK with cmdline-tools/latest installed." >&2
  exit 1
fi

log_directory="${RUNNER_TEMP:-$PWD/artifacts}"
mkdir -p "$log_directory"
log_file=$(mktemp "$log_directory/android-sdk-install.XXXXXX")
trap 'rm -f "$log_file"' EXIT

permanent_error='Permission denied|No space left on device|License.*not.*accepted
HTTP (response code:|status code:|status:)?[[:space:]]*(400|401|403|404|410)([^0-9]|$)'
transient_error='Error on ZipFile|ZipException|zip END header not found|Unexpected end of (ZIP|ZLIB)
invalid (zip|archive)|corrupt(ed)? (zip|archive)|not a zip (file|archive)
SocketTimeoutException|ConnectException|UnknownHostException|SSLException
Connection (reset|timed out|refused|aborted)|Read timed out|Connect timed out|Broken pipe
Temporary failure in name resolution|Could not resolve host|Network (error|is unreachable)
Error (occurred )?(while|during) download|Download (failed|error)
HTTP (response code:|status code:|status:)?[[:space:]]*(408|429|5[0-9]{2})([^0-9]|$)'

for attempt in 1 2 3; do
  echo "Installing Android SDK packages (attempt $attempt/3): $*"
  result=0
  # yes may exit with SIGPIPE on success; only sdkmanager's exit status is authoritative.
  yes 2>/dev/null | "$sdkmanager" --install "$@" > "$log_file" 2>&1 || result=${PIPESTATUS[1]}
  cat "$log_file"
  if [ "$result" -eq 0 ]; then
    exit 0
  fi

  if LC_ALL=C grep -Eiq "$permanent_error" "$log_file" ||
    ! LC_ALL=C grep -Eiq "$transient_error" "$log_file"; then
    echo "Android SDK installation failed with exit code $result; not a retryable ZIP/download error." >&2
    exit "$result"
  fi

  if [ "$attempt" -eq 3 ]; then
    echo "Android SDK installation failed after 3 attempts; last exit code: $result." >&2
    exit "$result"
  fi

  delay=$((attempt * 5))
  echo "Retrying Android SDK installation after a ZIP/download error in ${delay}s." >&2
  sleep "$delay"
done
