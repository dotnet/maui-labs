#!/usr/bin/env bash
# Jetsnack (M3) Android smoke: home feed → filters sheet → snack detail →
# search grid → cart (remove + totals) → profile → home. Chrome is the gold's
# own hand-composed design system, so most controls are registered Comet views;
# coordinate taps drive the few spots where element text repeats across rows.
set -euo pipefail
cd "$(dirname "$0")"
source ./lib.sh

PKG=com.comet.composeprobe
smoke_begin jetsnack.android

adb_ shell am force-stop "$PKG"
ACTIVITY=$(adb_ shell cmd package resolve-activity --brief "$PKG" | tail -1 | tr -d '\r')
adb_ shell am start -W -n "$ACTIVITY" --es screen jetsnack > /dev/null
adb_ forward "tcp:${AGENT_HOST_PORT}" tcp:9223 > /dev/null
agent_wait
sleep 2

# ── Home (gold compact-01) ──
assert_element "destination bar"     "type=Text&text=Delivery to 1600 Amphitheater Way"
assert_element "highlight section"   "type=Text&text=Android's picks"
assert_element "popular section"     "type=Text&text=Popular on Jetsnack"
assert_element "filter chip"         "type=Text&text=Gluten-free"
android_shot 01-home

# ── Filters sheet (gold compact-03): filter circle → sheet → close ──
adb_ shell input swipe 105 410 105 410 120
sleep 2
assert_element "filters title"       "type=Text&text=Filters"
assert_element "sort default row"    "type=Text&text=Android's favorite (default)"
assert_element "price chip"          "type=Text&text=\$\$\$\$"
android_shot 02-filters
adb_ shell input swipe 250 795 250 795 120   # X close
sleep 2

# ── Snack detail (gold compact-04): cupcake card → detail → back ──
adb_ shell input swipe 258 976 258 976 120
sleep 2.5
assert_element "detail price"        "type=Text&text=\$2.99"
assert_element "details header"      "type=Text&text=Details"
assert_element "see more"            "type=Text&text=SEE MORE"
assert_element "add to cart"         "type=Text&text=ADD TO CART"
android_shot 03-detail
adb_ shell input swipe 101 240 101 240 120   # up circle
sleep 2
assert_element "back on feed"        "type=Text&text=Android's picks"

# ── Search (gold compact-06) ──
adb_ shell input swipe 570 2700 570 2700 120
sleep 2
assert_element "categories grid"     "type=Text&text=Categories"
assert_element "lifestyles grid"     "type=Text&text=Lifestyles"
assert_element "category card"       "type=Text&text=Fruit snacks"
android_shot 04-search

# ── Cart (gold compact-07): totals + row remove ──
adb_ shell input swipe 800 2700 800 2700 120
sleep 2
# The selected pill's label only exists while selected — proves the bar follows the tab.
assert_element "cart pill selected"  "type=Text&text=MY CART"
assert_element "order header"        "type=Text&text=Order (6 items)"
assert_element "cart row"            "type=Text&text=Ice Cream Sandwich"
assert_element "summary total"       "type=Text&text=\$58.13"
assert_element "checkout"            "type=Text&text=Checkout"
android_shot 05-cart
# Remove the KitKat row (X at its top-right) → totals recompute.
adb_ shell input swipe 1185 1330 1185 1330 120
sleep 2
assert_element "recomputed header"   "type=Text&text=Order (5 items)"
assert_element "recomputed total"    "type=Text&text=\$52.64"
android_shot 06-cart-removed

# ── Profile (gold compact-08 is the WIP screen) + home restore ──
adb_ shell input swipe 1100 2700 1100 2700 120
sleep 2
assert_element "profile wip"         "type=Text&text=This is currently work in progress"
adb_ shell input swipe 220 2700 220 2700 120
sleep 2
assert_element "home restored"       "type=Text&text=Android's picks"
android_shot 07-home-again

smoke_end
