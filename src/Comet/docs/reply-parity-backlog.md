# Reply parity backlog (M1)

Gold standard: `~/work/compose-samples/Reply` (Kotlin). Gold screenshots + capture
matrix + breakpoint table: `sample/Shared/Reply/gold/` (self-captured 2026-07-05,
Pixel 9 Pro API 36). Method per `docs/sample-workflow-checklist.md`: this document is
the build contract — every widget/value cited `file:line` into the Kotlin source;
capability classified **WORKING / bound-unwired / facade-missing / no-control**.

App shape: 4 top-level routes (`ReplyApp.kt:128-153`) — Inbox (real), Articles /
DirectMessages / Groups (all `EmptyComingSoon`). One activity, no per-route back
stack beyond inbox→detail. All data static in-memory (`data/local/LocalEmailsDataProvider`);
no network, no images beyond bundled avatar drawables.

## 1. Adaptive chrome (the framework lift — see docs/adaptive-primitives-design.md)

Source of truth `ReplyNavigationComponents.kt:77-107` + `ReplyApp.kt:76-88`:

| Window | Nav chrome | Content |
|---|---|---|
| width <600dp OR height <480dp (`isCompact()` :77) or tabletop | NavigationBar (bottom) | single-pane |
| 600–839dp | NavigationRail | single-pane (dual only in folding posture :79-83) |
| 840–1199dp | NavigationRail | **two-pane** (`WindowWidthSizeClass.Expanded` :85) |
| ≥840dp class AND window ≥1200dp (:98-99) | PermanentNavigationDrawer | two-pane |

- Whole app wrapped in `ModalNavigationDrawer` (:122-137); gestures enabled only when
  open or rail mode (:113-114); opened ONLY by the rail's menu item (:151-155);
  `BackHandler` closes (:116-120). → Comet: **Drawer control exists** (ComposeDrawerNode)
  — needs modal-sheet content variant + gesture-enable flag; verify against gold
  `medium-700dp-02`.
- `NavigationSuiteScaffoldLayout` (:138) is **facade-missing** — per plan, DON'T bind it;
  hand-compose the 3 chrome variants switched on `CometWindowMetrics` (the pre-agreed
  fallback). Height ≥480dp centers rail/drawer content, else top (:103-107,
  `navigationMeasurePolicy` :435-471 — custom Layout; Yoga equivalent: header top,
  content centered in remaining space, `coerceAtLeast(header)`).
- Two-pane: accompanist `TwoPane` 50/50 split, 16dp gap (`ReplyListContent.kt:81-100`).
  Fold `displayFeatures` OUT of scope (plan: foldable-hinge specifics excluded) →
  plain HStack-based ListDetail primitive keyed off width class.
- Detail pane content when nothing opened: `openedEmail ?: emails.first()` (:94).
- Compact FAB only in BOTTOM_NAVIGATION mode (:112), aligned BottomEnd padding 16
  (:119-121).

Comet infra: `CometWindowMetrics` (WidthClass/HeightClass, Signal-reactive) landed in
M0 (`bf879a75`) — the chrome switch programs against it. REMAINING M0 deferral now due:
ComposeNode.AvailableSize re-plumb so per-root metrics (not the static) drive layout.

## 2. Nav chrome widgets

