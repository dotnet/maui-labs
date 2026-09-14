#!/usr/bin/env bash
# JetNews iOS smoke (the M2 iOS gate): Home → Article round-trip → drawer →
# Interests (TabBar twin switch + topic toggle) → Home. The iOS chrome is
# composed from Comet views, so drawer items and tab labels ARE registered
# elements (unlike Android's facade-internal tab labels).
source "$(dirname "$0")/lib.sh"

BUNDLE=com.comet.swiftuiprobe
smoke_begin jetnews.ios

xcrun simctl terminate "$IOS_SIM" "$BUNDLE" 2>/dev/null || true
SIMCTL_CHILD_COMET_SCREEN=jetnews xcrun simctl launch "$IOS_SIM" "$BUNDLE" > /dev/null
sleep 4
ios_agent_discover
agent_wait

# --- Home (gold compact-01) ---------------------------------------------------
ios_shot 01-home
assert_element "wordmark vector"        "type=Icon&text=jetnews_wordmark"
assert_element "top-stories header"     "type=Text&text=Top stories for you"
assert_element "hero title"             "type=Text&text=Redesigning the Android Studio Logo"

# --- Article round-trip (hero tap → detail; back arrow → home) -----------------
HERO=$(element_id "type=Text&text=Redesigning the Android Studio Logo")
check "tap hero" tap "$HERO"
sleep 2
ios_shot 02-article
assert_element "published-in bar"       "type=Text&text=Published in:"
assert_element "publication name"       "type=Text&text=Android Developers"
assert_element "article author"         "type=Text&text=Android Studio Team"
BACK=$(element_id "type=Icon&text=arrow_back")
check "tap article back" tap "$BACK"
sleep 2
assert_element "back on home"           "type=Text&text=Top stories for you"

# --- Drawer (logo tap → sheet) --------------------------------------------------
LOGO=$(element_id "type=Icon&text=jetnews_logo")
check "open drawer" tap "$LOGO"
sleep 2
ios_shot 03-drawer
assert_element "drawer home item"       "type=Text&text=Home"
assert_element "drawer interests item"  "type=Text&text=Interests"

# --- Interests: TabBar twin + toggle -------------------------------------------
INTERESTS=$(element_id "type=Text&text=Interests")
check "open interests" tap "$INTERESTS"
sleep 2
ios_shot 04-interests
assert_element "topics tab"             "type=Text&text=Topics"
assert_element "topics section"         "type=Text&text=Android"
assert_element "topic row"              "type=Text&text=Jetpack Compose"

PEOPLE=$(element_id "type=Text&text=People")
check "switch to People" tap "$PEOPLE"
sleep 2
assert_element "people row"             "type=Text&text=Kobalt Toral"
PERSON=$(element_id "type=Text&text=Kobalt Toral")
check "toggle person" tap "$PERSON"
sleep 1
ios_shot 05-people-toggled
TOPICS=$(element_id "type=Text&text=Topics")
check "back to Topics" tap "$TOPICS"
sleep 2
assert_element "topics restored"        "type=Text&text=Jetpack Compose"

# --- Home via drawer ------------------------------------------------------------
LOGO=$(element_id "type=Icon&text=jetnews_logo")
check "reopen drawer" tap "$LOGO"
sleep 2
HOME=$(element_id "type=Text&text=Home")
check "home via drawer" tap "$HOME"
sleep 2
assert_element "home restored"          "type=Text&text=Top stories for you"
ios_shot 06-home-again

smoke_end
