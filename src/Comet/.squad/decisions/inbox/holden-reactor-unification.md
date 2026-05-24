# Reactor Unification — Research + Proposed Path

**Author:** Holden (Lead Architect) — drafted via Copilot research session
**Date:** 2026-04-17
**Status:** Decided 2026-05-02 — Yoga adopted; unification rejected; `[Body]`/`View` retained

---

## Verdict (David, 2026-05-02)

1. **Yoga adoption — ACCEPTED and already shipped.** `src/Comet/src/Comet.Layout.Yoga/` contains the ported algorithm (`YogaAlgorithm.cs`, `YogaNode.cs`, `YogaStyle.cs`, `AlgorithmUtils.cs`, enums/values/config) and is referenced from `Comet`. Item #3 of the proposal is complete.
2. **Reactor unification — REJECTED.** We will not extract `Reactor.Core` or ship `Reactor.Maui`. Comet stays Comet.
3. **`[Body]` / `View` API — RETAINED.** The fine-grained-reactive authoring surface is not on the table. Do not propose dropping it again without new evidence.
4. MauiReactor remains out of scope (unchanged from prior directive).
5. Live Preview learnings stand on their own merits — evaluate separately if/when relevant; not blocked by this decision.

**No further action required on this proposal.** Original analysis preserved below for the record.
**Subject repo:** [microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor) (commit `7c90d29`, April 2026, "Experimental")
**Related research:** `~/.copilot/session-state/7adf8610-2868-40db-ae4f-c43acd0af58f/research/take-a-look-at-this-new-experiment-from-the-window.md`

---

## TL;DR

1. **Unify Comet with Reactor** by extracting `Reactor.Core` (platform-neutral) and shipping a `Reactor.Maui` backend that **replaces Comet's current authoring surface**. Drop `[Body]`/`View` public API. Keep `Reactive<T>` as a storage primitive only (not as a render-bypass).
2. **Accept the loss of fine-grained reactivity** in exchange for one reconciler, one DSL, one hot-reload path across WinUI 3 + all MAUI platforms. No customers means no migration cost.
3. **Steal Yoga immediately**, independent of (1). Reactor's C#-port Yoga (~170 KB, 590 fixtures) is drop-in for Comet's flex-layout problems.
4. **MauiReactor is out of scope** per David's directive, unless core extraction yields a free adoption path for Adolfo.
5. **Live Preview is real, not aspirational** — `PreviewCaptureServer.cs` + `vscode-reactor` VS Code extension both ship. WinUI-only today; porting to Comet reuses DevFlow's cross-platform screenshot pipeline.

---

## Questions answered

### Q1 — Should we adopt Yoga for Comet's layout engine?

**Yes. High confidence. Do it independently of the full unification.**

- Reactor's `src/Reactor/Yoga/` is a pure-C# Yoga port with 590 fixtures passing.
- Core files: `YogaAlgorithm.cs` (114 KB), `YogaNode.cs` (22.7 KB), `YogaStyle.cs` (16.8 KB), `AlgorithmUtils.cs` (20 KB), plus small enum/value/config files. Total ~170 KB, no external deps.
- `FlexPanel.cs` (25.5 KB) is WinUI-specific and does NOT port — we write our own `FlexLayout : Microsoft.Maui.Controls.Layout` that delegates measure/arrange to `YogaNode`.
- Same algorithm already used by React Native, Litho, Expo, and MAUI's own `Microsoft.Maui.Controls.FlexLayout` (which P/Invokes upstream C++ Yoga). The alignment/measuring bugs we're hitting in hand-written Comet layout are ones Yoga has long solved.
- Yoga is **flex-only**. Keep Grid / AbsoluteLayout / any custom layouts on separate code paths. Reactor does this same split (Yoga for FlexPanel, WinUI Grid for Grid, Canvas for absolute positioning).
- Licence: Meta's upstream Yoga is MIT. Verify Reactor's port preserves it before copying files in.

**Sizing:** 1 engineer, 1–2 weeks to port + wire up + parity-test with Yoga's fixture suite.

### Q2 — Is Reactor's Live Preview implemented or aspirational?

**Implemented. Shipping code exists on both ends.**

**Server — `src/Reactor/Hosting/PreviewCaptureServer.cs` (12.7 KB):**
- `HttpListener` on free localhost port
- `DispatcherQueueTimer` at 10 fps → Win32 `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` → `System.Drawing.Bitmap` → JPEG → `Interlocked.Exchange` into `_latestFrame`
- Endpoints: `GET /frame` (JPEG), `GET /status`, `POST /focus`, `GET /components`, `POST /preview` (switch component by name)
- CORS restricted to `http://localhost:`, `https://localhost:`, `vscode-webview://`

