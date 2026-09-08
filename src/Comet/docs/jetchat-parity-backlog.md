# Jetchat → Comet parity backlog

Purpose: use Google's **Jetchat** (android/compose-samples, the gold standard) to find and fill
gaps in the **Comet node backend** (Jetpack Compose on Android). This is not about imitating the
sample — each item below is a **Comet capability** the sample happens to exercise.

**Method (important):** screenshots identify *what* differs (missing features, layout structure);
every **color / size / spacing / lineHeight / weight** value is taken from **source**, never
eyeballed or color-picked. Source root: `~/work/compose-samples/Jetchat/app/src/main/java/com/example/compose/jetchat`
(checkout `d3ff757b`). Citations below are `file:line`.

**Status (2026-06-14):** ✅ §0 value corrections (`d2743458`), ✅ **T1 Material You dynamic color**
(`2a7e805b`), ✅ **T3 explicit line height** (`<this commit>` — `View.LineHeight(sp)` → bubbles get
bodyLarge 16/24; letterSpacing deferred, facade `Sp` integer-only + negligible). All verified Pixel 5.
**Next: C1.** Facade is ready (`LazyColumn.State`/`.ReverseLayout`, `LazyListState.CanScrollForward`/
`AnimateScrollToItemAsync`, `composer.RememberLazyListState`) — no facade change. C1 design:
(1) `ListView` exposes a `Signal<bool>` "scrolled-away" + a `ScrollToBottom()`; (2) `ComposeListNode`
remembers a `LazyListState`, reads `CanScrollForward` (boundary-triggered, so no per-frame recompose →
no scroll-jank regression) and marshals it to the signal, and runs `AnimateScrollToItemAsync` on the
remembered coroutine scope; (3) the JumpToBottom `ExtendedFloatingActionButton` (surface/primary,
h36, `ic_arrow_downward` 18dp) overlays via ZStack. **Sub-dependency discovered: node-level REACTIVE
VISIBILITY** (show/hide the button from the signal without re-running the whole tree) — currently the
conversation tree is static; needs a `.Visible(signal)`/opacity node property or AnimatedVisibility.
That's the real capability C1 unblocks (and it generalizes).
✅ **D1 drawer structure** (`<commit>`): "Chats" + leading logo pills + "(you)" + Settings, verified.
✅ **C1 scroll-state + reactive visibility** (`<this commit>`): `ComposeNode` now honors `Opacity`/
`IsVisible` → `Modifier.Alpha` + skip-clickable-when-faded (the **reactive-visibility capability**);
`View.Backend` emits `Opacity` whenever explicitly set (even =1) so a toggle back to default reaches
the node. `IListView` gained a scroll bridge (`ScrolledAway` signal + `RegisterScroller`/`ScrollToBottom`);
`ComposeListNode` remembers a `LazyListState`, reads `CanScrollForward` in composition (boundary-
triggered) → marshals to `ScrolledAway`, and registers an `AnimateScrollToItemAsync(last)` scroller.
The Jetchat **JumpToBottom** FAB binds its opacity to `ScrolledAway` and taps → `ScrollToBottom()`.
Verified Pixel 5: fades in at top / on scroll-up, animates to bottom on tap, fades out at the bottom.
**Follow-up C1a:** the FAB is a styled Comet view (like the profile FAB); a real
`ExtendedFloatingActionButton` *control* (Comet view → facade node) is deferred (same lift as the
I-cluster "real widget" work). Box child `align`/`fillMaxSize` for the overlay handled by the existing
ZStack engine path (no new unknowns). ✅ Follow-up (D1 color) DONE: the drawer chat-icon is now the
jetchat logo TINTED monochrome (`onSurfaceVariant`, or `primary` when selected). `ComposeIconNode`
routes a multicolor asset through the tinted Icon path WHEN an explicit `.Color()` is set (else the
untinted Image path keeps its colors), so the channel-bar logo stays multicolor (dropped its no-op
`.Color`) while the drawer rows tint. Verified Pixel 5.

**Reframing finding:** the captured screenshots are rose/maroon because Jetchat runs
`JetchatTheme(isDynamicColor = true)` → `dynamicLightColorScheme(context)` (`theme/Themes.kt:91`),
i.e. **Material You from the wallpaper**. The blue scheme in our code is only the static fallback
(`JetchatLightColorScheme`, `theme/Themes.kt`). So do not "match" the rose pixels — implement the
capability and use the static tokens as the canonical values.

