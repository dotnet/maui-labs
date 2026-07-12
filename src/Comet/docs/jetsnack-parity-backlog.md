# Jetsnack (M3) — parity backlog

Gold: ~/work/compose-samples/Jetsnack (Kotlin). Golds self-captured 2026-07-10
(app-debug on the Pixel 9 Pro AVD): sample/Shared/Jetsnack/gold/ — 8 compact
(home / scrolled / filters sheet / detail / detail-scrolled / search / cart /
profile) + medium-700dp + expanded-1260dp.

## App shape (values-from-source pointers)

- **Custom design system, NOT stock M3**: JetsnackTheme (ui/theme) with its own
  color set (brand purple, GRADIENTS everywhere — card headers, section
  titles use tinted text, bottom bar brand background), custom JetsnackSurface/
  JetsnackButton/JetsnackBottomBar components (ui/components). The gold itself
  hand-composes its chrome — so Comet hand-composing the SAME structure from
  the same primitives IS the faithful reproduction here (unlike Reply/JetNews
  where stock M3 widgets were the target).
- Screens: Home (destination bar + filter chip row + snack collections as
  horizontal card rows), Snack detail (gradient header, circular image, price,
  Details/Ingredients, qty stepper + ADD TO CART bottom bar), Search
  (categories/lifestyles image grid), Cart (order rows + qty steppers +
  summary + checkout bar), Profile (work-in-progress placeholder).
- Model: Snack.kt / SnackCollection.kt / Filter.kt / Search.kt (~575 lines
  total — small port).
- Images: LOCAL drawables in this gold revision (drawable-nodpi *.jpg —
  copied to the Android probe; iOS bundle entries land with the screens).
  (An older Jetsnack used Coil remote URLs — not this checkout.)

## Capability classification (framework work expected)

- **Gradient fills**: snack card headers + bottom-bar/status accents are
  linear gradients. Comet Background() is solid-color today → needs a
  gradient background capability (Compose Brush.linearGradient / SwiftUI
  LinearGradient in the shim).
- Custom bottom bar: hand-composed (gold's own idiom) — NavigationSuite not
  required; selected item = pill with icon+label, others icon-only.
- Qty stepper, filter chips, filter SHEET (custom dialog), search grid
  (two-column image cards), cart swipe-to-remove (gold has it — classify).
- Detail screen: collapsing/parallax header behavior on scroll (gold
  ui/snackdetail — classify how deep to go).
- Shared-element transitions (SnackSharedElementKey) — likely out of scope;
  document as deviation.

## Status

- 2026-07-10: golds captured; backlog skeleton.
- 2026-07-10 (`a3e92c3a`): model/data + custom palette ported (28 snacks,
  collections, cart, filters as Signals; JetsnackColors roles + 8 gradient
  stop lists); snack photos in the Android probe; both probes compile.
  Next: gradient-fill capability (Compose Brush.linearGradient / shim
  LinearGradient), then the Home screen (destination bar + filter row +
  Highlight/Normal collection rows + custom bottom bar).

## Status 2026-07-10 (later) — Home feed LANDED on Android

Commits `8aef7b35`…`a4e62294`: BackgroundGradient capability (Compose
Brush.horizontalGradient / shim LinearGradient; BrushBridges stale-jclass
crash fixed — first real exercise of the vendored gradient path), Home feed
+ chrome near-gold (destination bar, chips, highlight gradient cards,
circle rows, hand-composed bottom bar with routes).

Known deviations / next:
- Gradient parallax: the gold's offsetGradientBackground shifts the band
  with scroll (gradientWidth 6×card); ours is a static horizontal gradient.
- Chip border: gold uses a diagonal fade gradient border; ours solid
  brand@40%. Filter icon metrics eyeballed.
- Section-header arrow: gold mirrors ic_arrow_back; ours arrow_forward glyph.
- Next increments: Snack detail (gradient header, qty stepper, ADD TO CART
  bar), Filters sheet (FiltersOpen signal is wired, sheet unbuilt), Search,
  Cart, iOS pass (bundle snack jpgs + verify gradients/rows), smokes,
  standalone app id, RESULTS row, /code-review.

## Status 2026-07-10 (night) — M3 COMPLETE on both platforms + reviewed

Landed since the Home increment: Snack detail (gradient header, italic
title/tagline via NEW Text italic support, SEE MORE/LESS, qty stepper, cart
bar), Filters sheet (scrim overlay, sort/price/category — the walk exposed
and fixed the framework-level invisible-overlay input interception), Search
(uiFloated bar + gradient category grids), Cart (live totals, row remove),
iOS pass (36 jpgs — BundleResource Remove+re-Include flattening trap),
standalone app id, smokes (android 24, ios 24), RESULTS row (678ms cold
start — best Comet number yet; jank at parity).

## /code-review round 2 (4412942c~1..HEAD) — outcome

FIXED (`06750c5f`/`8a8f1616` + earlier `3335eeda`): frozen bottom-bar pill
(Peek-built, never re-styled); GradientBackground missing invalidation + the
per-recomposition JNI brush rebuild (now cached per node); filters-overlay
per-rebuild subscription leak; icon-toggle double materialization
(IBackendManagesOwnContent); AsCard suppressing long-press; italic
measure/render divergence; BrushBridges publication ordering; iOS card
elevation lost in the AsCard promotion; _seeMore leaking across snacks; dead
BodyVersion/DetailContent signals; sign-safe shared FormatPrice; captured
gradient-index counter; Take2/Skip2 chip slicing.

REPORTED, BACKLOGGED:
- Stock-M3 promotions the gold demonstrably uses: DestinationBar → real
  TopAppBar; IconButton control for the ~13 hand-rolled circular icon
  buttons (section arrows, detail Up, cart X, filters close); filters
  MaxCalories Slider (Comet has a real Slider node), Lifestyle chip section,
  Reset button — currently silently omitted.
- Alpha-0 compose-nothing guard: stacks only (list/scroll/switcher/suite
  containers keep the input hole); tension with ComposeFabNode's
  keep-composed contract (hidden subtrees now lose remember{} state).
  Needs a node-level hit-test story; consider a first-class Overlay
  primitive instead of the reactive-Opacity idiom (3rd copy).
- Gradient payload is Color[]+horizontal only — the gold's parallax
  offsetGradientBackground/diagonal gradients need a richer spec (define
  the struct BEFORE more callers bake in the Color[] wire shape).
- IconToggleButton takes bool+callback (vs Toggle's Binding<bool>) — the
  node's Toggle_IsOn path is undriveable; bind it.
- Outlined TextField branch drops color/fontSize/returnType; borderless
  wins over outlined silently.
- FontSlant.Oblique silently dropped; iOS gradient stops re-marshal per
  rebuild; reload-storm granularity (whole sheet/cart per toggle vs the
  gold's single-node recomposition); shim bundledImage subdir search
  (would remove the csproj flattening incantation); QuantitySelector/CTA
  pill/placeholder-panel dedup.