**Client — `src/vscode-reactor/` VS Code extension:**
- `package.json` v0.1.0, publisher `reactor`, engines `vscode ^1.85.0`
- Commands: `reactor.preview`, `reactor.previewConnect`, `reactor.previewStop`, `reactor.previewFocus`
- `extension.ts` ~21 KB — polls `/frame`, paints webview

**Porting to Comet:** the server logic is ~400 lines of C# with `PrintWindow` + `DispatcherQueue` as the only non-portable pieces. DevFlow in this repo already has composited per-platform screenshot capture (the `maui_screenshot` MCP tool) — wrapping that behind the same 5-endpoint HTTP contract makes Reactor's VS Code extension work for Comet with minimal TS changes.

**Sizing:** 1 engineer, 1 week for the server wrapper + VS Code extension fork. Preview-host MAUI app (renders isolated components) is a follow-up — the existing Comet Go CompanionApp is nearly this already.

---

## Proposed unification path (focused on Comet ↔ Reactor only)

### Target architecture

```
Microsoft.Ui.Reactor.Core                (net10.0, no UI deps)
├── Element records, ElementModifiers, Attached props
├── Reconciler (Mount / Update / ChildReconciler / ElementPool)
├── Component, Component<TProps>, RenderContext, Hooks
├── Yoga layout engine                   ← also ships as Comet's flex layout
├── Commands + StandardCommand
├── Localization (ICU + source generator)
├── Theme tokens (ThemeRef, ColorScheme)
├── Navigation graph (NavigationHandle<TRoute>, UseNavigation)
└── Factories static class (Tier A+B DSL only — no WinUI types)

Microsoft.Ui.Reactor.WinUI               (net8.0-windows, WindowsAppSDK)
└── Host + handlers (as microsoft-ui-reactor is today, unchanged)

Microsoft.Ui.Reactor.Maui                (net10.0-{android,ios,maccatalyst,windows,tizen})
├── Host (builder.UseReactor<T>() extension on MauiAppBuilder)
├── Handlers: ButtonElement → Microsoft.Maui.Controls.Button via BindableProperty
├── Tier B mappings: NavigationView → Shell, ContentDialog → DisplayAlert, …
├── Hot reload via MetadataUpdateHandler (shared with Reactor.Core)
└── DevFlow integration inherited from existing Comet wiring
```

### What this does to Comet's current codebase

| Comet today | Post-unification |
|---|---|
| `src/Comet/src/Comet/` (public `View`, `[Body]`, `Reactive<T>`) | **Public API deleted.** Source files repurposed into Reactor.Maui handler implementations. |
| `src/Comet/src/Comet/Controls/` (Grid, HStack, VStack, CollectionView, Shell…) | Absorbed as handler implementations for corresponding Reactor `Element` records. No longer user-facing. |
| `src/Comet/src/Comet/Reactive.cs` | Moves to `Microsoft.Ui.Reactor.Maui.State`. Retained as storage primitive for shared state. Reactor's existing `UseObservable<T>(T) where T : INotifyPropertyChanged` picks it up with zero new API (Comet's `Reactive<T>` already implements `INotifyPropertyRead`). |
| `src/Go/` (Comet Go dev-server + CompanionApp + Cli) | **Kept.** The socket protocol is unchanged; the compiled shape moves from Comet `View` subclasses to Reactor `Component` subclasses. This is a templating change, not a protocol change. |
| Authoring `[Body] View body() => …` | Authoring `public override Element Render() => …` with Reactor's factory DSL |
| Fine-grained rebind via `Reactive<T>` | Reconciler diffs, controls are pooled, updates are cheap. Fine-grained model is gone. |

### The non-negotiable trade

Reactor's model is **render-and-diff**: `Render()` returns immutable `Element` records, reconciler patches real controls on state change. Comet's model today is **fine-grained reactivity**: reads are tracked, `body()` never re-runs.

We cannot keep both as the rebind mechanism. Picking Reactor means re-rendering on state change. The reconciler + element pooling make this cheap, but it is a genuine loss vs. Svelte/Solid-style fine-grained updates.

**Accept it.** Maintaining two render models across every handler and every hot-reload path is more cost than the perf win is worth, and we have no customers depending on the fine-grained model. `Reactive<T>` survives as a typed observable for cross-component state; `UseState` is the per-component default.

