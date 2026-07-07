#!/usr/bin/env bash
# Shared helpers for the per-sample DevFlow smoke scripts (tools/smoke/<sample>.<platform>.sh).
# Convention: each script launches the app, walks every screen, exercises each interaction,
# asserts via the Comet dev agent's tree/element queries, and screenshots each step into
# tools/smoke/out/<sample>.<platform>/ — see docs/sample-workflow-checklist.md step 6.

set -euo pipefail

# --- Android ---------------------------------------------------------------

# The Comet dev agent listens on 9223 IN the app; the host side of the forward defaults
# to 19223 because 9223 is commonly squatted on dev Macs (FoundrySt… does on David's).
: "${ANDROID_SERIAL:=emulator-5554}"
: "${AGENT_HOST_PORT:=19223}"
AGENT="http://localhost:${AGENT_HOST_PORT}"

adb_() { adb -s "$ANDROID_SERIAL" "$@"; }

android_launch() {  # android_launch <package>
	local pkg="$1"
	adb_ shell am force-stop "$pkg"
	local activity
	activity=$(adb_ shell cmd package resolve-activity --brief "$pkg" | tail -1 | tr -d '\r')
	adb_ shell am start -W -n "$activity" > /dev/null
	adb_ forward "tcp:${AGENT_HOST_PORT}" tcp:9223 > /dev/null
}

agent_wait() {  # poll the agent until it answers (app startup)
	for _ in $(seq 1 40); do
		if curl -s -m 2 "$AGENT/api/v1/agent/status" | grep -q '"running":true'; then
			return 0
		fi
		sleep 0.5
	done
	echo "FAIL: dev agent did not come up on $AGENT" >&2
	return 1
}

agent_get()  { curl -s -m 10 "$AGENT$1"; }
agent_post() { curl -s -m 30 -X POST "$AGENT$1" -d "$2"; }

# elements <query>          → the raw JSON array for ?type=&text=&automationId=
# (spaces in text= values are URL-encoded here so callers can write them naturally)
elements() { agent_get "/api/v1/ui/elements?${1// /%20}"; }

# element_id <query>        → first matching element id (empty when none)
element_id() { elements "$1" | sed -n 's/.*"id":"\([0-9]*\)".*/\1/p' | head -1; }

tap()      { agent_post /api/v1/ui/actions/tap   "{\"elementId\":\"$1\"}" | grep -q '"success":true'; }
clear_el() { agent_post /api/v1/ui/actions/clear "{\"elementId\":\"$1\"}" | grep -q '"success":true'; }
fill()  { agent_post /api/v1/ui/actions/fill "{\"elementId\":\"$1\",\"text\":$(printf '%s' "$2" | jq -Rs .)}" | grep -q '"success":true'; }
focus() { agent_post /api/v1/ui/actions/focus "{\"elementId\":\"$1\"}" | grep -q '"success":true'; }
back()  { agent_post /api/v1/ui/actions/back '{}' | grep -q '"success":true'; }

# drag <x1> <y1> <x2> <y2> [durationMs] — REAL input-pipeline drag, physical px
drag() {
	agent_post /api/v1/ui/actions/drag \
		"{\"x1\":$1,\"y1\":$2,\"x2\":$3,\"y2\":$4,\"durationMs\":${5:-300}}" | grep -q '"success":true'
}

# scroll_by <dy> — semantic scroll of the frontmost scrollable (dy<0 scrolls toward older/top)
scroll_by() {
	agent_post /api/v1/ui/actions/scroll "{\"dy\":$1}" | grep -q '"success":true'
}

android_shot() {  # android_shot <name> — screenshot into $OUT
	adb_ exec-out screencap -p > "$OUT/$1.png"
	echo "  shot: $1"
}

# android_resize <w> <h> — override the emulator display size (px). Drives the
# LayoutChange → CometWindowMetrics path, so adaptive size-class UI reflows —
# the smoke-script "resize verb" for Reply-style adaptive asserts.
# ALWAYS pair with android_resize_reset (trap it) or the emulator stays resized.
android_resize()       { adb_ shell wm size "$1x$2"; sleep 2; }
android_resize_reset() { adb_ shell wm size reset; sleep 2; }

# --- iOS (simulator) --------------------------------------------------------

# The sim shares the host loopback, so the agent is reached directly — but the default
# port 9223 is often held by a Mac-side process (a MAUI DevFlow agent squats it on
# David's machine), so CometDevAgent scans forward. Discover the COMET agent by probing.
ios_agent_discover() {
	for p in $(seq 9223 9232); do
		if curl -s -m 1 "http://localhost:$p/api/v1/agent/status" | grep -q '"framework":"comet"'; then
			AGENT_HOST_PORT=$p
			AGENT="http://localhost:$p"
			echo "  agent: $AGENT"
			return 0
		fi
	done
	echo "FAIL: no Comet dev agent found on localhost:9223-9232" >&2
	return 1
}

ios_launch() {  # ios_launch <bundle-id>
	xcrun simctl terminate booted "$1" 2>/dev/null || true
	xcrun simctl launch booted "$1" > /dev/null
}

ios_shot() {  # ios_shot <name>
	xcrun simctl io booted screenshot "$OUT/$1.png" > /dev/null 2>&1
	echo "  shot: $1"
}

# prop_of <id> <prop> — a node's friendly prop (text/opacity/isOn/…) from the simple /tree route
prop_of() {
	agent_get /tree | python3 -c "
import json,sys
for n in json.load(sys.stdin)['nodes']:
    if n['id'] == $1:
        print(n.get('props',{}).get('$2',''))
        break"
}

# --- assertions ------------------------------------------------------------

PASS=0
FAIL=0

check() {  # check <description> <command...>
	local desc="$1"; shift
	if "$@"; then
		PASS=$((PASS+1)); echo "  ok: $desc"
	else
		FAIL=$((FAIL+1)); echo "  FAIL: $desc" >&2
	fi
}

assert_element() {  # assert_element <description> <query>
	local desc="$1" q="$2"
	local id; id=$(element_id "$q")
	if [ -n "$id" ]; then
		PASS=$((PASS+1)); echo "  ok: $desc (id=$id)"
	else
		FAIL=$((FAIL+1)); echo "  FAIL: $desc — no element for '$q'" >&2
	fi
}

assert_no_element() {  # assert_no_element <description> <query>
	local desc="$1" q="$2"
	local id; id=$(element_id "$q")
	if [ -z "$id" ]; then
		PASS=$((PASS+1)); echo "  ok: $desc"
	else
		FAIL=$((FAIL+1)); echo "  FAIL: $desc — unexpected element id=$id for '$q'" >&2
	fi
}

assert_prop() {  # assert_prop <description> <id> <prop> <expected>
	local desc="$1" id="$2" prop="$3" want="$4"
	local got; got=$(prop_of "$id" "$prop")
	if [ "$got" = "$want" ]; then
		PASS=$((PASS+1)); echo "  ok: $desc ($prop=$got)"
	else
		FAIL=$((FAIL+1)); echo "  FAIL: $desc — $prop='$got', expected '$want'" >&2
	fi
}

smoke_begin() {  # smoke_begin <sample>.<platform>
	OUT="$(cd "$(dirname "${BASH_SOURCE[1]}")" && pwd)/out/$1"
	mkdir -p "$OUT"
	echo "== smoke: $1 (out: $OUT) =="
}

smoke_end() {
	echo "== $PASS passed, $FAIL failed =="
	[ "$FAIL" -eq 0 ]
}
