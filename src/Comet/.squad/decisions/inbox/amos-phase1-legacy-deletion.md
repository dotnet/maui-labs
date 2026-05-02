# Phase 1 — Legacy style/theme deletion + singleton BuiltInStyles

**Author:** Amos (Controls & API Dev)
**Date:** 2026-04-08
**Status:** Implemented — follows Holden's `feature/comet@932129e` Theme-as-record work
**Spec:** `docs/architecture/STYLE_THEME_SPEC.md` §1.2, §3.6, §11.2–§11.5, §15.2

## What I deleted (Deliverable 1)

Per David's directive ("we don't care about backwards compat, so get rid of anything
that could be confusing to developers or to AI LLM agents") — no shims, no
`[Obsolete]`, just gone.

### Framework (src/Comet/src/Comet/)

| File | LOC |
|------|-----|
| `Styles/Style.cs` | 361 |
| `Styles/ControlStyle.cs` | 90 |
| `Styles/ButtonStyle.cs` | 20 |
| `Styles/NavbarStyle.cs` | 13 |
| `Styles/SliderStyle.cs` | 12 |
| `Styles/ProgressBarStyle.cs` | 11 |
| `Styles/TextStyle.cs` | 16 |
| `Styles/ViewStyle.cs` | 18 |
| `Styles/DefaultThemeStyles.cs` | 109 |
| `Styles/ResourceDictionary.cs` | 74 |
| `Styles/AppThemeBinding.cs` | 50 |
| `Styles/StyleAwareValue.cs` | 78 |
| `Styles/StyleType.cs` | 11 |
| `Styles/IThemeable.cs` | 15 |
| `Styles/VisualStateManager.cs` | 82 |
| `Styles/ThemeColors.cs` | 184 |
| `Interactivity/VisualStateManager.cs` | 110 |
| `Styles/Material/MaterialStyle.cs` | ~110 |
| `Styles/Material/Extensions.cs` | 25 |
| `Styles/Material/ColorPalette.cs` | (cascading — only MaterialStyle consumed it) |

**Total framework deletions: ~1,400 LOC.**

The `Material/` subdirectory was deleted wholesale because `MaterialStyle : Style`
is now a dangling inheritance and the only consumers were `MaterialStyle.*StyleId`
constants in `Extensions.cs` and sample apps that will be migrated separately.

### Tests (src/Comet/tests/Comet.Tests/)

| File | LOC | Reason |
|------|-----|--------|
| `ThemeTests/ThemeBaseTests.cs` | 399 | Tests `Theme.Current`, `Theme.Apply`, `ThemeChanged`, `IThemeable` — all deleted |
| `ThemeTests/ThemeColorsTests.cs` | 333 | Tests `ThemeColors.LightScheme` etc. — type deleted |
| `ThemeTests/ControlStyleTests.cs` | 481 | Tests `ControlStyle<T>` — type deleted |
| `ThemeTests/ThemeIntegrationTests.cs` | 381 | Legacy theme-change notification |
| `Styles/ControlStyleTests.cs` | 416 | Same |
| `Styles/ThemeTests.cs` | 346 | Same |
| `StylingTests.cs` | 161 | Tests `ResourceDictionary`, `VisualStateManager`, `Setter` — all deleted |
| `ThemeTests.cs` | 159 | Legacy theme API |

**Total test deletions: ~2,676 LOC.** None of these tests had intent that maps to
the new Theme record / `IControlStyle<T, TConfig>` API — they're end-of-life, not
redirectable. New tests against the new API belong in a follow-up PR scoped by
Bobbie alongside the template build gate.

## Call-site migrations I made

All within framework `src/Comet/src/Comet/` or tests (minimum required to get
`Comet.csproj` and `Comet.Tests.csproj` to compile):