---

## 0. Value corrections to existing code (bugs from earlier screenshot-sampling)

These are wrong *values* in `sample/Shared/Jetchat/JetchatTheme.cs`; the widgets are already correct.

| Our current value | Correct (source) | Source |
|---|---|---|
| `TertiaryContainer = #F8D8F0` (FAB bg, pink) | `tertiaryContainer = Yellow90 = #FFDE9C` | `theme/Themes.kt` `JetchatLightColorScheme`; `theme/Color.kt` |
| `OnTertiaryContainer = #3A2A33` | `onTertiaryContainer = Yellow10 = #261900` | same |
| `SurfaceTinted #E7E9F8` used for the **header** | header is `CenterAlignedTopAppBar` → default container = **`surface` #FBFDFD** (no tint) | `components/JetchatAppBar.kt:46` |
| same hex used for the **footer** | footer = `Surface(tonalElevation = 2.dp)` → **computed** tonal overlay; selector panel = `8.dp` | `conversation/UserInput.kt:151,224` |
| footer action-icon tint ≈ primary | footer `Surface(contentColor = secondary)` → `DarkBlue40 #3648EA` | `conversation/UserInput.kt:151` |
| lineHeight = `round(sp × 1.3)` heuristic | **explicit** per style (see T3) | `theme/Typography.kt` |
| `LetterSpacing.Zero` everywhere | per style (see T3) | `theme/Typography.kt` |

---

## Theming capabilities

### T1 — Material 3 dynamic color (Material You) + dark mode  ⭐ foundational
- **Source:** `theme/Themes.kt:91–110` — `JetchatTheme(isDarkTheme = isSystemInDarkTheme(), isDynamicColor = true)`; `dynamicLightColorScheme(context)` / `dynamicDarkColorScheme(context)` on API ≥ 31, else `JetchatLightColorScheme` / `JetchatDarkColorScheme`.
- **Comet gap:** consume a real Material `ColorScheme` provider (incl. the platform dynamic scheme) and respond to system dark mode; feed it through the existing `ComposeBackendRoot.WrapContent` theme hook. Today the app hardcodes a light scheme.
- **Canonical static tokens** (`theme/Themes.kt`, raw in `theme/Color.kt`): primary `Blue40 #1546F6`, onPrimary white, primaryContainer `Blue90 #DDE1FF`, secondary `DarkBlue40 #3648EA`, surface/background `Grey99 #FBFDFD`, onSurface `Grey10 #191C1D`, surfaceVariant `BlueGrey90 #E2E1EC`, onSurfaceVariant `BlueGrey30 #45464F`, tertiary `Yellow40 #7A5900`, tertiaryContainer `Yellow90 #FFDE9C`, onTertiaryContainer `Yellow10 #261900`, outline `BlueGrey50 #767680`.

### T2 — Tonal elevation (`surfaceColorAtElevation`)
- **Source:** footer `Surface(tonalElevation = 2.dp)` (`conversation/UserInput.kt:151`); selector panel `Surface(tonalElevation = 8.dp)` (`:224`).
- **Comet gap:** compute the M3 tonal overlay (primary over surface at an elevation-derived alpha) instead of a flat sampled hex. Pairs with T1.

### T3 — Type scale: explicit lineHeight + letterSpacing
- **Source:** `theme/Typography.kt` — replace our `1.3×` heuristic with exact values:
  - headlineSmall: Montserrat SemiBold **24 / 32**
  - titleMedium: Montserrat SemiBold **16 / 24**, ls 0.1
  - titleSmall: Karla Bold **14 / 20**
  - bodyLarge: Karla Normal **16 / 24**, ls **0.15**
  - bodyMedium: Montserrat Medium **14 / 20**, ls 0.25
  - bodySmall: Karla Bold **12 / 16**, ls **0.4**
  - labelLarge: Montserrat SemiBold **14 / 20**, ls 0.1
  - labelMedium: Montserrat SemiBold **12 / 16**, ls 0.5
  - labelSmall: Montserrat SemiBold **11 / 16**
