#!/usr/bin/env bash
# Jetsnack iOS smoke: feed → filters sheet → snack detail → search → cart math →
# profile → home. The chrome is Comet-composed on both platforms, so items are
# registered elements.
source "$(dirname "$0")/lib.sh"

BUNDLE=com.comet.swiftuiprobe
smoke_begin jetsnack.ios

xcrun simctl terminate "$IOS_SIM" "$BUNDLE" 2>/dev/null || true
SIMCTL_CHILD_COMET_SCREEN=jetsnack xcrun simctl launch "$IOS_SIM" "$BUNDLE" > /dev/null
sleep 4
ios_agent_discover
agent_wait

# --- Feed ----------------------------------------------------------------------
ios_shot 01-home
assert_element "destination bar"   "type=Text&text=Delivery to 1600 Amphitheater Way"
assert_element "highlight section" "type=Text&text=Android's picks"
assert_element "filter chip"       "type=Text&text=Gluten-free"

# --- Filters sheet --------------------------------------------------------------
FILTER=$(element_id "type=Icon&text=filter_list")
check "open filters" tap "$FILTER"
sleep 2
assert_element "filters title"     "type=Text&text=Filters"
assert_element "sort default"      "type=Text&text=Android's favorite (default)"
ios_shot 02-filters
CLOSE=$(element_id "type=Icon&text=close")
check "close filters" tap "$CLOSE"
sleep 2

# --- Snack detail ----------------------------------------------------------------
CARD=$(element_id "type=Text&text=Cupcake")
check "open detail" tap "$CARD"
sleep 2
assert_element "detail price"      "type=Text&text=\$2.99"
assert_element "details header"    "type=Text&text=Details"
assert_element "add to cart"       "type=Text&text=ADD TO CART"
ios_shot 03-detail
BACK=$(element_id "type=Icon&text=arrow_back")
check "detail up" tap "$BACK"
sleep 2
assert_element "back on feed"      "type=Text&text=Android's picks"

# --- Search ----------------------------------------------------------------------
SEARCH=$(element_id "type=Icon&text=search")
check "search tab" tap "$SEARCH"
sleep 2
assert_element "categories"        "type=Text&text=Categories"
assert_element "lifestyles"        "type=Text&text=Lifestyles"
ios_shot 04-search

# --- Cart ------------------------------------------------------------------------
CART=$(element_id "type=Icon&text=shopping_cart")
check "cart tab" tap "$CART"
sleep 2
assert_element "order header"      "type=Text&text=Order (6 items)"
assert_element "summary total"     "type=Text&text=\$58.13"
assert_element "checkout"          "type=Text&text=Checkout"
ios_shot 05-cart

# --- Profile + home restore --------------------------------------------------------
PROFILE=$(element_id "type=Icon&text=account_circle")
check "profile tab" tap "$PROFILE"
sleep 2
assert_element "profile wip"       "type=Text&text=This is currently work in progress"
HOME=$(element_id "type=Icon&text=home")
check "home tab" tap "$HOME"
sleep 2
assert_element "home restored"     "type=Text&text=Android's picks"
ios_shot 06-home-again

smoke_end
