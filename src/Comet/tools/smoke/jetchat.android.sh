#!/usr/bin/env bash
# Jetchat smoke: launch → walk the conversation screen → exercise scroll (real drag),
# jump-to-bottom, composer fill/clear → assert via Comet dev-agent element queries →
# screenshot each step. See tools/smoke/lib.sh for the helpers and conventions.
#
# Usage: ANDROID_SERIAL=emulator-5554 tools/smoke/jetchat.android.sh
source "$(dirname "$0")/lib.sh"

PKG=com.comet.composeprobe
smoke_begin jetchat.android

android_launch "$PKG"
agent_wait
sleep 2   # first composition + seed-scroll-to-newest

# --- screen: conversation ---------------------------------------------------
android_shot 01-conversation
assert_element "channel title bar text"          "type=Text&text=%23composers"
assert_element "member count"                    "type=Text&text=42%20members"
assert_element "composer text field"             "type=TextField"
assert_element "message list"                    "type=ListView"

# --- interaction: scroll away (REAL drag through the input pipeline) --------
# The jump-to-bottom pill is a Comet.Fab that is ALWAYS composed; visibility is its
# reactive Opacity bound to the list's ScrolledAway signal (JetchatConversation.cs).
FAB_ID=$(element_id "type=Fab")
assert_element "jump-to-bottom FAB present" "type=Fab"
assert_prop "jump-to-bottom hidden at newest" "$FAB_ID" opacity 0
drag 540 600 540 1800 400   # finger down = reveal older messages
sleep 1
android_shot 02-scrolled-away
assert_prop "jump-to-bottom appears after scrolling away (ScrolledAway signal)" "$FAB_ID" opacity 1

# --- interaction: jump back to newest ----------------------------------------
check "tap jump-to-bottom" tap "$FAB_ID"
sleep 2
android_shot 03-jumped-back
assert_prop "jump-to-bottom hides again at newest" "$FAB_ID" opacity 0

# --- interaction: composer fill / clear --------------------------------------
TF_ID=$(element_id "type=TextField")
check "fill composer" fill "$TF_ID" "smoke test message"
sleep 1
android_shot 04-composer-filled
assert_element "composer holds the typed text" "type=TextField&text=smoke%20test%20message"
check "clear composer" clear_el "$TF_ID"
sleep 1
assert_element "composer cleared" "type=TextField"

smoke_end