- **Comet gap:** carry explicit `lineHeight` + `letterSpacing` through the type system and the text measure/baseline path (bodyLarge is **24**, not 20.8 — this shifts bubble + profile line spacing and the baseline math). Families/weights/sizes already match.

---

## Conversation screen (`conversation/Conversation.kt`, `JumpToBottom.kt`)

### C1 — Jump-to-bottom button  ✅ (unlocked the scroll-state + reactive-visibility capabilities)
- **Source:** `JumpToBottom.kt` — `ExtendedFloatingActionButton`, icon `ic_arrow_downward` (height 18dp), text `R.string.jumpBottom` ("Jump to bottom"), `containerColor = surface`, `contentColor = primary`, height **36dp**, visibility via `updateTransition` + `animateDp` offset between **-32dp** (gone) and **32dp** (visible). Trigger: `firstVisibleItemIndex != 0 || firstVisibleItemScrollOffset > 56.dp` (`Conversation.kt:329–336`, `JumpToBottomThreshold = 56.dp:563`), `align(BottomCenter)`.
- **Comet gap:** expose `LazyListState` (first-visible index/offset) + `scrollToItem(0)` from `ListView`/`ComposeListNode`, plus animated show/hide. Today our list has no scroll awareness.

### C2 — reverseLayout + auto-scroll on send  ◑ (auto-scroll-on-send done; reverseLayout pending)
- **Source:** `Messages(...)` uses a `LazyColumn(reverseLayout = true)` (newest at bottom); `UserInput(resetScroll = { scope.launch { scrollState.scrollToItem(0) } })`.
- **Done (with I5, `<this commit>`):** Send appends a "me" `MsgRow`, `ListView.ReloadData()` (now also drives the node backend via `UpdateBackendNode` → re-emit `List_Version` → recompose) rebuilds the LazyColumn, and `ScrollToBottom()` (C1 scroller) animates to the new message; the field clears via the two-way `InputText` signal.
- **Pending:** `reverseLayout` (open at the newest message instead of the top) — needs the list to expose `ReverseLayout` + open scrolled-to-bottom.

### C3 — Date day-headers by real date
- **Source:** `Conversation.kt:300–306` emit `DayHeader("20 Aug")` and `DayHeader("Today")`; `DayHeader` (`:445`): row of `DayHeaderLine` + Text `labelSmall`, `onSurfaceVariant`, `padding(vertical=8, horizontal=16).height(16)`; divider color `onSurface.copy(alpha=0.12f)` (`:463`). We hardcode a single "Today".

### C4 — Author avatar double-ring (verify ours matches)
- **Source:** `Image(...).padding(horizontal=16).size(42).border(1.5.dp, borderColor, CircleShape).border(3.dp, surface, CircleShape)`; `borderColor = primary` (me) / `tertiary` (others) (`Conversation.kt` Message ~357–397). Non-first-in-group rows use a `Spacer(width = 74.dp)` gutter (16+42+16). Group spacing: `padding(top = 8.dp)` between authors; within a group `Spacer(height = 8.dp)` then `4.dp`.
- **Already matches** (commit f3633806 FlexShrink + rings); re-confirm the **two** borders (1.5dp color + 3dp surface gap) and the `tertiary` ring for others (now `Yellow40`, was sampled).

### Already faithful (for reference, do not redo)
- ChatItemBubble: me = `primary`, others = `surfaceVariant`; shape `RoundedCornerShape(4,20,20,20)`; text `padding(16)`; image attachment `size(160)`, `Spacer(4)` (`:473–508`).
- AuthorNameTimestamp: name `titleMedium` `paddingFrom(LastBaseline, after=8.dp)`, `Spacer(8)`, timestamp `bodySmall` `alignBy(LastBaseline)` `onSurfaceVariant` (`:422–443`) — our `.AlignBaseline()` covers this.
- ChannelNameBar: title `titleMedium`, "N members" `bodySmall`/`onSurfaceVariant`; search/info `Icon` tint `onSurfaceVariant` `padding(horizontal=12, vertical=16)` (`:222–278`). Nav icon = `JetchatIcon` `size(64).padding(16)` (`JetchatAppBar.kt`).

---

## Input bar (`conversation/UserInput.kt`) — the largest cluster

