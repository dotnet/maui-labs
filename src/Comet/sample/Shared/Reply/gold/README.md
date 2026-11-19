# Reply gold screenshots (fidelity contract)

Captured from the **Kotlin Reply app** (`~/work/compose-samples/Reply`, debug build)
running on the **Pixel_9_Pro emulator** (API 36, physical 1280x2856 px, density 480 →
3.0x scale, 427x952 dp). Adaptive states were driven with `adb shell wm size <WxH>`
(density unchanged), matching the smoke-script `android_resize` verb.

## Capture matrix

| File | wm size (px) | Window dp | Nav chrome | Content |
|---|---|---|---|---|
| compact-01-inbox.png | 1280x2856 (reset) | 427x952 | Bottom nav bar | Inbox list, docked search bar + profile, expanded Compose FAB |
| compact-02-detail.png | 1280x2856 | 427x952 | Bottom nav bar | Email detail (full-screen), back button + subject app bar, Reply/Reply All per thread item |
| compact-03-articles-comingsoon.png | 1280x2856 | 427x952 | Bottom nav bar | EmptyComingSoon ("Screen under construction") |
| compact-04-search-active.png | 1280x2856 | 427x952 | Bottom nav bar | DockedSearchBar active: back arrow, "No search history", scrim over list |
| compact-05-scrolled-fab-collapsed.png | 1280x2856 | 427x952 | Bottom nav bar | List scrolled; ExtendedFAB collapsed to icon-only (`expanded = lastScrolledBackward \|\| !canScrollBackward`) |
| medium-700dp-01-inbox-rail.png | 2100x2856 | 700x952 | Navigation rail | Rail: menu, FAB, 4 destinations (centered); single-pane list |
| medium-700dp-02-modal-drawer-open.png | 2100x2856 | 700x952 | Modal drawer over rail | REPLY header + close icon, Compose extended FAB, 4 drawer items, scrim |
| expanded-960dp-01-twopane-rail.png | 2880x2856 | 960x952 | Navigation rail | TwoPane 50/50: list + detail (openedEmail ?: first email) |
| expanded-1260dp-01-twopane-permanent-drawer.png | 3780x2856 | 1260x952 | Permanent drawer | Drawer (REPLY, Compose extended FAB, labeled items) + two-pane |

## Breakpoints (from source — ReplyNavigationComponents.kt / ReplyApp.kt)

- **Bottom nav:** width < 600dp OR height < 480dp (`isCompact()`), or tabletop posture.
- **Permanent drawer:** width-class Expanded (≥ 840dp) **AND window width ≥ 1200dp**.
- **Nav rail:** everything in between (600–1199dp).
- **Two-pane content (`DUAL_PANE`):** width-class Expanded (≥ 840dp) — so 840–1199dp
  is *rail + two-pane*; Medium (600–839dp) stays single-pane (unless folding posture).
- Modal drawer opens **only from the rail's menu item** (gestures disabled on
  compact/permanent; bottom nav has no entry point).

## Caveats (do not build these)

- **The warm beige/green scheme is Reply's OWN static palette, not Material You.**
  `ContrastAwareReplyTheme` defaults `dynamicColor = false` (Theme.kt:298) and
  MainActivity passes nothing; every color is a literal in `ui/theme/Color.kt`
  (primary `#805610`, tertiaryContainer `#D4EABB` = the green FAB, background
  `#FFF8F4`). Fidelity uses those exact hex values — deterministic on any device,
  no seed generator needed. (Contrast variants exist behind UiModeManager contrast;
  default contrast = `lightScheme`/`darkScheme`.)
- Status-bar clock/battery and the bottom gesture handle are OS chrome.
- compact-04: soft keyboard is not visible because the emulator reports a hardware
  keyboard (`show_ime_with_hard_keyboard` was enabled during capture to suppress
  Gboard's floating toolbar pill, which contaminated an earlier take).
