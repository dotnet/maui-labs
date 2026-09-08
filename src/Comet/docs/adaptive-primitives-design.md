# Adaptive primitives design note (Reply → JetNews, Jetcaster)

Written before coding, per plan. Two framework primitives + the chrome controls they
compose. Everything keys off **`CometWindowMetrics`** (M0, `Backend/CometWindowMetrics.cs`):
reading `WidthClass`/`HeightClass`/`SizeDp` in a body/binding re-renders exactly the
views that read it — no new reactive machinery needed.

## 1. `NavigationSuite` (the chrome switcher)

Gold behavior (Reply): bottom bar <600dp, rail 600–1199dp, permanent drawer ≥1200dp
(AND width-class Expanded); modal drawer overlays everything, openable only in rail
mode. `NavigationSuiteScaffoldLayout` is NOT facade-bound and we deliberately don't
bind it (plan fallback): **hand-compose the three variants in one Comet container view**.

```csharp
new NavigationSuite()
    .Destinations(dests)              // icon + label + route key, selected index Binding
    .Header(...)                      // FAB/compose affordance per variant slot
    .DrawerContent(...)               // shared item list for modal+permanent sheets
    .Content(() => currentScreen)
```

- Body: `switch (this.GetWindowMetrics().WidthClass, SizeDp.Width)` → HStack(rail, content)
  | HStack(permanentSheet, content) | VStack(content, bottomBar); modal drawer wraps
  the whole thing (existing Drawer control, gestures gated like the gold).
- The suite type is **derived state, not stored** — resize re-runs the body; the
  content view instance is shared across variants so screen state (scroll, text)
  survives chrome swaps. Host test: resize across a breakpoint → chrome node type
  changes, content node identity stable.
- Per-variant chrome = real M3 widgets via new leaf controls (NavigationBar/Item,
  NavigationRail/Item, NavigationDrawerItem, sheets) — thin nodes over the already-bound
  facade composables, one commit each with FakeBackendNode tests.
- Rail/drawer header-top/content-centered placement (gold `navigationMeasurePolicy`):
  Yoga column with center-justified content block and `coerceAtLeast(header)` behavior —
  express as spacer-weighted layout, NOT a custom measure policy.

## 2. `ListDetail` (two-pane)

Gold: accompanist TwoPane 50/50, 16dp gap, ≥840dp only; detail falls back to first item;
compact pushes a full-screen detail with BackHandler.

```csharp
new ListDetail()
    .List(() => inbox)
    .Detail(() => opened ?? first)
    .IsDetailOpen(binding)            // compact: drives push/back; expanded: highlight only
```

- Body on WidthClass: Expanded → HStack(list.Frame(0.5f), gap 16, detail.Frame(0.5f));
  else → single pane where `IsDetailOpen` swaps list↔detail (NavigationView push on
  Android compact for real back handling).
- Fold postures explicitly out of scope (plan); API leaves room (`SplitFraction`,
  `Gap` modifiers) without modeling DisplayFeatures.
- Host tests: breakpoint crossing swaps pane structure; open-detail state preserved
  across the swap (the gold's `closeDetailScreen` LaunchedEffect on contentType is the
  behavior to match: entering single-pane with detail-not-explicitly-open shows list).

## 3. Plumbing prerequisite (M0 deferral now due)

`ComposeNode.AvailableSize` is still a process-wide static fed by the root; the
primitives read per-root `GetWindowMetrics()`. Re-plumb: ComposeBackendRoot owns a
`CometWindowMetrics` instance, installs it via `view.WindowMetrics(m)`, updates it in
LayoutChange (Shared stays as fallback + keeps updating for single-window). SwiftUI
root same in RunLayout; REAL iOS resize observation (viewWillTransition/scene geometry)
lands at the Reply iOS gate.

## Test/verify hooks

- Smoke: `android_resize 2100 2856` → assert rail element present, bottom bar absent;
  `3780x2856` → drawer items with labels present; reset → bottom bar back. (Verified in
  M0 that wm-size config change re-creates the activity and the app survives.)
- The 3 gold chrome screenshots are the pixel contract (`sample/Shared/Reply/gold/`).