### I1 — InputSelector state machine + expandable panel  ⭐
- **Source:** `InputSelector` enum `{ NONE, MAP, DM, EMOJI, PHONE, PICTURE }` (`:113`); `UserInput` (`:135`) tracks `currentInputSelector`; `SelectorExpanded` (`:212`) shows a `Surface(tonalElevation = 8.dp)` (`:224`) panel below the input that swaps content by selector; selector row `UserInputSelector` `height(72).padding(start=16,end=16,bottom=16)` (`:265`).
- **Comet gap:** stateful composite input with an expandable selector region.

### I2 — Selected action-icon state
- **Source:** `InputSelectorButton` (`:345`): when selected, `Modifier.background(color = LocalContentColor.current, shape = RoundedCornerShape(14.dp))`, icon tint `contentColorFor(LocalContentColor)`; `IconButton`, icon `padding(8).size(56)`. Icons: `ic_mood` (EMOJI), `ic_alternate_email` (DM), `ic_insert_photo` (PICTURE), `ic_place` (MAP), `ic_duo` (PHONE).
- **Note:** the maroon "teardrop" above a selected icon in the screenshots is the **OS text-cursor drag handle** (the field was focused), **not** a Jetchat element — do not build it.

### I3 — Emoji selector (EMOJI/STICKER tabs + emoji table)
- **Source:** `EmojiSelector` (`:570`) with `ExtendedSelectorInnerButton` tabs (`:609`); `EmojiTable` (`:633`) = `Column(fillMaxWidth) { repeat(4) { Row(fillMaxWidth) { repeat(EMOJI_COLUMNS) { Text(emoji, clickable, textAlign=Center, Modifier.weight(1f)) } } } }`.
- **Comet gap:** *minor* — this is **weighted Rows, not a Grid** (Yoga `FlexGrow` already supports it). Real needs: a tab row + insert-emoji-at-cursor (see I7). (No `LazyVerticalGrid` required — earlier assumption corrected by reading source.)

### I4 — Dialog / AlertDialog on the node backend  ✅ (`<this commit>`)
- **Source:** `NotAvailablePopup` (`:381`) = Material `AlertDialog` "Functionality not available 🙊" + CLOSE (`UiExtras.kt` `FunctionalityNotAvailablePopup`); `FunctionalityNotAvailablePanel` (`:237`) = "Functionality currently not available / Grab a beverage and check back later!" (panel, not dialog).
- **Done:** new Comet `AlertDialog` control (`Controls/AlertDialog.cs`: `Signal<bool> IsOpen` + Text/ConfirmButton/Title?/DismissButton? slot Views) → `ComposeAlertDialogNode` renders the real Material 3 `AlertDialog` (own window + scrim) only while open, materializing the slot Views and dropping them into the facade `ConfirmButton`/`Text`/`Title`/`DismissButton` slots (laid out by Material, no Yoga). `Dialog_IsOpen` prop (224) + `DialogDismissed` event (12), wired exactly like Drawer (signal→node, onDismissRequest→event→IsOpen=false). `IBackendManagesOwnContent` so it's a zero-size leaf in its parent layout — can sit anywhere. iOS = empty placeholder node (SwiftUI `.alert` deferred, Android-first). Triggered faithfully from the DM "@" selector (`InputSelector.DM -> NotAvailablePopup`). Verified Pixel 5: opens on "@", dismisses via CLOSE button AND scrim tap. **Follow-up I4a:** `FunctionalityNotAvailablePanel` (the inline panel, not the dialog) + a `TextButton` variant for CLOSE (currently a plain styled Button).

### I5 — Send button reactive enabled/disabled  ✅ (`<this commit>`)
- **Source:** `Button` `height(36)`, `enabled = sendMessageEnabled`, `border = if(!enabled) BorderStroke(1.dp, onSurface.copy(0.3f)) else null`, `contentPadding = PaddingValues(0)`, `colors = ButtonDefaults.buttonColors(disabledContentColor = onSurface.copy(0.3f))`, Text `padding(horizontal=16)` (`:265` block). Empty → outlined/transparent; text present → filled `primary`.
- **Done:** the composer text is a two-way `Signal<string> InputText` (bound via `SignalExtensions.TextField`); the Send button restyles reactively from it (subscribe `PropertyChanged` → `send.Outlined(false).Color(OnPrimary)` filled when text present, `Outlined(true).Color(Disabled)` bordered-grey when empty). Added `Button.Outlined(bool)` + made `Button_Outlined` emit both ways (the set-only patch would otherwise leave it stuck outlined — same fix as Opacity). Verified Pixel 5: empty=outlined/grey, type→filled primary, send→clears→back to outlined.

