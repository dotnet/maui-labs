#!/bin/bash
# Apple CLI Smoke Tests
# ---------------------
# Validates the `maui apple` CLI commands work correctly on macOS.
# Run after changes to src/Cli/.../Providers/Apple/ or after updating
# the Xamarin.Apple.Tools.MaciOS package version.
#
# Prerequisites:
#   - macOS with Xcode installed
#   - .NET SDK (version per global.json)
#   - At least one iOS simulator runtime installed
#
# Usage:
#   ./eng/smoke-tests/apple-cli-smoke-test.sh [path-to-maui-binary]
#
# If no binary path is provided, builds the CLI in Debug mode first.

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

PASS=0
FAIL=0
SKIP=0

pass() { echo -e "  ${GREEN}✅ PASS${NC}: $1"; PASS=$((PASS + 1)); }
fail() { echo -e "  ${RED}❌ FAIL${NC}: $1 — $2"; FAIL=$((FAIL + 1)); }
skip() { echo -e "  ${YELLOW}⏭️  SKIP${NC}: $1 — $2"; SKIP=$((SKIP + 1)); }

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

# Determine the CLI binary path
if [[ $# -ge 1 ]]; then
    MAUI="$1"
else
    echo "Building CLI in Debug mode..."
    dotnet build src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj -c Debug --nologo -v q
    MAUI="$REPO_ROOT/artifacts/bin/Microsoft.Maui.Cli/Debug/net10.0/maui"
fi

if [[ ! -x "$MAUI" ]]; then
    echo -e "${RED}ERROR${NC}: CLI binary not found or not executable at: $MAUI"
    exit 1
fi

echo ""
echo "========================================"
echo " Apple CLI Smoke Tests"
echo " Binary: $MAUI"
echo "========================================"
echo ""

# Check we're on macOS
if [[ "$(uname)" != "Darwin" ]]; then
    echo -e "${RED}ERROR${NC}: These smoke tests require macOS."
    exit 1
fi

# --- Test 1: Xcode List ---
echo "Test 1: maui apple xcode list --json"
OUTPUT=$($MAUI apple xcode list --json 2>&1) || true
if echo "$OUTPUT" | grep -q '"path"'; then
    XCODE_VER=$(echo "$OUTPUT" | grep '"version"' | head -1 | sed 's/.*: "//;s/".*//')
    pass "Xcode found (version: $XCODE_VER)"
else
    fail "Xcode list" "No Xcode installations found in JSON output"
fi

# --- Test 2: Runtime List ---
echo "Test 2: maui apple runtime list --json"
OUTPUT=$($MAUI apple runtime list --json 2>&1) || true
if echo "$OUTPUT" | grep -q '"identifier"'; then
    RUNTIME_COUNT=$(echo "$OUTPUT" | grep -c '"identifier"' || true)
    pass "Runtimes listed ($RUNTIME_COUNT runtime(s))"
else
    fail "Runtime list" "No runtimes found in JSON output"
fi

# --- Test 3: Simulator List ---
echo "Test 3: maui apple simulator list --json"
SIM_OUTPUT=$($MAUI apple simulator list --json 2>&1) || true
if echo "$SIM_OUTPUT" | grep -q '"udid"'; then
    SIM_COUNT=$(echo "$SIM_OUTPUT" | grep -c '"udid"' || true)
    pass "Simulators listed ($SIM_COUNT simulator(s))"
else
    fail "Simulator list" "No simulators found in JSON output"
fi

# --- Test 4: Simulator Start ---
echo "Test 4: maui apple simulator start (boot a simulator)"
# Find the first available iPhone simulator name
SIM_NAME=$(echo "$SIM_OUTPUT" | python3 -c "
import sys, json

text = sys.stdin.read()
lines = text.split('\n')
json_start = next((i for i, l in enumerate(lines) if l.strip().startswith('[')), None)
if json_start is None:
    sys.exit(1)
json_text = '\n'.join(lines[json_start:])
# Find end of array
bracket_count = 0
end = 0
for i, ch in enumerate(json_text):
    if ch == '[': bracket_count += 1
    elif ch == ']': bracket_count -= 1
    if bracket_count == 0:
        end = i + 1
        break
data = json.loads(json_text[:end])
for d in data:
    if d.get('is_available') and not d.get('is_booted') and 'iPhone' in d.get('name', ''):
        print(d['name'])
        sys.exit(0)
sys.exit(1)
" 2>/dev/null) || SIM_NAME=""

if [[ -z "$SIM_NAME" ]]; then
    skip "Simulator start" "No available iPhone simulator found to boot"
else
    START_OUTPUT=$($MAUI apple simulator start "$SIM_NAME" --json 2>&1) || true
    if echo "$START_OUTPUT" | grep -q '"success"' || echo "$START_OUTPUT" | grep -q '"status": "success"'; then
        pass "Simulator '$SIM_NAME' booted"

        # --- Test 5: Simulator Stop ---
        echo "Test 5: maui apple simulator stop (shut down simulator)"
        STOP_OUTPUT=$($MAUI apple simulator stop "$SIM_NAME" --json 2>&1) || true
        if echo "$STOP_OUTPUT" | grep -q '"success"' || echo "$STOP_OUTPUT" | grep -q '"status": "success"'; then
            pass "Simulator '$SIM_NAME' stopped"
        else
            fail "Simulator stop" "Failed to shut down '$SIM_NAME'"
        fi
    else
        fail "Simulator start" "Failed to boot '$SIM_NAME'"
        SKIP=$((SKIP + 1))  # skip the stop test
    fi
fi

# --- Test 6: Install (dry-run, default platform = iOS) ---
echo "Test 6: maui --dry-run apple install --json"
INSTALL_OUTPUT=$($MAUI --dry-run apple install --json 2>&1) || true
if echo "$INSTALL_OUTPUT" | grep -q '"status"'; then
    STATUS=$(echo "$INSTALL_OUTPUT" | grep '"status"' | head -1 | sed 's/.*: "//;s/".*//')
    pass "Install dry-run completed (status: $STATUS)"
else
    fail "Install dry-run" "No status in JSON output"
fi

# --- Test 7: Install with --platform all (dry-run) ---
echo "Test 7: maui --dry-run apple install --platform all --json"
INSTALL_ALL_OUTPUT=$($MAUI --dry-run apple install --platform all --json 2>&1) || true
if echo "$INSTALL_ALL_OUTPUT" | grep -q '"status"'; then
    pass "Install --platform all dry-run completed"
else
    fail "Install --platform all dry-run" "No status in JSON output"
fi

# --- Test 8: Simulator app lifecycle commands (invalid UDID = validates error handling) ---
echo "Test 8: maui apple simulator install (invalid UDID, expects E2204)"
SIM_INSTALL_OUTPUT=$($MAUI apple simulator install "INVALID-UDID" "/tmp/Fake.app" --json 2>&1) || true
if echo "$SIM_INSTALL_OUTPUT" | grep -q 'E2204'; then
    pass "Simulator install correctly returns E2204 for invalid UDID"
else
    fail "Simulator install error handling" "Expected E2204 in output"
fi

echo "Test 9: maui apple simulator launch (invalid UDID, expects E2204)"
SIM_LAUNCH_OUTPUT=$($MAUI apple simulator launch "INVALID-UDID" "com.fake.app" --json 2>&1) || true
if echo "$SIM_LAUNCH_OUTPUT" | grep -q 'E2204'; then
    pass "Simulator launch correctly returns E2204 for invalid UDID"
else
    fail "Simulator launch error handling" "Expected E2204 in output"
fi

echo "Test 10: maui apple simulator get-app-container (invalid UDID, expects E2204)"
SIM_CONTAINER_OUTPUT=$($MAUI apple simulator get-app-container "INVALID-UDID" "com.fake.app" --json 2>&1) || true
if echo "$SIM_CONTAINER_OUTPUT" | grep -q 'E2204'; then
    pass "Simulator get-app-container correctly returns E2204 for invalid UDID"
else
    fail "Simulator get-app-container error handling" "Expected E2204 in output"
fi

# --- Summary ---
echo ""
echo "========================================"
echo -e " Results: ${GREEN}$PASS passed${NC}, ${RED}$FAIL failed${NC}, ${YELLOW}$SKIP skipped${NC}"
echo "========================================"

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
