# Comet Layout — Yoga Migration

This document summarises the work that replaced Comet's hand-rolled stack/flex
layout managers with Meta's [Yoga](https://www.yogalayout.dev/) engine.

## 1. What changed

Comet's `VStack`, `HStack`, `ZStack` and `FlexLayout` previously used a set of
hand-rolled measurement and arrangement managers. Phase 0–6 replaced those with
a thin adapter on top of [Yoga](https://github.com/facebook/yoga), Meta's
flexbox layout engine. The Yoga port itself is a pure-managed C# translation
(no native dependencies) derived from
[microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor)'s
managed Yoga implementation, MIT-licensed.

Net effect: Comet stacks and `FlexLayout` now match the CSS flexbox spec
exactly — same algorithm, same edge cases, same behaviour as the web — instead
of approximating it.

## 2. Where the engine lives

| Path | Purpose |
|------|---------|
| `src/Comet/src/Comet.Layout.Yoga/` | The Yoga port itself (`YogaNode`, `YogaAlgorithm`, enums, helpers). MIT-licensed; attribution in `THIRD-PARTY-NOTICES.md` next to the project. |
| `src/Comet/src/Comet.Layout.Yoga.Tests/` | 632 fixture tests generated from Meta's upstream Yoga conformance suite (`YogaGenerated/*Test.cs`). All pass — algorithm parity is preserved. |
| `src/Comet/src/Comet/Layout/YogaMeasureBridge.cs` | Adapter that builds a `YogaNode` for a single Comet `View` (forwards `Measure(...)` callbacks; reads `FlexGrow`, `FlexShrink`, `FlexAlignSelf`, `AspectRatio`, `PositionType`, `Gap`, etc. from the env). |
| `src/Comet/src/Comet/Layout/YogaStackLayoutManager.cs` | Shared base for the three stack managers — builds a Yoga root from the current child list and arranges it. |
| `src/Comet/src/Comet/Layout/{V,H,Z}StackLayoutManager.cs` | Subclasses pick direction, default cross-axis alignment, and absolute (Z) vs flow (V/H) positioning. |
| `src/Comet/src/Comet/Layout/FlexLayoutManager.cs` | `FlexLayout` driver — same bridge, but reads container-level `FlexDirection`, `FlexWrap`, `FlexJustify`, `FlexAlignItems`, `FlexAlignContent`. |

## 3. API changes (breaking)

Comet is at experimental / 0.x — there are no shipping NuGet consumers. These
breaks are deliberate and final.

### Removed enums (Comet root namespace)

The following enums have been removed from the root `Comet` namespace:

| Removed | Replacement |
|---------|-------------|
| `Comet.FlexDirection` | `Comet.Layout.Yoga.FlexDirection` |
| `Comet.FlexWrap` | `Comet.Layout.Yoga.FlexWrap` |
| `Comet.FlexJustify` | `Comet.Layout.Yoga.FlexJustify` |
| `Comet.FlexAlignItems` | `Comet.Layout.Yoga.FlexAlign` |
| `Comet.FlexAlignContent` | `Comet.Layout.Yoga.FlexAlign` |
| `Comet.FlexAlignSelf` | `Comet.Layout.Yoga.FlexAlign` |
| — | `Comet.Layout.Yoga.FlexPositionType` (new) |

The three alignment enums (`FlexAlignItems`, `FlexAlignContent`,
`FlexAlignSelf`) have been collapsed into a **single `FlexAlign` enum**. This
matches both the underlying Yoga API and the CSS spec, where `align-items`,
`align-content`, `align-self`, and `justify-content` all draw from the same
set of values (`FlexStart`, `Center`, `FlexEnd`, `Stretch`, `SpaceBetween`,
`SpaceAround`, `SpaceEvenly`, `Baseline`, `Auto`). The *property* still
distinguishes items/content/self via the extension method name — only the
enum type unified.

### New extensions in `Helpers/LayoutExtensions.cs`

Five new first-class layout properties on `View`, all backed by Yoga node
properties:

| Extension | Yoga property | Notes |
|-----------|---------------|-------|
| `AspectRatio(double ratio)` | `AspectRatio` | Cross-axis = main-axis × ratio. |
| `PositionType(FlexPositionType type)` | `PositionType` | `Static`, `Relative`, `Absolute`. |
| `Gap(double gap)` | `Gap(All)` | Equal main + cross spacing between children. |
| `RowGap(double gap)` | `Gap(Row)` | Spacing between rows only. |
| `ColumnGap(double gap)` | `Gap(Column)` | Spacing between columns only. |

The two existing flex extensions are unchanged: `FlexGrow(double)`,
`FlexShrink(double)`, `FlexAlignSelf(FlexAlign)`.

## 4. Behavioural changes

### 4.1 Issue A — `Fill` is no longer the implicit cross-axis alignment

`IView.HorizontalLayoutAlignment` / `VerticalLayoutAlignment` default to
`Fill`. Earlier intermediate builds of the Yoga bridge translated that default
into `AlignSelf=Stretch` on every child, which **overrode the parent's
`AlignItems`** and made `FlexLayout(alignItems: Center)` ineffective unless
the child also opted in via `.FlexAlignSelf(Center)`.

`YogaMeasureBridge` now distinguishes "explicitly set" alignment (via
`.FillHorizontal()`, `.FitHorizontal()`, `.HorizontalLayoutAlignment(...)`,
`.Alignment(...)`, etc.) from the unset default:

- `AlignSelf` is set on the child only when the alignment env was explicitly
  written by the developer or when an explicit `.FlexAlignSelf(...)` was
  applied.
- `FlexGrow=1` is applied on the main axis only when the alignment env is
  explicitly `Fill` — not when the child is a plain `IView` that happens to
  return the `Fill` default.

As a result, `FlexLayout(alignItems: Center)` and `VStack { Text("...") }`
now behave the way the documentation described (parent `AlignItems` governs
unless the child overrides it).

> **Migration**: if your app calls `.FlexAlignSelf(...)` to override the
> parent, that still works exactly as before. If you relied on the
> *implicit* `Fill` of an `IView` to override a centred parent, add
> `.FillHorizontal()` (or `.FlexAlignSelf(Stretch)`) explicitly.

### 4.2 Issue B — Stack children with no alignment now stretch to the parent's cross axis

The five `*InVStackIsFullWidth` tests in `LayoutTests` (`Slider`,
`ProgressBar`, `SecureField`, `TextField`, `Editor`) had been failing on
baseline. They expected a child with no per-child alignment env set to be
stretched to the `VStack`'s full width because the root's `AlignItems` is
`Stretch`.

Before the Issue A fix, `YogaStackLayoutManager.BuildRoot` was pre-resolving
each child's alignment via `view.GetHorizontalLayoutAlignment(Layout)` (which
returns `Start` when the env is unset) and forwarding it to the bridge as an
override — this pinned `AlignSelf=FlexStart` on every unset child. With the
bridge now reading the env directly and treating "unset" as `null`, the
root's `AlignItems=Stretch` reaches the children as expected and the five
tests pass.