### I6 — Voice record (mic): long-press + drag + animated overlay
- **Source:** `RecordButton.kt`; `RecordingIndicator` (`UserInput.kt:505`) — press-and-hold record, swipe-to-cancel, pulsing dot.
- **Comet gap:** long-press + drag gestures + animated overlay. Larger; later.

### I7 — TextField: cursor/selection + focus/IME coordination
- **Source:** `UserInputText`/`UserInputTextField` (`:391–503`), `height(64)`, text `padding(start=32)`, `imeAction = Send`; emoji insert uses `TextFieldValue.addText` at the selection (`:197`); opening the emoji panel hides the keyboard and vice-versa.
- **Comet gap:** `TextFieldValue` selection/caret + focus/IME control. Feeds I3 (insert at cursor) and I1 (keyboard ⇄ selector).

---

## Navigation drawer (`components/JetchatDrawer.kt`)

### D1 — Drawer structure parity
- **Source:** `JetchatDrawerContent` (`:65`): `DrawerItemHeader` "Chats" / "Recent Profiles" / "Settings" (`heightIn(min=52).padding(horizontal=28)`, `bodySmall`/`onSurfaceVariant`, `:117`).
  - `ChatItem` (`:133`): `height(56).padding(horizontal=12).clip(CircleShape)`; selected → `background(primaryContainer)`; leading `Icon` (jetchat logo) tint `primary` (selected) / `onSurfaceVariant`, `padding(start=16,top=16,bottom=16)`; label `bodyMedium`, `primary`/`onSurface`, `padding(start=12)`. Two chats: "composers" (selected), "droidcon-nyc".
  - `ProfileItem` (`:174`): same 56dp pill; avatar `Image size(24).clip(CircleShape)` (or `Spacer` if none), `padding(start=16,top=16,bottom=16)`; label `bodyMedium`/`onSurface`. "Ali Conors **(you)**", "Taylor Brooks".
  - `WidgetDiscoverability` (`:246`) under "Settings": "Add Widget to Home Page".
- **Comet gap:** mostly content/structure (section header "Chats" not "Channels", leading chat icons, `(you)` suffix, dynamic `primaryContainer` selected pill). Our drawer is close but differs in section name + per-chat icons.

---

## Profile (`profile/Profile.kt`) — mostly done; deltas

### P1 — FAB float position + dynamic color
- **Source:** `FloatingActionButton` `padding(16).align(BottomEnd).offset(y = -100.dp).height(48).widthIn(min=48)`, `containerColor = tertiaryContainer`, icon `ic_create` (me, "Edit Profile") / `ic_chat` (other, "Message") (`:113,233–245`).
- **Delta:** ours uses `Margin(bottom: 24 + inset)`; source is `padding(16) + offset(y = -100.dp)` (≈116dp lift). FAB color falls out of T1 (`tertiaryContainer`).

### P2 — Parallax photo header
- **Source:** `ProfileHeader` (`:178`): `Image.heightIn(max = containerHeight/2).fillMaxWidth().padding(start=16, top=offsetDp, end=16).clip(CircleShape)`, `ContentScale.Crop`; `offset = scrollState.value / 2` (`:179`) → the photo scrolls up at half speed.
- **Comet gap:** scroll-offset-driven layout (shares C1 scroll-state capability).

### Already faithful (commit 5ae5833b)
- `UserInfoFields`: `Spacer(8)`, `NameAndPosition`, 4×`ProfileProperty` (`:120`). `NameAndPosition` `Column(padding horizontal 16)`, Name `baselineHeight(32)`, Position `padding(bottom=20).baselineHeight(24)` (`:144`). `ProfileProperty` `Column(padding start=16,end=16,bottom=16)`, Label + value `baselineHeight(24)`, link = `primary` (`:203`).

---

