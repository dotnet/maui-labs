#!/usr/bin/env bash
# Jetchat iOS smoke: same walk as jetchat.android.sh on the CometSwiftUIProbe
# simulator build. Scroll-away is driven via the agent's scroll action (contentOffset
# on the backing UIScrollView — fires the shim's onScroll exactly like a finger);
# there is no iOS drag injector yet (Android's MotionEvent synthesis has no public
# UIKit equivalent — revisit at the first iOS sample that needs a true gesture).
#
# Usage: tools/smoke/jetchat.ios.sh   (expects a booted simulator with the app installed)
source "$(dirname "$0")/lib.sh"

BUNDLE=com.comet.swiftuiprobe
smoke_begin jetchat.ios

ios_launch "$BUNDLE"
sleep 3   # first render + seed-scroll-to-newest
ios_agent_discover
agent_wait

# --- screen: conversation ---------------------------------------------------
ios_shot 01-conversation
assert_element "channel title bar text"          "type=Text&text=%23composers"
assert_element "member count"                    "type=Text&text=42%20members"
assert_element "composer text field"             "type=TextField"
assert_element "message list"                    "type=ListView"

# --- interaction: scroll away (agent scroll → shim onScroll → ScrolledAway) --
FAB_ID=$(element_id "type=Fab")
assert_element "jump-to-bottom FAB present" "type=Fab"
assert_prop "jump-to-bottom hidden at newest" "$FAB_ID" opacity 0
check "scroll up toward older messages" scroll_by -1200
sleep 1
ios_shot 02-scrolled-away
assert_prop "jump-to-bottom appears after scrolling away (ScrolledAway signal)" "$FAB_ID" opacity 1

# --- interaction: jump back to newest ----------------------------------------
check "tap jump-to-bottom" tap "$FAB_ID"
sleep 2
ios_shot 03-jumped-back
assert_prop "jump-to-bottom hides again at newest" "$FAB_ID" opacity 0

# --- interaction: composer fill / clear --------------------------------------
TF_ID=$(element_id "type=TextField")
check "fill composer" fill "$TF_ID" "smoke test message"
sleep 1
ios_shot 04-composer-filled
assert_element "composer holds the typed text" "type=TextField&text=smoke%20test%20message"
check "clear composer" clear_el "$TF_ID"
sleep 1
assert_element "composer cleared" "type=TextField"

smoke_end
