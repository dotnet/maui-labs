# Spec: Baseline alignment in the Comet layout engine

Status: **Implemented on Android (Compose); iOS pending.** Created 2026-06-14.

## Implementation notes (what shipped)
- Public API: **`view.AlignBaseline()`** (env `Layout.BaselineAlign`), mirroring Compose's
  `Modifier.alignByBaseline()`. Per-child opt-in on a row.
- Engine: `YogaMeasureBridge.ApplyStyle` sets `AlignSelf = FlexAlign.Baseline` for opted-in
  children of a row; `CometBackendLayoutEngine.Build` installs `node.BaselineFunction →
  leaf.Node.MeasureBaseline(...)` (falling back to node height when null).
- Node contract: `ICometBackendNode.MeasureBaseline(width, height)` (default `null`);
  `ComposeTextNode` overrides it via `TextMeasure.FirstBaselineDp` — the analytical first-baseline
  inside the pinned `LineHeightSp`, using the **proportional** leading split (Compose's default).
- **Visibility gap (§4.1) was a non-issue:** `Comet.Layout.Yoga` already has
  `[InternalsVisibleTo("Comet")]`, so `BaselineFunction`/`YogaBaselineFunc` are reachable — no port
  change needed.
- Jetchat `AuthorNameTimestamp` now uses `.AlignBaseline()`; the empirical `0.5dp` nudge is gone.
- Verified: host unit test `BaselineAlignedRow_LinesUpChildBaselines` (engine math, generalizes to
  any baseline values, 45/45 backend tests pass) + on-device Pixel 5 measurement (author/timestamp
  baselines within 1px — the screenshot measurement floor).

### Still open
- **iOS**: the SwiftUI text node still returns `null` from `MeasureBaseline` (falls back to height
  ≈ bottom). Implement via `UIFont` metrics — see §5. Not validated yet (Android-first per current
  direction).
- **`FormattedText`** (rich-text bubbles) doesn't override `MeasureBaseline` yet — only plain `Text`.
- The proportional leading model matches Compose to ≤1px here; if a future font exposes a mismatch,
  pin an explicit `LineHeightStyle` (§5) to make it closed-form.

---

## Original spec follows.

## 1. Goal

Let a row of text-bearing views align its children on a shared **text baseline**, the way
Jetpack Compose's `Row(verticalAlignment = …)` + `Modifier.alignByBaseline()` and SwiftUI's
`HStack(alignment: .firstTextBaseline)` do.

The motivating case is the Jetchat conversation header — an author name and a timestamp at
different font sizes that must sit on the same line:

```
John Glenn  8:12 PM
‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾   ← one shared baseline
```

The gold standard (`android/compose-samples` Jetchat `AuthorNameTimestamp`) uses:

```kotlin
Row {
    Text(author,    style = titleMedium, modifier = Modifier.alignBy(LastBaseline))
    Spacer(Modifier.width(8.dp))
    Text(timestamp, style = bodySmall,  modifier = Modifier.alignBy(LastBaseline))
}
```

### Why the current result is wrong

The Comet layout engine has no baseline alignment, so today the sample bottom-aligns the two
texts (`VerticalLayoutAlignment.End`) plus an **empirical `0.5dp` bottom-margin nudge** on the
timestamp (`JetchatConversation.AuthorNameTimestamp`). Measured on a Pixel 5 this lands the two
baselines within ~1px — but it is a hand-tuned constant calibrated to the specific 16sp/12sp pair.
It does not generalize to other font-size pairs and is exactly the class of "≈2px off" error we
want to eliminate structurally.

**Definition of done:** a developer can opt two (or more) text views in a row into baseline
alignment with no magic numbers, and the rendered baselines coincide on both the Compose (Android)
and SwiftUI (iOS) backends, at any density and for any font-size pair.

## 2. Background — how baseline alignment works

- The **baseline** of a line of text is the line the glyphs "sit" on (descenders hang below).
- Flexbox baseline alignment: in a row, each participating child reports its **first-baseline
  offset** (distance from the child's top edge to its baseline). The engine finds the maximum such
  offset across the row and shifts every participating child down so all baselines land on that
  common line. Bigger text naturally has a larger ascent, so it pushes the shared baseline down and
  the smaller text rides up to meet it.
- This is **not** the same as bottom/top/center alignment of the boxes — those align box edges or
  box centers, which only coincides with the baseline by accident.

## 3. Prior art

- **Yoga** (Meta's flexbox engine, the basis of React Native and of our C# port) fully supports
  `align-items: baseline` and per-child `align-self: baseline`, plus a per-node **baseline
  function** registered via `YGNodeSetBaselineFunc`. Docs:
  https://www.yogalayout.dev/docs/styling/align-items-self
- **React Native** exposes `alignItems: 'baseline'`; it works because RN's native **Text** shadow
  node installs a baseline function that returns the text's first baseline from its measurement.
  This is the canonical pattern we should mirror. See the Yoga 3.0 layout-conformance notes:
  https://blog.logrocket.com/react-native-layout-management-yoga-3/
- **Jetpack Compose**: `Modifier.alignByBaseline()` / `alignBy(FirstBaseline|LastBaseline)` on
  `Row` children; the measured `Placeable` exposes `FirstBaseline`/`LastBaseline` alignment lines.
- **SwiftUI**: `HStack(alignment: .firstTextBaseline)` and `.alignmentGuide(.firstTextBaseline)`.

Takeaway: every comparable system implements this as **"the leaf reports its baseline; the
container aligns to it."** Our engine is already built for exactly this.

## 4. Current state of *this* codebase (what already exists)

Good news: the vendored C# Yoga port (`src/Comet/src/Comet.Layout.Yoga/`) **already contains the
full baseline machinery** — it was ported from Yoga's `Baseline.cpp`/`Align.cpp`, not stripped:

- `FlexEnums.cs` → `FlexAlign.Baseline = 5`.
- `YogaNode.cs` → `YogaBaselineFunc` delegate, `BaselineFunction` property, `HasBaselineFunc`,
  `IsReferenceBaseline`.
- `AlgorithmUtils.cs` → `BaselineHelper.CalculateBaseline(node)` (recursive: uses the node's
  baseline func, else recurses into the first baseline child, else **falls back to the node's full
  height**) and `BaselineHelper.IsBaselineLayout(node)`.
- `YogaAlgorithm.cs` → the cross-axis positioning honors baseline layout (≈ lines 628, 700–780,
  1733–1795): it computes each child's ascent via `CalculateBaseline` and offsets children so
  baselines line up.

So the **engine needs no algorithm work.** The reason bottom-align is ~2px low today is precisely
the fallback above: a text leaf with **no baseline function** reports its baseline as its full
height (its bottom edge), so "baseline" degenerates to "bottom of the line box," which sits a
descent-plus-leading below the real baseline.

### The gaps (all at the Comet ↔ Yoga integration seam)

1. **Visibility.** `YogaBaselineFunc` is `internal` and `IsReferenceBaseline` is `internal`, while
   `BaselineFunction` is a `public` property *of an internal type* — so `Comet.csproj` (a different
   assembly) cannot construct/set a baseline function today. Need to make the delegate public (or
   expose a `public` setter taking `Func<…>`/a px value), or add `[InternalsVisibleTo]`.
2. **No baseline value flows from the backend nodes.** `ICometBackendNode` exposes only
   `Size Measure(double, double)` (`src/Comet/Backend/ICometBackendNode.cs:46`). There is no way for
   a text node to report its first-baseline offset.
3. **The layout engine never sets a baseline function.** `CometBackendLayoutEngine.Build`
   (`src/Comet/Backend/CometBackendLayoutEngine.cs`, leaf branch ~line 103) installs a
   `MeasureFunction` but no `BaselineFunction`.
4. **No public API to request baseline alignment.** `LayoutAlignment` is
   `Microsoft.Maui.Primitives.LayoutAlignment` (Start/Center/End/Fill) — a MAUI enum we cannot
   extend with `Baseline`. We need a separate opt-in (see §6).
5. **`YogaMeasureBridge.ApplyStyle`** (`src/Comet/Layout/YogaMeasureBridge.cs`) maps
   `LayoutAlignment → AlignSelf`; it has no path to emit `FlexAlign.Baseline`.

## 5. The core challenge: measured baseline must equal *rendered* baseline

The baseline function returns a number used purely for layout. If that number differs from where the
backend actually *draws* the baseline, the visual result is wrong by the difference — reintroducing
the very ~2px error. So the hard part is **not** the flexbox math; it is computing a baseline value
that exactly matches each backend's text rendering.

### Compose (Android)
`ComposeTextNode` pins `text.LineHeight = LineHeightSp(sp)` where `LineHeightSp = round(sp * 1.3)`
(`ComposeLeafNodes.cs`). Within that line box, the baseline position depends on how Compose
distributes the extra leading (`LineHeightStyle.Alignment`/`Trim`), which is **not** simply
"`-ascent` from the top." Measurement already uses `android.text.StaticLayout` / `Paint.FontMetrics`
(`TextMeasure`), so the metrics are in hand, but the leading distribution must be matched.

Recommended approach: **make the distribution deterministic.** Set an explicit
`LineHeightStyle(alignment = …, trim = LineHeightStyle.Trim.None)` on the rendered `Text`, then the
first-baseline offset is a closed-form function of `FontMetrics` + `LineHeightSp` that the baseline
function can reproduce exactly. Add `TextMeasure.MeasureBaseline(text, sp, width, typeface)`
returning the first-baseline offset in **dp** (divide px by `ComposeNode.Density`).
Cross-check with `StaticLayout.GetLineBaseline(0)` and, ideally, assert against the composed
`Text`'s reported `FirstBaseline` alignment line in an instrumented test.

### SwiftUI (iOS)
Two options:
- **(A) Compute from `UIFont` metrics** (mirrors the Compose path): first baseline ≈ `font.ascender`
  plus the top-leading the shim applies; return it from `measureNode`. Keeps the absolute-positioning
  model the engine already uses.
- **(B) Defer to SwiftUI.** Because SwiftUI has true first-baseline support, a row that opts into
  baseline alignment could be rendered as a real `HStack(alignment: .firstTextBaseline)` instead of
  the engine's absolute placement. Cleaner typographically but it carves a hole in the
  "engine owns all layout" model and only works when the whole row is SwiftUI-native.

Option (A) is recommended for consistency with the Android path and the engine's design; (B) is a
possible future optimization.

## 6. Proposed design

### 6.1 Public API (developer-facing)
`LayoutAlignment` can't carry `Baseline`, so add a dedicated opt-in. Recommended: a per-child
modifier mirroring Compose's `Modifier.alignByBaseline()`:

```csharp
new HStack {
    new Text(author).TitleMedium().AlignBaseline(),
    new Text(time).BodySmall().AlignBaseline(),
}
```

`AlignBaseline()` sets an env flag (e.g. `EnvironmentKeys.Layout.BaselineAlign`) consumed by the
engine, exactly like the existing `.AsSurface()`/`.Borderless()` opt-ins. (Alternative: an
`HStack`-level `.BaselineAligned()` that sets `AlignItems=Baseline` for all children — less precise
but matches the `Row(verticalAlignment=…)` shape. The per-child modifier is preferred; it is what the
gold standard uses and avoids forcing baseline on non-text children like icons.)

### 6.2 Backend-node contract
Extend the node protocol so a leaf can report its baseline:

```csharp
// ICometBackendNode
double? MeasureBaseline(double width, double height); // first-baseline offset from the top, in Dp;
                                                       // null = no text baseline (fall back to height)
```

Default implementation returns `null`. `ComposeTextNode` and the SwiftUI text node override it
(§5). `FormattedText` returns its first line's baseline.

### 6.3 Engine wiring
- `CometBackendLayoutEngine.Build` leaf branch: if the view opted into baseline align, set
  `node.BaselineFunction = (n, w, h) => (float)(leaf.Node?.MeasureBaseline(w, h) ?? h);`.
- `YogaMeasureBridge.ApplyStyle` (and/or the engine's per-child setup): when the view has the
  baseline-align flag, set `node.AlignSelf = FlexAlign.Baseline` (overriding the
  cross-axis alignment), and ensure the parent isn't a column (baseline only applies to rows).
- Make `YogaBaselineFunc` public (or expose a `Func<>`-typed setter) in `Comet.Layout.Yoga`.

### 6.4 Retire the empirical nudge
Once landed, delete the `0.5dp` margin + `VerticalLayoutAlignment.End` workaround in
`JetchatConversation.AuthorNameTimestamp` and replace with `.AlignBaseline()` on both texts.

## 7. Touch points (file checklist)
- `src/Comet/src/Comet.Layout.Yoga/YogaNode.cs` — make `YogaBaselineFunc` public / expose setter.
- `src/Comet/src/Comet/Backend/ICometBackendNode.cs` — add `MeasureBaseline` (default `null`).
- `src/Comet/src/Comet/Backend/CometBackendLayoutEngine.cs` — set `BaselineFunction` for baseline leaves.
- `src/Comet/src/Comet/Layout/YogaMeasureBridge.cs` — emit `FlexAlign.Baseline` for opted-in children.
- `src/Comet/src/Comet/Helpers/…` — `AlignBaseline()` extension + `EnvironmentKeys.Layout.BaselineAlign`.
- `src/Comet/src/Comet/Platform/Compose/ComposeLeafNodes.cs` — `TextMeasure.MeasureBaseline` + a
  deterministic `LineHeightStyle`; `ComposeTextNode`/`ComposeFormattedTextNode` override `MeasureBaseline`.
- `src/Comet/src/Comet/Platform/SwiftUI/SwiftUINode.cs` + `Comet.SwiftUI.Shim` — report baseline from
  `measureNode` (UIFont metrics).
- `JetchatConversation.AuthorNameTimestamp` — swap the nudge for `.AlignBaseline()`.

## 8. Risks & open questions
- **Leading distribution match (highest risk).** Getting `MeasureBaseline` to equal the rendered
  baseline within ≤0.5px on Compose hinges on pinning `LineHeightStyle`. Must be verified by
  instrumented measurement, not by eye.
- **Multi-line text.** First vs last baseline semantics (`alignBy(FirstBaseline)` vs `LastBaseline`).
  Jetchat uses `LastBaseline`; for single-line text first==last. Decide whether to expose both.
- **Mixed children.** A row with an icon + text + text: only the texts opt in; the icon keeps its
  own (top/center) alignment. The per-child API handles this; an `AlignItems=Baseline` container
  would not.
- **`LayoutContent` rows.** The LazyColumn/list row pass (`LayoutContent`) must preserve baseline
  funcs — verify baseline survives the width-pinned/height-wrapped pass.
- **Performance.** Baseline layout adds a second measurement pass for baseline rows in Yoga; rows
  are tiny so this is negligible, but note it.

## 9. Test plan
- **Host/unit (`tests/Comet.Tests/Backend/…`):** a fake backend node returning a known
  `MeasureBaseline`; assert that two children of different heights are positioned so
  `top_i + baseline_i` is equal across the row (pure engine math, no device).
- **Instrumented (Android):** render the 16sp/12sp pair, screenshot, measure the two baselines
  (the harness already does this — see the pixel-baseline measurement used to find the original 2px
  error); assert ≤0.5px. Repeat for a 22sp/14sp pair to prove it generalizes.
- **iOS:** same screenshot-measure on the simulator.

## 10. Rollout
1. Engine plumbing + host unit tests (no rendering) — make `FlexAlign.Baseline` reachable end-to-end
   with a fake baseline func.
2. Compose `MeasureBaseline` + deterministic line height; instrumented test; convert Jetchat; delete
   the nudge.
3. SwiftUI `MeasureBaseline`; iOS verification.
4. Doc the `AlignBaseline()` API in `controls.md`/`styling.md`.

## Appendix: the original failing measurement (for regression context)
Pixel 5, Jetchat header, bottom-aligned (pre-nudge): author baseline y=519, timestamp y=521 →
timestamp **2px low** (it has less descender+leading below its baseline than the larger name). The
`0.5dp` nudge brought it to within 1px but is font-size-pair-specific. This spec removes the need
for any nudge.
