# Jetcaster (M4) — parity backlog

Gold: ~/work/compose-samples/Jetcaster — **mobile/ + core/ only** (tv/wear/
glancewidget out of scope per plan). Survey 2026-07-15 (file:line citations
below are into that tree). Gold mobile-debug.apk built and ready for captures.

## App shape (values-from-source pointers)

- **Flat 2-destination NavHost** (JetcasterApp.kt:51-80): `home` and
  `player/{episodeUri}` — NOT a bottom-nav app. Podcast details is a
  **SupportingPaneScaffold supporting pane inside Home** (Home.kt:249), keyed
  by podcast URI, with a custom `calculateScaffoldDirective` (Home.kt:132-172:
  single pane when width OR height compact; 2 panes at expanded; tabletop →
  vertical partitions). All wrapped in `SharedTransitionLayout`.
- **Home** (Home.kt:362-441): radial-gradient background scrim → transparent
  Scaffold; top bar = M3 **SearchBar** (decorative, never expands —
  Home.kt:313) + LinearProgressIndicator while refreshing; content = ONE
  **LazyVerticalGrid(Adaptive(362dp))** switching Library/Discover on a
  bottom-center **HorizontalFloatingToolbar** pill (Home.kt:445-527).
  - Library: **HorizontalMultiBrowseCarousel** of followed podcasts
    (Home.kt:659) + "Latest Episodes" list.
  - Discover: LazyRow of **FilterChip** category tabs (Discover.kt:96-118) +
    per-category **HorizontalUncontainedCarousel** (PodcastCategory.kt:127) +
    episode list.
- **Episode rows** (EpisodeListItem.kt): Surface card + ripple, image shared
  element, **SwipeToDismissBox** swipe-to-remove (:79), HTML summary via
  HtmlTextContainer (:248).
- **PodcastDetails** (supporting pane; PodcastDetailsScreen.kt): LazyVerticalGrid
  header (280dp PodcastImage, expandable description, subscribe/notify
  **ButtonGroup of ToggleButtons** :291) + episode items.
- **Player** (PlayerScreen.kt): image-backed background with color scrim
  (:193 — surface@0.9 over the Coil image), vertical gradient scrims
  (:298/:358/:436), shared-element PlayerImage, **basicMarquee** title (:621),
  **Slider** (:696), play/pause **ToggleButton with morphing shapes** (:722) +
  ButtonGroup of prev/-10/+10/next IconButtons; fold-aware tabletop/book
  layouts via Accompanist TwoPane (:254-334).
- **Theme**: `MaterialExpressiveTheme`, and mobile ALWAYS uses the static
  **darkScheme** (Theme.kt:478; dynamicColor param never true). Fonts:
  Montserrat (already bundled for Jetsnack) + **RobotoFlex variable** (display
  styles, e.g. displayLarge 64sp weight 738 — nearest static weight is the
  documented approximation).

## Data / player (app-layer plan)

- Gold = **live RSS**: 18 hard-coded feeds (Feeds.kt:27-47), OkHttp + ROME
  parse incl. iTunes DTD module (PodcastFetcher.kt:54-157), Room persistence
  (5 entities), Coil artwork. **No artwork→theme extraction** (scrims only).
- Gold player is a **MOCK TICKER** (MockEpisodePlayer.kt:88-112 — coroutine
  delay loop advancing timeElapsed; media3 is in the version catalog but
  unreferenced). Comet's player will be the same mock — that IS parity.
- **Comet plan (per approved M4 shape)**: C# feed pipeline with TWO modes —
  - **fixture mode** (default for smokes/pixel work): bundled snapshot of a
    few real feed XMLs + bundled artwork, mirroring core/domain-testing
    PreviewData (PreviewData.kt:27-72) so renders are deterministic offline;
  - **live mode**: HttpClient fetch of the same 18 URLs, System.Xml parse
    (RSS 2.0 + iTunes namespace: image, summary, duration, categories).
  - Persistence: in-memory stores first (the gold's Room is an offline cache,
    not observable behavior); revisit only if a screen needs it.