## App / navigation
- **Source:** single-Activity `NavHost`; drawer hosts conversation + profile; `AnimatedContent` transitions.
- **Comet gap (minor):** animated nav transitions (we have push/pop; commit ca096220).

---

## Parked (need framework infrastructure that isn't built yet)
- **I1/I2/I3 — InputSelector state machine + expandable selector panel + emoji table.** The panel
  appears below the input and swaps content by selector (EMOJI table / "not available" panel), with
  the active icon highlighted. The icon highlight is reactive-property (doable), but the **panel's
  appear/collapse + content-swap is a structural reactive change** — clean only via root-Component
  structural re-render (**task #28**, still open) or a new conditional-content "expandable panel"
  node (the AlertDialog/Drawer pattern: always in the tree, renders the active panel from a
  MutableState). Parked to avoid a half-built unverifiable state unattended. The DM selector already
  shows the real `NotAvailablePopup` (I4).
- **P2 — parallax profile photo.** Needs a **ScrollView scroll-offset bridge** (the C1 pattern, but
  for `ComposeScrollNode`'s `ScrollState.Value` → drive the photo's translation at half speed).
  Buildable; parked as a lower-value flourish.
- **I7 (advanced) — TextField caret/selection + IME action + insert-at-cursor.** Basic input + send +
  clear works (I5); the advanced `TextFieldValue` selection/IME coordination is facade-deep and feeds
  I1/I3 (insert emoji at cursor). Parked with I1.
- **I6 — voice record (mic).** Long-press + drag + animated overlay. Largest input item; later.

## Out of scope (not a Comet in-app gap)
- **"Add Widget to Home Page"** launches a **Glance home-screen App Widget** (`widget/`), an OS/launcher feature, not in-app UI. N/A for Comet UI parity.

---

## Suggested sequencing
`T1 (dynamic color)` + `0 (value fixes)` → `T2 / T3 (tonal + type values)` → `C1 (scroll state)` →
`I4 (dialog)` + `I5/I7 (textfield/send)` → `I1/I2 (selector)` → `I3 (emoji)` →
animation → `I6 (voice)`. `C3 / D1 / P1` are quick wins anytime. The framework-capability builds are
**T1, T2, C1, I4, I7**; most other items compose from those.

---

## Addendum 2026-07-01 (autonomous run)

- **I7 / P4 caret+IME: DONE** (`210ede7c`) — `BasicTextField(TextFieldValue)` bound;
  the composer tracks the caret, the emoji table inserts AT the caret (gold
  `addText`), typing echo never resets the caret. Deferred: `reverseLayout` (C2
  remainder), TextFieldValue composition/IME-region styling.
- **P7 IME insets: DONE** (`7c592914`) — Android-15 edge-to-edge makes AdjustResize
  a no-op; ComposeBackendRoot now observes the decor-level IME inset
  (ViewCompat) + AdjustNothing and reflows to `AvailableSize`. Composer +
  selector row sit flush above the keyboard; restore on dismiss.
- **Record-dot pulse: DONE** (`24f6ddcf`) — Comet's own animation engine now runs on
  the node backends (ChoreographerTicker / DisplayLinkTicker); the gold's
  infiniteRepeatable alpha pulse is `recordDot.Animate(repeats, autoReverses)`.
- **iOS F3 seed-scroll: DONE** (`01192c99`) — conversation opens at the newest
  message on iOS.
- Remaining engine-level deviations: `AnimatedContent`/`updateTransition`
  facade bindings (nav transitions, panel crossfades), reverseLayout,
  structural-insert re-materialization on reload, `_rowCache` LRU.


## Known visual bug (found 2026-07-09, PRE-EXISTING — verified present at 56f5deae)
- **Android: long message bubbles' TEXT overflows the bubble box** (right edge —
  the FormattedText/Text ink wraps at a wider width than the Yoga box it gets,
  clipping at the screen edge; the bubble BACKGROUND is correctly at
  screen−16dp). iOS wraps correctly. Suspect: the Compose text node's
  measure-vs-render width mismatch (MeasureRuns/MeasureWrapped constraint vs
  the rendered composable's width). Repro: Jetchat conversation, John Glenn's
  long messages. Surfaced during the Reply close-out visual inspection —
  smokes never caught it (existence-based asserts).