## 5. Migration guide

The Yoga move is the only breaking change. For each removed enum, either
fully-qualify or import an alias:

```csharp
// before
using Comet;                                  // pulled FlexDirection etc.

VStack { ... }
    .FlexAlignSelf(Comet.FlexAlignSelf.Center);

// after — fully qualify
VStack { ... }
    .FlexAlignSelf(Comet.Layout.Yoga.FlexAlign.Center);

// or — using alias (least friction)
using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;
using YogaFlexAlign     = Comet.Layout.Yoga.FlexAlign;
using YogaFlexJustify   = Comet.Layout.Yoga.FlexJustify;
using YogaFlexWrap      = Comet.Layout.Yoga.FlexWrap;

VStack { ... }
    .FlexAlignSelf(YogaFlexAlign.Center);
```

One-liner per renamed enum:

| Before | After |
|--------|-------|
| `Comet.FlexDirection.Row` | `Comet.Layout.Yoga.FlexDirection.Row` |
| `Comet.FlexWrap.Wrap` | `Comet.Layout.Yoga.FlexWrap.Wrap` |
| `Comet.FlexJustify.Center` | `Comet.Layout.Yoga.FlexJustify.Center` |
| `Comet.FlexAlignItems.Stretch` | `Comet.Layout.Yoga.FlexAlign.Stretch` |
| `Comet.FlexAlignContent.SpaceBetween` | `Comet.Layout.Yoga.FlexAlign.SpaceBetween` |
| `Comet.FlexAlignSelf.Center` | `Comet.Layout.Yoga.FlexAlign.Center` |