- **Image v2 decision point** (plan): remote artwork = C#-side download cache
  + placeholder/crossfade on the existing Image node — bias C# cache over
  binding Coil, keeps iOS symmetric. Fixture mode sidesteps it for pixels;
  live mode exercises it.

## Capability classification

**Bound in the facade already (unwired → needs Comet nodes/controls):**
LazyVerticalGrid, HorizontalMultiBrowseCarousel, HorizontalUncontainedCarousel,
SearchBar (+Docked/Top variants), FilterChip, Slider, Linear/Circular
ProgressIndicator, TopAppBar, Snackbar/SnackbarHost, AlertDialog (Comet
control exists), IconButton (backlogged promotion from M3).

**Facade-missing (JNI binding work):** HorizontalFloatingToolbar (expressive),
ButtonGroup, plain ToggleButton + ToggleButtonShapes (morphing), 
SwipeToDismissBox, basicMarquee modifier.

**Comet-side capabilities:**
- SupportingPaneScaffold → map onto the existing ListDetail/NavigationSuite
  adaptive primitives (custom directive ≈ variantFor policy); assess whether
  the real M3-adaptive widget is warranted or the gold's custom directive
  makes hand-mapping faithful (it computes its own directive — same bar as
  Jetsnack's hand-composed chrome).
- **GradientSpec increment: Radial** — Home background radialGradientScrim
  (GradientScrim.kt:44) and ImageBackgroundRadialGradientScrim
  (ImageBackground.kt:43, + BlendMode.Multiply). Spec v2 (b4a0fb30) covers
  vertical scrims (per-stop alpha precomputes the exponential decay of
  GradientScrim.kt:72-138); radial is the next Direction/Kind value.
- Image v2 (above). HTML→FormattedText mapping for episode summaries.
- Shared-element transitions: same M3 decision — bounds-matched fallback,
  documented deviation (plan's 3-day timebox already spent in M3 decision).
- iOS twins: carousels → shim horizontal list w/ item sizing; floating
  toolbar → hand-composed pill (SwiftUI has no twin); marquee → static
  truncation deviation or manual animation.

## Deviations declared up front

- Mock player = gold behavior (not a deviation — the gold has no audio).
- Marquee, morphing ToggleButtonShapes, shared elements: approximate/static
  first pass, polish second pass (pre-agreed descope pattern).
- Fold postures (tabletop/book TwoPane): WindowSizeClass adaptivity IS in
  scope; hinge-specific postures are OUT per plan (foldable-hinge specifics
  excluded).
- displayLarge RobotoFlex weight 738 → nearest bundled static weight.

## Milestone checklist state

- [x] Source survey (this doc)
- [x] Gold mobile-debug.apk built
- [x] Gold captures, first set 2026-07-15 (emulator, live feeds):
      compact-01-discover / 02-podcast-details / 03-player in
      sample/Shared/Jetcaster/gold/. Capture notes: the floating
      Library|Discover toolbar renders TOP-docked overlapping the status bar
      on this build (survey said bottom-center — verify against source when
      building it) and its Library half sits under the status clock, so the
      library view + expanded/two-pane + offline-dialog captures are PENDING
      (grab during build phase; details pane already proves the pane nav).
      Player screen: episode CARD opens the player; the row's play button
      only toggles mock playback in place.
- [ ] Fixture feed snapshot + C# pipeline (fixture mode first)
- [ ] Framework: grid/carousel/search-bar/chip-row nodes; radial GradientSpec
- [ ] Screens: Home (Library/Discover) → PodcastDetails pane → Player
- [ ] Smokes both platforms; standalone app id; RESULTS row (+ Pixel 5 B2
      with Jetsnack per plan); /code-review
- [ ] Canvas feasibility spike (1 day, for M5 JetLagged) DURING this sample
