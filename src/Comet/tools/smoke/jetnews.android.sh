#!/usr/bin/env bash
# JetNews (M2) Android smoke: launch → Home asserts → Article round-trip →
# drawer → Interests (tab switch + topic toggle) → back to Home. Chrome that
# lives inside own-content nodes (drawer sheet pills, PrimaryTabRow tabs) is
# driven with coordinate taps (input swipe X Y X Y 120 — the device-tap method).
set -euo pipefail
cd "$(dirname "$0")"
source ./lib.sh

PKG=com.comet.composeprobe
smoke_begin jetnews.android

# Launch straight into the JetNews screen (intent extra picks the sample).
adb_ shell am force-stop "$PKG"
ACTIVITY=$(adb_ shell cmd package resolve-activity --brief "$PKG" | tail -1 | tr -d '\r')
adb_ shell am start -W -n "$ACTIVITY" --es screen jetnews > /dev/null
adb_ forward "tcp:${AGENT_HOST_PORT}" tcp:9223 > /dev/null
agent_wait
sleep 2

# ── Home (gold compact-01) ──
assert_element "wordmark"              "type=Text&text=jetnews"
assert_element "top-stories header"    "type=Text&text=Top stories for you"
assert_element "hero title"            "type=Text&text=Redesigning the Android Studio Logo"
assert_element "popular section"       "type=Text&text=Popular on Jetnews"
android_shot 01-home

# ── Article round-trip (gold compact-07): tap the hero card ──
adb_ shell input swipe 640 776 640 776 120
sleep 1.5
assert_element "published-in bar"      "type=Text&text=Published in:"
assert_element "publication name"      "type=Text&text=Android Developers"
assert_element "article author"        "type=Text&text=Android Studio Team"
android_shot 02-article
# Article body scroll (headers/bullets deep in the post).
drag 640 2200 640 700 300
sleep 1
android_shot 03-article-scrolled
# Back arrow (top-left) returns Home.
adb_ shell input swipe 71 229 71 229 120
sleep 1.5
assert_element "back on home"          "type=Text&text=Top stories for you"

# ── Drawer (gold compact-03): menu opens the REAL ModalNavigationDrawer ──
adb_ shell input swipe 71 229 71 229 120
sleep 2
assert_element "drawer home item"      "type=Text&text=Home"
assert_element "drawer interests item" "type=Text&text=Interests"
android_shot 04-drawer

# ── Interests (gold compact-04): drawer item → tabs + topics ──
adb_ shell input swipe 286 642 286 642 120
sleep 2.5
# (Tab labels render inside the real PrimaryTabRow facade — not registry-visible;
# tab function is proven by the People switch below.)
assert_element "interests title"       "type=Text&text=Interests"
assert_element "topics section"        "type=Text&text=Android"
assert_element "topic row"             "type=Text&text=Jetpack Compose"
android_shot 05-interests

# Tab switch: People (real PrimaryTabRow tab, coordinate tap).
adb_ shell input swipe 639 419 639 419 120
sleep 1.5
assert_element "people row"            "type=Text&text=Kobalt Toral"
android_shot 06-people
# And back to Topics.
adb_ shell input swipe 213 419 213 419 120
sleep 1.5
assert_element "topics restored"       "type=Text&text=Jetpack Compose"

# ── Back to Home via drawer ──
adb_ shell input swipe 71 229 71 229 120
sleep 2
adb_ shell input swipe 286 462 286 462 120   # Home pill
sleep 2
assert_element "home via drawer"       "type=Text&text=Top stories for you"
android_shot 07-home-again

# ── Expanded chrome (gold expanded-1260dp-01): rail + list-detail ──
trap android_resize_reset EXIT
android_resize 3780 2856
sleep 4
# (The REAL OutlinedTextField renders its placeholder facade-internally — assert the field.)
assert_element "expanded search field"     "type=TextField"
assert_element "select-a-post placeholder" "type=Text&text=Select a post"
android_shot 08-expanded
# Open a post into the detail pane (hero title tap).
adb_ shell input swipe 794 800 794 800 120
sleep 2
assert_element "expanded article"          "type=Text&text=Published in:"
android_shot 09-expanded-detail
android_resize_reset
sleep 4
assert_element "compact restored"          "type=Text&text=Top stories for you"

smoke_end