| Widget | Gold cite | Facade | Comet control | Class |
|---|---|---|---|---|
| NavigationBar + NavigationBarItem (icon-only, 4 items) | ReplyNavigationComponents.kt:237-252 | bound (NavigationBarItem.cs area — verify) | none | **bound-unwired** |
| NavigationRail + NavigationRailItem (menu + FAB header, centered items) | :172-234 | bound (NavigationRailItem.cs) | none | **bound-unwired** |
| PermanentDrawerSheet (min 200 / max 300dp :261, surfaceContainerHigh) | :255-337 | bound (ComposeBridges.cs) | none | **bound-unwired** |
| ModalDrawerSheet + scrim | :340-433 | bound (DrawerStateHolder.cs) | Drawer (verify variant) | **bound-unwired** |
| NavigationDrawerItem (selected pill, transparent unselected :326-328, label padding h16 :315) | :309-331 | bound | none | **bound-unwired** |
| FloatingActionButton 
(rail: tertiaryContainer, 18dp icon, pad top8/bottom32 :197-208) | :197-208 | bound | Fab (Jetchat) | **WORKING** (variant check) |
| ExtendedFloatingActionButton (drawers: fillMaxWidth, pad top8/bottom40 :282-300; compact list: text+icon, `expanded` collapse :113-126 ReplyListContent) | both files | bound (ExtendedFloatingActionButton.cs) | Fab is plain circle | **bound-unwired** — real ExtendedFAB control w/ `expanded` state (gold compact-05 = collapsed) |

Drawer/rail headers: "REPLY" `titleMedium` primary uppercase (:278-281, :365-369);
modal header adds `ic_menu_open` close IconButton (:370-375).

## 3. Inbox list screen (`ReplyListContent.kt`)

- Root Box `windowInsetsPadding(statusBars)` (:171) — **safe-area automation** is the
  planned kill-the-manual-topInset item (no-control today; App.Build(topInset) hack).
- `ReplyDockedSearchBar` pinned on top, h16/v16 padding (:172-180); LazyColumn under it
  `padding(top = 80.dp)` (:182-186).
- **DockedSearchBar + SearchBarDefaults.InputField** (`ReplyAppBars.kt:84-168`):
  facade-bound (SearchBarInputField.cs), no Comet control → **bound-unwired**.
  States (gold compact-04): collapsed = search icon + "Search emails" + 32dp profile
  trailing (pad 12 :117-124); expanded = back arrow (pad start16, click collapses+clears
  :97-107), content = "No search history" / "No item found" / results LazyColumn
  (contentPadding 16, spacedBy 4 :131-156) of **ListItem** (headline=subject,
  supporting=sender, leading 32dp avatar) → ListItem facade-bound, no control →
  **bound-unwired**. Search filter: subject OR fullName startsWith ignoreCase (:65-82).
  Item click → navigateToDetail + collapse (:149-153).
- **ReplyEmailListItem** (`ReplyEmailListItem.kt:52-142`): Card, pad h16/v4 (:64),
  inner Column pad 20 (:81); container = primaryContainer if selected /
  secondaryContainer if opened / surfaceVariant (:72-76) — gold compact-01 shows the
  opened first item darker. Comet Card exists? → verify; Jetchat used custom rows.
  Row: 40dp circular avatar (`ReplyProfileImage.kt`: Image clip CircleShape size 40);
  sender firstName + createdAt both `labelMedium` (:106-113); star IconButton in
  CircleShape surfaceContainerHigh bg, outline tint (:115-126). Subject `bodyLarge`
  pad top12/bottom8 (:129-133); body `bodyMedium` maxLines 2 ellipsis (:134-139).
- **Interactions**: `combinedClickable` onClick=detail, onLongClick=toggleSelection
  (:67-70) → needs long-press gesture (NOTE: comet-next HandleGesture still falls back
  longpress→tap; the longpress CLI work lives only on devflow/longpress-gesture branch).
  Avatar click (no ripple `indication = null` :84-87) also toggles selection;
  `AnimatedContent` swaps avatar ↔ `SelectedProfileImage` (primary circle + 24dp check
  :144-161) → AnimatedContent facade-bound (Transitions.cs) → **bound-unwired**;
  fallback = plain swap, animation second pass.
- ExtendedFAB collapse driver: `emailLazyListState.lastScrolledBackward ||
  !canScrollBackward` (:124-125) → map to ListView scroll-bridge signals
  (ScrollOffset/AtTop from Jetchat; may need lastScrolledBackward direction signal).
- List end spacer: `windowInsetsBottomHeight(systemBars)` (:200-202).

