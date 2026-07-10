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