If you previously worked around Issue A by adding `.FlexAlignSelf(Center)`
to every child of a centred layout, those calls are now redundant — keep or
remove them as you prefer; both compile.

If your layout relied on an unset child *not* stretching inside a `VStack` /
`HStack` (i.e. the legacy `Start` default), add an explicit `.FitHorizontal()`
(`HorizontalLayoutAlignment(Start)`) to that child.

## 6. Test results (Phase 6, macOS / .NET 11 preview)

| Suite                              | Total | Passed | Skipped | Failed |
|------------------------------------|------:|-------:|--------:|-------:|
| `Comet.Layout.Yoga.Tests`          |   678 |    632 |      46 |      0 |
| `Comet.Tests` (full suite)         |   858 |    828 |      26 |      4 |

The four remaining `Comet.Tests` failures are pre-existing baseline issues
unrelated to the Yoga port:

1. `ReactiveSchedulerTests.Scheduler_MaxFlushDepth_BreaksInfiniteLoop`
2. `ViewExtensionTests.FlowDirectionDefault` — `IView.FlowDirection` getter
   returns the enum's zero value (`MatchParent`) when no env was set; the
   test expects `LeftToRight`.
3. `TemplateCurrentSurfaceValidationTests.SingleProjectTemplateUsesCurrentComponentSurface`
4. `TemplateCurrentSurfaceValidationTests.SingleProjectTemplateTargetsCurrentMauiAndDropsLegacyDependencies`

These are environment / template / reactive issues that pre-date Phase 6 and
are tracked separately.

## 7. Known limitations

- **Mac Catalyst sample build** — sample projects target
  `SupportedOSPlatformVersion=15.0` while the installed SDK is 17.0; sample
  builds emit a warning and may fail on a clean MAUI 11.0 install. Pre-existing
  and unrelated to the Yoga work.
- **Yoga `--layout` diagnostics** — see §8.

## 8. `--layout` diagnostics — shipped