## 4. Email detail (`ReplyListContent.kt:207-225` + `ReplyAppBars.kt:173-228`)

- LazyColumn on inverseOnSurface bg (:209-211).
- **EmailDetailAppBar = M3 TopAppBar** (facade-bound, no Comet control →
  **bound-unwired**): container inverseOnSurface (:176-178); title Column — subject
  `titleMedium` onSurfaceVariant + "N Messages" `labelMedium` outline pad top4
  (:179-196), centered when fullscreen else start-aligned (:182-183); nav icon only
  fullscreen: **FilledIconButton** surface container, 14dp back icon, pad 8 (:199-214)
  → facade-bound; actions: more-vert IconButton onSurfaceVariant (:216-226). No
  scrollBehavior (that's JetNews).
- **ReplyEmailThreadItem** (`ReplyEmailThreadItem.kt:44-136`): Card surfaceContainerHigh
  (:47-49), pad h16/v4, inner pad 20; header row like list item but time hardcoded
  "20 mins ago" outline (:71-75), star bg surfaceContainer (:79-82); subject
  `bodyMedium` outline (:91-96); body `bodyLarge` onSurfaceVariant (:98-102);
  buttons row pad top20/bottom8 spacedBy 12 (:103-133): two **Button**s weight 1f,
  containerColor **surfaceBright**, text onSurface — plain M3 filled Button = WORKING
  (Jetchat) but surfaceBright color token must exist in theme bridge.
- Back: compact detail is `isDetailOnlyOpen` state w/ BackHandler (:141-147) — Comet
  NavigationView push or state-swap; two-pane never navigates.

## 5. EmptyComingSoon (`EmptyComingSoon.kt`)

Column centered: title `titleLarge` primary + subtitle `bodySmall` outline, pad 8 —
all WORKING (Text/VStack). Strings: "Screen under construction" / R.string subtitle.

## 6. Theme (values-from-source — DETERMINISTIC, no dynamic color)

- `ContrastAwareReplyTheme` defaults **dynamicColor = false** (`Theme.kt:294-317`);
  default contrast → static `lightScheme`/`darkScheme` (:37-111). **Every color is a
  literal in `ui/theme/Color.kt`** — port the light+dark tables verbatim (primary
  `#805610`, tertiaryContainer `#D4EABB`, background/surface `#FFF8F4`, etc).
  Contrast variants (UiModeManager) OUT of scope — note in RESULTS.
- Typography `Type.kt`: default Roboto, explicit sizes/weights (headlineLarge
  SemiBold 32/40 … ) — port the overridden styles; no custom fonts (unlike Jetchat).
- Shapes: `Shapes.kt` (verify — likely default M3).
- Jetchat theme generator NOT needed here; a static scheme ctor path suffices
  (`JetchatTheme.cs` pattern minus the seed generation).

## 7. Data layer

`data/`: Email(id, sender/recipients Accounts, subject, body, attachments, mailbox,
createdAt string, threads), MailboxType, LocalEmailsDataProvider static list, avatar
drawables. → straight C# port under `sample/Shared/Reply/Data/`; ViewModel
(`ReplyHomeViewModel.kt`) = replyHomeUIState { emails, openedEmail, selectedEmails,
isDetailOnlyOpen } + navigateToDetail/toggleSelectedEmail/closeDetailScreen →
Comet State/Signal.

## 8. Capability rollup (build order)

1. **Framework, host-test-first** (separate commits): ExtendedFAB control (+expanded
   signal); NavigationBar/Rail/DrawerItem controls; modal/permanent drawer variants;
   TopAppBar control; DockedSearchBar control; ListItem control; adaptive
   NavSuite + ListDetail primitives on CometWindowMetrics (design note first);
   safe-area statusBars inset automation; long-press gesture (port/redo — see branch
   note); AnimatedContent wiring (or defer, plain swap first pass).
2. **Sample screens**: theme port → data port → inbox list → detail → coming-soon →
   search → adaptive chrome → selection mode.
3. **Smoke**: `tools/smoke/reply.android.sh` as screens land (use `android_resize`
   for the 3 chrome asserts; elements: search placeholder, first-email subject,
   "Screen under construction", drawer items; drag for FAB collapse).
4. iOS gate after Android fidelity (SwiftUI twins: rail→sidebar-ish hand-comp,
   real resize observation lands here per M0 deferral).

Known iOS debt Reply will hit: real resize observation (SwiftUIBackendRoot reads
UIScreen bounds per pass only), drawer-row semantic-tap-doesn't-navigate (backlog),
no drag injector (scroll_by fallback).

## 9. Progress log (2026-07-06 — Android structural build-out COMPLETE)

DONE (commits 2be19e2b..9f40b105): NavigationSuite (bar/rail/permanent-drawer on real
M3 widgets, own-content swap) · ListDetail (two-pane ≥840 / compact push+BackHandler
round-trip) · ContentSwitcher route host · safe-area contract (SafeAreaDp; suite insets
content) · leaf-padding Yoga fix · full static ReplyTheme + per-screen Compose scheme ·
ReplyData verbatim port + gold avatars · inbox/detail/coming-soon screens ·
ExtendedFAB expand-at-top/collapse-on-scroll (ScrolledFromTop) · reply.android.sh
14/14 + jetchat 13/13.

REMAINING for M1 (updated 2026-07-09 — items 1/2/4/5 and most of 3 shipped; see
the dated ✅ sections below):
1. **Row long-press selection mode** (gold combinedClickable onLongClick →
   toggleSelectedEmail; avatar ↔ check swap via AnimatedContent; selected
   count in the UI) — the last unimplemented gold INTERACTION. Long-press
   gesture exists on the devflow/longpress-gesture branch (port or redo).
2. **ExtendedFAB re-expand on upward scroll** (gold `lastScrolledBackward ||
   !canScrollBackward` — we only re-expand at the very top; needs a scroll
   DIRECTION signal on ListView, both backends).
3. Small gold divergences (visual polish, non-blocking): detail status-bar
   strip is Background, gold paints it inverseOnSurface (needs per-route strip
   color or screen-owned top inset); Android search popup expands on focus
   only (Expanded ↔ SearchBarState facade sync); rail container inverseOnSurface
   (containerColor now exists control-side — pass it for the rail variant too).

### Review skips (2026-07-07, tracked)
- Leaf padding on NON-text Compose leaves (Image/Button/TextField-material) grows the
  box without insetting content; SwiftUI engine path has no PadsOwnContent twin — align
  both backends (generalize inset to all plain leaves) before any sample pads such leaves.
- Suite BottomBar variant: bar offset ignores the widget's internal bottom-inset growth
  (visible only with 3-button nav); use safeDp.Bottom in the bar offset + content math.
- Refactor debt: shared ComposeOwnContentNode base (5 copies of lifecycle scaffolding);
  EmitSignalProperty helper for the 7 backend partials; suite reusing bar/rail item
  rendering + drawer sync from their standalone nodes.
- Reply Release size snapshot ✅ (2026-07-08): single-RID (arm64) 26.1 MiB via the new
  `-p:CometSingleRid=true` probe-csproj switch (a raw `-p:RuntimeIdentifiers` breaks the
  netstandard2.0 source generator's copy step). Full RESULTS.md row filled: cold start
  3705ms vs gold 773ms (JIT + Compose init — the maturity gap to attack); inbox scroll
  jank AT PARITY with gold (13.0% vs 9.3%, near-identical percentiles).

## 10. iOS gate status (2026-07-07)

DONE (01da1d53, 44ee7093): shim maxLines + scroll-top handler (xcframework rebuilt);
SwiftUI hosted-composition twins (ContentSwitcher/ListDetail/NavigationSuite/SearchBar);
Text_MaxLines + ScrolledFromTop on iOS nodes; agent semantic-tap BUBBLING (fixes the
old drawer-row bug; Icons queryable by symbol); probe COMET_SCREEN=reply switch +
avatars. VERIFIED on iPhone 16 Pro sim: inbox renders near-gold (cards/avatars/
opened-highlight/2-line ellipsis/FAB/suite bar + pill, safe-area clean);
jetchat.ios.sh 14/14 (no regressions); reply.ios.sh inbox asserts 4/4.

✅ BLOCKER RESOLVED (26e3858a, 2026-07-08): the crash was NOT a context-graph
cycle — the env walk was innocent (GetValue frames just topped a much deeper
stack; the depth cap in 56f5deae was aimed at the wrong layer). The real loop,
recovered from the managed dump (`--console-pty` stderr — CoreCLR prints the
full managed stack on fatal overflow; DiagnosticReports .ips only shows
interpreter frames): hosted Refresh → Materialize → SwiftUIListNode.Rebuild →
ThreadItem's .FontSize → SetEnvironment → ReactiveEnv.SetValue →
EnsureFlushScheduled → FlushEntry runs INLINE (main thread, _flushing already
false during AfterFlush) → AfterFlush → ancestor twin Relayout → Layout →
Arrange(mid-build LD node, _shown==null) → Refresh again. Fixed in
ReactiveScheduler: FlushEntry re-entrancy guard + pass loop (see commit).
Host repro: BackendHostedCompositionChurnTests (nests 67 before / 5 after).
Also fixed: font-mapped Icons (Icon_Glyph path) lost their queryable symbol —
CometDevRegistry now falls back to Icon.Symbol for element text.
reply.ios.sh 13/13 → the iOS gate smoke is GREEN (inbox, detail round-trip via
app-bar back, route switch); jetchat 14/14 iOS + 13/13 Android, reply.android
14/14 — no regressions.
✅ POST-GATE POLISH DONE (2026-07-08, commits 199d95ad..b12a7abb):
- iOS search end-to-end (pill → expanded pane → typed query → gold-filtered
  result renders → close restores pill) — smoke grew a search section (18/18).
  Surfaced + fixed 4 framework gaps: HoldFlushes (atomic hosted swaps),
  node-generation disposal + registry pruning (stale-gen ghosts), SearchBar
  Expanded state on the CONTROL, ListView.ReloadData re-pulls items AND
  invalidates the row-view cache (opened-highlight now moves on iOS).
- iPad Pro 13 two-pane: rail + list + detail render side-by-side (gold
  840–1199 band); row tap swaps the detail in place; highlight follows.
- Android search: SearchBar itself now accepts the agent fill (TextChanged →
  expand + Query) since the M3 input field isn't a registered element —
  query pipeline verified (results rebuild). FOLLOW-UP: sync
  SearchBar.Expanded ↔ M3 SearchBarState so the popup opens/closes
  programmatically on Android (today expansion is focus-driven only);
  adb-IME text into the popup remains broken (upstream/IME quirk, agent
  fill is the supported path).
✅ iOS PARITY PASS 2 (2026-07-08, commit 77922ba0 — David flagged the FAB and
missing icons): ExtendedFAB now CONTRACTS on iOS (the node re-frames to a
Height-dp square with the right edge pinned — the shim only hides the label,
the frame was staying extended-wide) and uses the real M3 CornerLarge 16dp
shape (was a capsule). SearchBar.containerColor + NavigationSuite
containerColor/indicatorColor let the hand-composed iOS chrome wear the gold
M3 tokens (surfaceContainerHigh pill, surfaceContainer bar, secondaryContainer
indicator — Android's real widgets take these from the scheme); nav item
icons tinted onSurfaceVariant. Real app icons on BOTH platforms: per-variant
asset catalogs (iOS <AppIcon> property — icon-set NAME, so one Info.plist
serves all variants) / @mipmap + per-variant app_name strings (Android manifest
label was hardcoded); gold launcher art for reply/jetchat, the Comet mark
(art/comet_icon.svg) for the probe.
