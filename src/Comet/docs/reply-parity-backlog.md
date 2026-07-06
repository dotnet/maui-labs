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