### Phases

1. **Phase 0 — Alignment (before any code).**
   - Fork `microsoft/microsoft-ui-reactor` into maui-labs (or sibling incubation repo). Enables breaking core changes without upstream gating.
   - Agree with the Reactor team that we will upstream the `Reactor.Core` extraction if it works. Two cores drifting kills the entire premise.
   - Freeze the ~50 Tier A+B factories from `Dsl.cs` that make up the shared DSL. Everything else is per-backend.

2. **Phase 1 — Extract Reactor.Core (1 engineer, ~2 weeks).**
   - Copy `src/Reactor/Core/`, `Hooks/`, `Yoga/`, `Localization/`, `Commands/`, `Navigation/` into new `Microsoft.Ui.Reactor.Core` project at `net10.0`.
   - Replace every `Microsoft.UI.Xaml.*` / `Windows.UI.*` / `Microsoft.UI.Composition.*` reference with neutral types:
     - `Windows.UI.Color` → `Microsoft.Maui.Graphics.Color` (or own `RgbaColor` record)
     - `Windows.UI.Text.FontWeight` → `FontWeight(int Value)` wrapper
     - `Microsoft.UI.Xaml.Media.Brush` → abstract `BrushDescriptor` record (Solid / Linear / Radial)
     - Composition animation payloads → neutral `AnimationDescriptor` records (WinUI backend translates to Composition; MAUI backend translates to `ViewExtensions.FadeTo`/etc. or no-op)
     - `Microsoft.UI.Xaml.Controls.Orientation` → shared `enum Orientation`
   - `Element.cs`, `Reconciler.*`, `Component.cs`, `RenderContext.cs`, hooks remain mechanically identical — already UI-agnostic internally.
   - **Gate:** existing WinUI sample in Reactor repo must build and run unchanged against the core package.

3. **Phase 2 — Build Reactor.Maui (1 engineer, ~4–6 weeks).**
   - Create `Microsoft.Ui.Reactor.Maui` at all current Comet TFMs (`net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows`, plus Tizen if scoped).
   - Implement `IElementHandler<TElement, BindableObject>` for Tier A factories, reusing Comet's existing view classes as native controls.
   - Tier B: `NavigationViewElement` → Shell; `ContentDialogElement` → `Page.DisplayAlert`; `TeachingTipElement` → `CommunityToolkit.Maui.Toast`; `ExpanderElement` → synthesized collapsing `Border`.
   - Host: `builder.UseReactor<TRootComponent>()` extension on `MauiAppBuilder`.
   - Delete `[Body]` / `View` public API.
   - **Deliverable gate:** single `CounterApp.cs` file compiles + runs on WinUI + iOS + Android + Mac Catalyst.