`maui devflow tree --layout` returns the visual tree as before, plus a
`layout` block on every node whose backing MAUI/Comet layout is driven by a
Yoga manager (Comet's `VStack`, `HStack`, `ZStack`, `FlexLayout`). The block
exposes the cached YogaNode root frame + flex configuration and a per-child
list with computed frame, `flexGrow`, `flexShrink`, `alignSelf`, and
`positionType`.

**Status: shipped.**

### Public surface

Comet ships a small inspection interface that the agent reflects against —
no hard reference between the agent and Comet:

```csharp
namespace Comet.Layout;

public interface IYogaLayoutInspector
{
    YogaLayoutSnapshot? GetLayoutSnapshot();
}

public sealed class YogaLayoutSnapshot
{
    public (float X, float Y, float Width, float Height) Frame { get; init; }
    public string FlexDirection { get; init; }      // "Row", "Column", ...
    public string AlignItems { get; init; }
    public string JustifyContent { get; init; }
    public string AlignContent { get; init; }
    public string FlexWrap { get; init; }
    public IReadOnlyList<YogaLayoutChildSnapshot> Children { get; init; }
}

public sealed class YogaLayoutChildSnapshot
{
    public string ViewTypeName { get; init; }
    public string? AutomationId { get; init; }
    public (float X, float Y, float Width, float Height) Frame { get; init; }
    public float FlexGrow { get; init; }
    public float FlexShrink { get; init; }
    public string AlignSelf { get; init; }
    public string PositionType { get; init; }
}
```

`YogaStackLayoutManager` and `YogaFlexLayoutManager` implement
`IYogaLayoutInspector`. Returns `null` before the first measure pass.

### Wire format

```jsonc
{
  "id": "...", "type": "VStack", "bounds": [...],
  "layout": {
    "frame": { "x": 0, "y": 0, "width": 200, "height": 75 },
    "flexDirection": "Column",
    "alignItems": "Stretch",
    "justifyContent": "FlexStart",
    "alignContent": "Stretch",
    "flexWrap": "NoWrap",
    "children": [
      {
        "viewTypeName": "View",
        "frame": { "x": 0, "y": 0, "width": 200, "height": 20 },
        "flexGrow": 0, "flexShrink": 1,
        "alignSelf": "Auto", "positionType": "Relative"
      }
    ]
  }
}
```

### CLI / MCP / Driver

- `maui devflow tree --layout` — appends `?layout=1` to the agent request and
  renders an indented `layout:` line under each Yoga-driven node in text
  mode, or includes the `layout` block in the JSON projection.
- MCP `maui_tree` gained `layout: bool = false`.
- `IAppDriver.GetTreeAsync(int maxDepth = 0, bool includeLayout = false)` and
  the underlying `AgentClient.GetTreeAsync(maxDepth, window, includeLayout)`.

### Implementation notes

- The agent (`Microsoft.Maui.DevFlow.Agent.Core`) takes **no** compile-time
  reference to Comet. `LayoutInspectorAdapter` lazily resolves
  `Comet.Layout.IYogaLayoutInspector` via `Type.GetType(...)` (with an
  `AppDomain` fallback for assemblies not yet name-resolved) and caches the
  type. When Comet isn't loaded, every call returns `null`.
- `IVisualTreeElement → ILayoutManager` discovery uses reflection on a
  `LayoutManager` property. Both Comet's `AbstractLayout` (public) and
  `Microsoft.Maui.Controls.Layout` (internal) expose one — the reflection
  walk picks up either. The `PropertyInfo` is cached per element type.
- `YogaLayoutSnapshot` exposes Yoga enums as **strings** (`enum.ToString()`)
  to keep the interface decoupled from `Comet.Layout.Yoga` internals; the
  adapter projects them to the wire DTO without any enum mapping.

### Tests

`src/Comet/tests/Comet.Tests/Layout/YogaLayoutInspectorTests.cs` — three
xUnit tests cover snapshot-before-measure (returns `null`), VStack with three
children stacked vertically (verifies `FlexDirection="Column"` and per-child
Y positions), and `FlexLayout` with `FlexGrow` propagation.

## 9. For future migrations

A repo-wide search confirmed there are **no remaining call sites** of the
removed `Comet.{FlexDirection,FlexWrap,FlexJustify,FlexAlignItems,FlexAlignContent,FlexAlignSelf}`
enums outside `src/Comet/` itself (the in-tree usages all import from
`Comet.Layout.Yoga`).

If a downstream consumer is later added (sample, template, or external
NuGet caller), the migration is mechanical:

```bash
# Find old references
grep -rln 'Comet\.\(FlexDirection\|FlexWrap\|FlexJustify\|FlexAlignItems\|FlexAlignContent\|FlexAlignSelf\)' .

# Apply the §5 rename table
```

The two non-`src/Comet/` areas worth re-checking on each refresh:

- `samples/` — currently contains only `DevFlow.Sample`, which doesn't use
  Comet flex enums.
- `playground/` — none today.
- Any future `templates/` or external sample repos pulled in via Dependency
  Flow.
