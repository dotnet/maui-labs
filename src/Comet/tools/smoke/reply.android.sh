#!/usr/bin/env bash
# Reply (M1) Android smoke: launch → inbox asserts → detail round-trip → adaptive
# chrome walk (rail @700dp, permanent drawer @1260dp) → route switch. Coordinate taps
# use the device-tap method (input swipe X Y X Y 120) for chrome widgets that live
# inside own-content nodes (nav items aren't individually registered Comet views).
set -euo pipefail
cd "$(dirname "$0")"
source ./lib.sh

PKG=com.comet.composeprobe
smoke_begin reply.android

# Launch straight into the Reply screen (intent extra picks the sample).
adb_ shell am force-stop "$PKG"
ACTIVITY=$(adb_ shell cmd package resolve-activity --brief "$PKG" | tail -1 | tr -d '\r')
adb_ shell am start -W -n "$ACTIVITY" --es screen reply > /dev/null
adb_ forward "tcp:${AGENT_HOST_PORT}" tcp:9223 > /dev/null
agent_wait
sleep 2

# ── Inbox (gold compact-01) ──
assert_element "search bar placeholder"        "type=Text&text=Search emails"
assert_element "first email subject"           "type=Text&text=Package shipped!"
assert_element "second email sender"           "type=Text&text=Ali"
assert_element "compose FAB label"             "type=Text&text=Compose"
android_shot 01-inbox

# ── Detail round-trip (gold compact-02) ──
adb_ shell input swipe 640 640 640 640 120   # tap first email card
sleep 1.5
assert_element "detail app bar message count"  "type=Text&text=7 Messages"
assert_element "thread subject"                "type=Text&text=Your update on Google Play Store is live!"
android_shot 02-detail
back
sleep 1.5
assert_element "back on inbox"                 "type=Text&text=Search emails"

# ── FAB collapse on scroll (gold compact-05): drag content up, label still composed
# (ExtendedFAB keeps both slots), then return to top. ──
drag 640 2000 640 800 300
sleep 1.5
android_shot 03-scrolled
drag 640 800 640 2000 300
sleep 1

# ── Route switch: Articles via the bottom bar (coordinate tap) ──
adb_ shell input swipe 476 2703 476 2703 120
sleep 1.5
assert_element "coming-soon title"             "type=Text&text=Screen under construction"
android_shot 06-coming-soon
adb_ shell input swipe 150 2703 150 2703 120   # back to Inbox
sleep 1.5
assert_element "inbox restored"                "type=Text&text=Package shipped!"

# ── Adaptive chrome (gold medium-700dp / expanded-1260dp) ──
trap android_resize_reset EXIT
android_resize 2100 2856
sleep 2
assert_element "rail keeps inbox visible"      "type=Text&text=Package shipped!"
android_shot 04-rail
android_resize 3780 2856
sleep 2
assert_element "permanent drawer wordmark"     "type=Text&text=REPLY"
assert_element "drawer item label"             "type=Text&text=Direct Messages"
assert_element "two-pane detail visible"       "type=Text&text=7 Messages"
android_shot 05-drawer-twopane
android_resize_reset
trap - EXIT
sleep 3
assert_element "inbox after resize walk"        "type=Text&text=Package shipped!"

smoke_end