4. **Phase 3 — State interop (1 engineer, ~1 week).**
   - `Reactive<T>` moves to `Microsoft.Ui.Reactor.Maui.State` (public).
   - Reactor's existing `UseObservable<T>(T) where T : INotifyPropertyChanged` picks it up — no new API.
   - Samples use `UseState` as default; `Reactive<T>` for cross-component shared state (what we'd otherwise do with DI-registered services).

5. **Phase 4 — Hot reload, preview, DevFlow (1 engineer, ~2 weeks).**
   - `MetadataUpdateHandler` is the shared hot-reload mechanism (pure .NET runtime feature, works on MAUI unchanged).
   - Repoint Comet Go dev-server to hot-load Reactor `Component` classes instead of Comet `View` classes.
   - Build cross-platform `PreviewCaptureServer` using DevFlow's existing screenshot pipeline; reuse Reactor's VS Code extension with minimal TS changes.
   - DevFlow agent already walks `BindableObject` trees — add a Reactor-aware element-ID hook so `maui_tree` reports `Element.Key` + component type.

6. **Phase 5 — Yoga adoption into MAUI's own `FlexLayout` (optional, out of scope for unification).**
   - If the port's 590-fixture parity holds, file an issue in dotnet/maui proposing that `Microsoft.Maui.Controls.FlexLayout` swap its P/Invoke Yoga backend for the managed port. Upstream win for the entire MAUI ecosystem.

### Decisions required from David before Phase 1

| Decision | Options | Holden's pick |
|---|---|---|
| Repo layout | (a) Fork into maui-labs; (b) contribute core extraction upstream; (c) consume Reactor as NuGet with MAUI backend separate | **(a)** — iterate fast, merge upstream after Phase 2 proves viability |
| `Reactor.Core` TFM | netstandard2.1 / net9.0 / net10.0 | **net10.0** — matches MAUI TFM, unlocks C# 13 |
| `Reactive<T>` survives? | Keep as storage / keep + opt-in fine-grained mode later / delete | **Keep as storage.** Revisit fine-grained later if needed. |
| Comet public API | Hard break day one / deprecate for a release | **Hard break** — no customers, no carrying cost |
| Navigation | Typed `NavigationHost<TRoute>` maps to Shell under the hood / expose raw Shell as Tier-C element | **Both** — typed default, Shell escape hatch for MAUI-specific idioms |
| Animations | Reduced common subset in core (opacity/translate/scale/rotate); WinUI-advanced (composition, connected) stays WinUI-only | Same |
| Element equality | Record value equality (Reactor) vs. reference equality (Comet today) | **Adopt Reactor's model.** Non-negotiable for the reconciler. |
| Yoga adoption (independent of unification) | Adopt now / wait for unification / not at all | **Adopt now** — highest-ROI single change, unblocks current layout bugs |
| Live Preview port | Port alongside unification / separate / defer | **Alongside Phase 4**, reusing DevFlow's screenshot pipeline |

### Week-one deliverable (gating proof)

Single `CounterApp.cs` that compiles and runs on WinUI 3, iOS, Android, and Mac Catalyst:

```csharp
using Microsoft.Ui.Reactor;
using Microsoft.Ui.Reactor.Core;
using static Microsoft.Ui.Reactor.Factories;

public class CounterApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(
            Heading($"Count: {count}"),
            HStack(8,
                Button("-", () => setCount(count - 1)).IsEnabled(count > 0),
                Button("+", () => setCount(count + 1))
            )
        ).Padding(16);
    }
}
```

Host bootstrap:
- WinUI: `Microsoft.Ui.Reactor.ReactorApp.Run<CounterApp>("Counter");`
- MAUI: `builder.UseMauiApp<CounterReactorApp>().UseReactor<CounterApp>();`

If that ships, the architecture is proven. Everything else is filling in control handlers.

---

## Risks

1. **Reactor API churn.** Its README explicitly says "every line of code is fair game" and the DSL syntax may shift with C# language-team work. Pin to a specific commit SHA for Phase 1; track upstream only when we have a stable MAUI backend.
2. **WinUI team receptivity.** Extraction is fine as our own experiment; *upstreaming* requires buy-in. If they refuse, we ship our own `Reactor.Core` fork. Cost: permanent drift, duplicated bug fixes.
3. **Lost fine-grained reactivity.** Real capability loss. Mitigation: accept it now; revisit opt-in fine-grained mode (subtree-scoped `Reactive<T>` reconciler bypass) if it becomes a perf problem.
4. **Yoga-only flex.** Non-flex layouts (Grid, AbsoluteLayout, custom) need separate code paths. Same split Reactor already uses.
5. **Breaking Comet samples in `.squad/artifacts/phase15-e2e/`.** All 19 sample apps (CometTodoApp, CometBaristaNotes, etc.) rewritten as part of Phase 2 gate. Bobbie owns e2e regression plan.

## Out of scope (explicit)

- MauiReactor compat (per David: not a goal unless trivially free).
- Reinventing MAUI handlers or cross-platform controls. Reactor.Maui *produces* MAUI handlers, does not replace them.
- iOS/Android-specific features beyond what the Reactor DSL expresses (haptics, sensors, background tasks). These stay in platform-specific escape hatches.

## Open questions

- Does Reactor's Yoga port carry Meta's MIT licence as-is? (Verify before copying.)
- Does Reactor's `Reconciler.cs` (~124 KB) have any WinUI dependencies beyond what the `Element` records surface? (Needs inspection during Phase 1.)
- Can we run both a WinUI head (for desktop dev) and MAUI-WinUI head of the same app simultaneously for parity testing? (Likely yes via separate project heads referencing the same `CounterApp.cs`.)
- Hot-reload state-transfer: Reactor relies on `MetadataUpdateHandler` invalidation; MauiReactor has `TypeLoader` state migration. Is Reactor's approach good enough for Comet's current dev loop, or do we need state-migration? (Validate against Comet Go's current hot-reload scenarios.)

---

**Handoff:** David to make the decisions listed above. Once confirmed, this memo promotes out of `decisions/inbox/` and becomes the driving spec for Phase 0 → Phase 4. Yoga adoption (Phase-independent) can start immediately on its own branch.
