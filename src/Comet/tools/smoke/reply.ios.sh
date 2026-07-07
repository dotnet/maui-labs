#!/usr/bin/env bash
# Reply iOS smoke (the M1 iOS gate): structure/values/interaction parity on the
# CometSwiftUIProbe simulator build — inbox list, detail round-trip via the app bar
# back button, route switch through the suite's bottom bar (element taps: the iOS
# chrome is composed from Comet views, so items ARE registered elements).
#
# Usage: tools/smoke/reply.ios.sh   (expects a booted simulator with the app installed;
# the app reads COMET_SCREEN=reply via simctl's SIMCTL_CHILD_ env prefix)
source "$(dirname "$0")/lib.sh"

BUNDLE=com.comet.swiftuiprobe
smoke_begin reply.ios

xcrun simctl terminate booted "$BUNDLE" 2>/dev/null || true
SIMCTL_CHILD_COMET_SCREEN=reply xcrun simctl launch booted "$BUNDLE" > /dev/null
sleep 4
ios_agent_discover
agent_wait

# --- inbox -------------------------------------------------------------------
ios_shot 01-inbox
assert_element "search placeholder"     "type=Text&text=Search emails"
assert_element "first email subject"    "type=Text&text=Package shipped!"
assert_element "second email sender"    "type=Text&text=Ali"
assert_element "compose FAB label"      "type=Text&text=Compose"

# --- detail round-trip (row tap → detail; app-bar back → list) ----------------
ROW=$(element_id "type=Text&text=Package shipped!")
check "tap first email row" tap "$ROW"
sleep 2
ios_shot 02-detail
assert_element "detail message count"   "type=Text&text=7 Messages"
assert_element "thread subject"         "type=Text&text=Your update on Google Play Store is live!"
BACK=$(element_id "type=Icon&text=arrow_back")
check "tap app-bar back" tap "$BACK"
sleep 2
assert_element "back on inbox"          "type=Text&text=Search emails"

# --- route switch through the suite's bottom bar ------------------------------
ARTICLES=$(element_id "type=Icon&text=article")
check "tap Articles item" tap "$ARTICLES"
sleep 2
ios_shot 03-coming-soon
assert_element "coming-soon title"      "type=Text&text=Screen under construction"
INBOX=$(element_id "type=Icon&text=inbox")
check "tap Inbox item" tap "$INBOX"
sleep 2
assert_element "inbox restored"         "type=Text&text=Package shipped!"

smoke_end