| File | Change |
|------|--------|
| `AppHostBuilderExtensions.cs::UseCometHandlers` | Removed `var style = new Styles.Style(); style.Apply();` (Holden flagged). |
| `AppHostBuilderExtensions.cs::ApplyModifierAsInstanceDefaults` | Fixed a CS0157 ("return from finally") bug in Holden's new code — restructured so `StopMonitoringChanges` happens in `finally` but the replay loop runs after. This was invisible behind `ControlStyle.cs` errors previously. |
| `Controls/AbstractLayout.cs::GetDefaultPadding` | Was reading `Style.LayoutPadding` environment key; now returns `new Thickness(6)` directly (matches the legacy default — nobody was writing a different value now that `style.Apply()` is gone). Follow-up: wire to spacing tokens. |
| `Controls/View.cs` | Removed `public ResourceDictionary Resources` property and `VisualStateManager.ClearVisualStateGroups(this)` call in `Dispose`. |
| `Helpers/ViewExtensions.cs` | Removed `StaticResource<T>`, `DynamicResource`, `WithVisualStateGroups`, `GoToState`. |
| `Styles/StyleExtensions.cs` | Removed `ApplyStyle(Style)` and `StyleApply(Style<T>)` extensions. `StyleAsH1`..`StyleAsOverline` kept — they only touch `StyleId`. |
| `Behaviors/Trigger.cs` | Removed `List<Setter> Setters` from `DataTrigger` (Setter was in deleted VisualStateManager). Functional setter path via `DataTrigger<T>.TypedSetter` / `UndoSetter` is the supported API. |
| `Compatibility/MauiCompatibility.cs` | Removed `List<Setter> Setters` from `Trigger` and `MultiTrigger` for same reason. |
| `Interactivity/AdaptiveTrigger.cs` | Removed Setter-based `Apply` body; typed `AdaptiveTrigger<T>` is the canonical path. |
| `Comet.SourceGenerator/CometViewSourceGenerator.cs` | Removed emission of `{ClassName}StyleBuilder.g.cs` (generated code referenced deleted `ControlStyle<T>`). Template reduced to a comment stub; caller `context.AddSource(...)` removed. |
| `tests/Comet.Tests/UI.cs` | Removed the legacy `new Styles.Style(); style.Apply();` test setup. |

## BuiltInStyles — approach chosen (Deliverable 2)

**Chosen:** Option 2-adjacent — **per-state singleton appearances that resolve
theme tokens at `Apply(view)` time.**

I deliberately did _not_ bake `Binding<Color>` into the appearance (Option 1)
because spec §15.2 explicitly states: *"control styles resolve tokens eagerly
(not via `Binding<T>`) because they re-evaluate on every state change."* Mixing
binding-based resolution with `IControlStyle.Resolve()` would collide with the
state-change re-resolve loop Holden has queued as a follow-up (see his Phase 1
note's "Follow-ups" section).

### Shape of each built-in style

```csharp
public sealed class FilledButtonStyle : IControlStyle<Button, ButtonConfiguration>
{
    public ViewModifier Resolve(ButtonConfiguration config)
    {
        if (!config.IsEnabled) return Disabled.Instance;
        if (config.IsPressed)  return Pressed.Instance;
        if (config.IsHovered)  return Hovered.Instance;
        return Default.Instance;
    }

    static readonly RoundedRectangle s_shape = new RoundedRectangle(20);
    static readonly Thickness s_padding = new Thickness(24, 12);

    sealed class Default : ViewModifier<Button>
    {
        public static readonly Default Instance = new Default();
        public override Button Apply(Button view) => view
            .Background(ColorTokens.Primary.Resolve(view))
            .Color(ColorTokens.OnPrimary.Resolve(view))
            .Opacity(1.0)
            .ClipShape(s_shape)
            .Padding(s_padding);
    }
    // Pressed / Hovered / Disabled: same shape, different token / alpha choices.
}
```

Applied consistently across `FilledButtonStyle`, `OutlinedButtonStyle`,
`TextButtonStyle`, `ElevatedButtonStyle` — all four variants.

### Allocation profile per state change

| Kind | Before | After |
|------|--------|-------|
| `FilledButtonAppearance` / sibling classes | 1 per `Resolve()` | 0 (singleton) |
| `RoundedRectangle(20)` | 1 per `Apply()` | 0 (static readonly `s_shape`) |
| `Thickness(24, 12)` | 1 per `Apply()` (stack — struct) | 0 (static readonly `s_padding`, struct copy at use) |
| `SolidPaint` wrapping theme color | 1 per `Apply()` | 0 — `.Background(Color)` overload bypasses Paint allocation (confirmed: `ColorExtensions.Background<T>(this T, Color, bool)` exists at line 74) |
| `Color` result of `ColorTokens.X.Resolve(view)` | token lookup per property | same (unavoidable; spec §11.3) |

**Net:** zero heap allocation per state change for all four styles. The only
remaining allocation is MAUI's `Shadow` for the `Elevated` variant, which is
required by the MAUI shadow API shape. Disabled-state colors (`Colors.Grey`
variants) are cached as `static readonly Color` fields on the Disabled singletons.

### Why not `Binding<Color>` in the appearance

Spec §15.2 is explicit: per-control styles re-evaluate on state change. Wrapping
theme-aware colors in `Binding<Color>` would mean the binding defers resolution
to render time — but `Apply()` is the only time this modifier touches the view,
and `Apply()` is called _per state change_, not per frame. A binding is the
wrong unit here; we already have the view instance in `Apply(Button view)`, so
resolving eagerly via `ColorTokens.X.Resolve(view)` at Apply time is exactly the
hot path §11.2 describes.

## Build & verification

- **Framework:** `dotnet build src/Comet/Comet.csproj -c Debug` from `src/Comet/`
  → **0 errors, 0 new warnings introduced** across net11.0-android / -ios /
  -maccatalyst. Warning count unchanged at 3,589 (all pre-existing; mostly
  CS8625 nullable-literal and CS1591 doc-comment warnings from the broader
  codebase).
- **Tests project:** legacy tests deleted. I did not run `dotnet test` — that's
  Bobbie's gate.

## Things Bobbie should pay attention to

1. **Samples are broken.** All nine sample apps still reference deleted legacy
   API (`Theme.Current`, `ControlStyle<T>`, `ThemeColors`, `AppTheme`, etc.).
   I intentionally did not touch them — the task scoped them as "migrate or
   delete" but flagged that per-sample judgment was needed. The canonical
   sample per David is `templates/single-project/`; all `sample/*` apps need
   either (a) migration to the new Theme record + `IControlStyle` API, or (b)
   deletion. Recommend a follow-up spec'd as "Phase 1.5 — sample migration"
   with Elena and Ralph so it's done deliberately, not under time pressure.
   The list of affected sample files is preserved in the git history from this
   branch.
2. **`AppHostBuilderExtensions::ApplyModifierAsInstanceDefaults`** — I fixed a
   CS0157 latent bug in Holden's new code. The semantics I preserved: the
   original body `StopMonitoringChanges → null-check → replay loop` now runs
   outside `finally` after the try/finally completes. Behaviorally identical
   _except_ an exception in `modifier.Apply(view)` no longer swallows the
   replay (correctly). If there's a reason Holden wanted replay to still run on
   exception, shout — but standard practice is what I landed on.
3. **Reactive state → style re-resolution still not wired.** Holden flagged
   this as a Phase 1 follow-up. Today `Resolve()` runs for the state that was
   current when the handler's mapper fired; nothing re-invokes `Resolve()` when
   `IsPressed`/`IsHovered` flip at runtime. The singleton pattern is compatible
   with that future wiring — when `OnControlStateChanged` fires and re-runs
   `ResolveCurrentStyle`, it'll just pick the right singleton. No further
   BuiltInStyles work needed.
4. **`AbstractLayout.GetDefaultPadding` is now a literal `new Thickness(6)`.**
   Previously it read the environment key `"LayoutPadding"` — that key is no
   longer written anywhere (its sole writer was the deleted `Style.Apply()`).
   When layout spacing tokens arrive in a later phase, this should read from
   `SpacingTokens` instead. Filed under the same follow-up bucket as token
   wiring for the handlers.
5. **Source generator stub.** I left `styleBuilderMustacheTemplate` as a
   comment-only constant in `CometViewSourceGenerator.cs` rather than deleting
   the field outright, because `styleBuilderPropertyMustache` is still
   referenced from `GetModelData` input (the `StylePropertyFunc` delegate). The
   delegate renders an empty string template so the emitted code is a no-op;
   deleting the whole chain would have widened this PR unnecessarily. Clean
   follow-up: remove the dead `StyleProperties` / `StylePropertyFunc` input
   keys and the two stub constants.

## Spec alignment — self-check

- §1.2 "one way to do each thing" — all of `Style`, `Style<T>`, `ControlStyle<T>`,
  `ButtonStyle`/`NavbarStyle`/etc. deleted. Only `IControlStyle<TControl, TConfig>`
  remains as the per-control styling protocol. ✅
- §3.6 "Static Modifier Instances" — every built-in button appearance is a
  `public static readonly Instance` singleton. ✅
- §11.4 "Modifier instances can be cached as `static readonly` fields" — done. ✅
- §11.5 memory table — "ViewModifier (cached): 1 object, App lifetime" — matches. ✅
- §15.2 SwiftUI comparison — the Comet code block in the spec constructs a new
  appearance per `Resolve()`. My implementation is strictly better (singleton)
  while preserving eager token resolution. Worth updating the spec example; I'll
  flag it to Holden.

---

Commit: see branch `feature/comet` — commit message references this decision note.
