# Squad Decisions

## Active Decisions

### AI + Speech Services: Singleton Lifetime

**Author:** Bobbie (Backend & Integration)  
**Date:** 2025-07-23  
**Status:** Implemented (Phases 2-3)

**Context:** Task spec requested "register AIAdviceService as IAIAdviceService (scoped)". However, CometBaristaNotes does not use scoped service providers (no Shell, no scoped navigation). All existing service registrations use AddSingleton.

**Decision:** Registered both AIAdviceService and SpeechRecognitionService as Singleton, matching the project convention. AIAdviceService holds session-level state (_localClientDisabled, _azureOpenAIClient cache) that is explicitly designed for singleton lifetime. SpeechRecognitionService holds ISpeechToText subscriptions that should persist.

**Impact:** All squad members working with DI in CometBaristaNotes.

---

### CoffeeTheme: ConditionalWeakTable for App-Specific Tokens

**Author:** Holden (Lead Architect)  
**Date:** 2025-07-24  
**Status:** Implemented

**Decision:** App-specific color tokens (SurfaceElevated, TextPrimary, TextSecondary, TextMuted, Success, Warning, Info) that don't map to Material 3's ColorTokenSet are stored in a `CoffeeThemeData` object attached to each Theme instance via `ConditionalWeakTable<Theme, CoffeeThemeData>`.

**Rationale:**
- Avoids modifying the framework's `Theme` class for app-specific concerns
- ConditionalWeakTable provides weak references — data is GC'd when the Theme is
- Custom `Token<Color>` resolvers call `CoffeeTheme.GetThemeData(theme)` to extract values
- Clean separation: Material 3 tokens resolve from `theme.Colors`, coffee-specific tokens resolve from the attached data bag

**Impact:** All squad members working on BaristaNotes page migrations. When referencing app-specific colors (Success, Warning, TextMuted, etc.), use `CoffeeTokens.X` tokens or `CoffeeColors.X` static constants (bridge class).

---

### BaristaNotes Rewrite Plan — Architectural Review

**Author:** Holden (Lead Architect)  
**Date:** 2025-07-22  
**Status:** Recommendation

**Summary:** Reviewed the 7-phase, 39-todo rewrite plan. The plan is structurally sound and the phasing is correct. Five items need attention before execution begins.

**Key Callouts:**
1. **Theme class name collision** — Rename `CometBaristaNotes.Components.Theme` to `CoffeeColors` or fold into framework `Theme` during Phase 1
2. **Dark palette missing** — Phase 1 blocker; source dark colors from original app or define new palette
3. **Tab swap is trivial** — 15-minute task, not a risk; reframe Phase 3 scope accordingly
4. **Phase 4 scope risk** — 11 pages including 2851-line ShotLoggingPage; split into sub-batches (simple → medium → complex)
5. **FormHelpers refactor is optional** — Keep static factories for phases 1-4; migrate to ViewModifiers as Phase 6 polish

**What's Solid:** Phase 1 foundation, Component<TState> pattern, framework token system covers palette needs

**What's Risky:** ShotLoggingPage complexity, dark mode undefined, tab styling limitations, Theme migration surface

**Impact:** All squad members on BaristaNotes rewrite

---

### Opus 4.6 Directive for BaristaNotes Team

**Date:** 2026-04-03  
**By:** David Ortinau (via Copilot)

All squad agents must use Opus 4.6 for the BaristaNotes rewrite. Maximum effort, 100% focus. This overrides default model selection hierarchy for this project.

---

### 2026-04-03T14:15:23Z: Verification & Dependency Ownership Directive

**By:** David Ortinau (via Copilot)

**What:** 
1. Every task must be verified end-to-end before being marked done. "Render-only ≠ done" means: build must pass, app must launch, UI must be inspected interactively (DevFlow tree + screenshots), navigation must be exercised, and results must be compared against reference material.
2. When a dependency or tool is broken (e.g., DevFlow agent doesn't connect), the agent OWNS that problem. Do not report it as a blocker and move on. Fix the dependency — you have the source code in this repo. Build a custom version, PR a fix, whatever it takes.
3. When reference code exists showing how something works (e.g., how BaristaNotes sources an API key), DO WHAT THE REFERENCE DOES. The roadmap is right there — follow it. Don't punt with "needs API key" when the original app's code shows exactly how to get one.
4. Agents must be resourceful problem-solvers, not helpless reporters. If something is broken, fix it. If something is missing, find it. The instruction set and reference material are comprehensive — use them.

**Why:** User request — overnight work was marked "done" with minimal verification. A stale binary screenshot was accepted as proof. Broken dependencies were noted but not fixed. Reference code was available but not followed. This cannot happen again.

---

### 2026-04-03T14:33:22Z: Skill Trainer After Every Turn

**By:** David Ortinau (via Copilot)

**What:** After each turn, run the skill-trainer on any skills that were used or created during that turn to improve them. The verification-protocol skill should be the first one trained.

**Why:** User request — skills degrade if not continuously validated and improved. Eval-driven iteration catches gaps before they become failures.

### Theming Architecture: Comet's Answer to MauiReactor's ThemeKey

**Author:** Holden (Lead Architect)  
**Date:** 2026-04-04  
**Status:** Proposal  
**Impact:** All squad members; framework and sample code

#### 1. Inventory — What Comet Currently Has

Comet already has a **deep** token-based theming system:

| Component | File | Purpose |
|-----------|------|---------|
| `Token<T>` | `Token.cs` | Strongly-typed design token with `Resolver` delegate — resolves from active `Theme` |
| `ColorTokens` | `ColorTokens.cs` | 30+ Material 3 color tokens (Primary, OnSurface, SurfaceVariant, etc.) |
| `TypographyTokens` | `TypographyTokens.cs` | 15 Material 3 type scale tokens (DisplayLarge → LabelSmall) |
| `ThemeManager` | `ThemeManager.cs` | Global + scoped theme management, `TokenBinding()` for reactive resolution |
| `ViewModifier` | `ViewModifier.cs` | Reusable style bundles, supports composition via `.Then()`, application via `.Modifier()` |

#### 2. Gap Analysis

The perceived gap vs MauiReactor's ThemeKey is **purely adoption**, not capability:
- MauiReactor uses `.ThemeKey()` string lookup on per-control-type `Styles.Themes[]` dictionaries
- Comet's `.Modifier()` takes concrete typed instances (more type-safe, composable)
- **Actual problem:** `CoffeeModifiers` reference hardcoded `CoffeeColors.X` statics instead of `Token<T>` resolvers
- **Result:** current modifiers don't switch with light/dark theme changes

#### 3. Recommended Approach

**Do NOT add `.ThemeKey()` to framework.** Instead:
1. Make `CoffeeModifiers` token-aware (use `TypographyTokens` + `CoffeeTokens` + `ThemeManager.TokenBinding()`)
2. Add semantic modifier names (`Headline`, `SubHeadline`, `SecondaryText`, `Card`, etc.) mirroring MauiReactor vocabulary
3. Create reusable `TypographyModifier` class (applies typography token + color token, theme-aware)
4. Migrate pages from inline styling to `.Modifier(CoffeeModifiers.X)`

**Why not add ThemeKey to framework:**
- Type safety — `CoffeeModifiers.CardTitle` is compile-time; `ThemeKeys.CardTitle` is a string that could be misspelled
- No dispatch table needed — Comet's `ViewModifier.Apply` works on any View (type-agnostic)
- Already works — `.Modifier()` exists; just need to make modifiers theme-aware

#### 4. Concrete Work Items

| # | Task | Scope | Effort |
|---|------|-------|--------|
| 1 | Add `TypographyModifier` class to framework | Framework | 30 min |
| 2 | Rewrite all `CoffeeModifiers` to use `Token<T>` resolvers | App | 1 hr |
| 3 | Add semantic modifier names (Headline, SubHeadline, etc.) | App | 30 min |
| 4 | Migrate pages from inline styling to `.Modifier(CoffeeModifiers.X)` | App | 2-3 hr |

**No breaking framework changes required.** Everything can be done at app level.

---

### Visual Alignment Fixes: Global Styles Over Inline

**Author:** Amos (Controls & API Dev)  
**Date:** 2026-04-04  
**Status:** Implemented

**Context:** Multiple form fields in CometBaristaNotes used inline styling duplicating `CoffeeModifiers.FormField`. Section headers had conflicting FontSize (22pt from modifier + 13pt manual override).

**Decisions:**
1. Form entry helpers (`MakeFormEntry`, `MakeFormPicker`, `MakeFormEntryWithLimit`) now use `.Modifier(CoffeeModifiers.FormField)` instead of inline border/background/corner radius
2. `MakeSectionHeader` reverted to manual styling (removed conflicting modifier)
3. All page root views call `.IgnoreSafeArea()` for edge-to-edge background
4. VStack spacing changed to SpacingM (16px) in main sections to match original BaristaNotes
5. Per-section compensating margins removed (VStack spacing now provides adequate spacing)

**Impact:** All squad members editing form fields — use `CoffeeModifiers.FormField` for pill-shaped containers.

---

### DevFlow Agent: One Registration Per Sample

**Author:** Amos (Controls & API Dev)  
**Date:** 2026-04-03  
**Status:** Implemented

**Context:** CometBaristaNotes was stuck on iOS splash screen. Root cause: uncommitted local changes added duplicate DevFlow agent (local `Microsoft.Maui.DevFlow.Agent` + NuGet `Redth.MauiDevFlow.Agent` from `sample/Directory.Build.targets`). Two competing `AddMauiDevFlowAgent()` calls each blocked for 5 seconds during startup.

**Decision:** Never add a second DevFlow agent. `sample/Directory.Build.targets` already provides `Redth.MauiDevFlow.Agent` and `EnableSampleRuntimeDebugging()`. If you need the local DevFlow project, exclude the NuGet and update the shared extension's `using`.

**Impact:** All squad members working on samples.

---

### DevFlow IView Resolution: Platform-Native Visibility and Bounds

**Author:** Holden (Lead Architect)  
**Date:** 2026-04-04  
**Status:** Implemented

**Decision:** Added three virtual extension points to `VisualTreeWalker` (`ResolveIViewWindowBounds`, `ResolveIViewPlatformVisibility`, `PopulateIViewNativeInfo`) that mirror existing `VisualElement`-only methods but accept `IView`. Platform subclasses override to query handler's native view directly.

**Rationale:** Comet views implement `IView` but not `VisualElement`. Previous walker only resolved bounds/visibility for `VisualElement`, causing 98% of Comet elements to report invisible. Fix follows existing virtual method pattern — any framework producing `IView`-but-not-`VisualElement` elements benefits.

**Impact:** DevFlow consumers. Comet apps now report correct visibility/bounds in visual tree.

---

### CometViewController Safe Area: Check ISafeAreaView.IgnoreSafeArea

**Author:** Holden (Lead Architect)  
**Date:** 2026-04-04  
**Status:** Implemented

**Decision:** `CometViewController.LoadView()` now checks `ISafeAreaView.IgnoreSafeArea` before setting `EdgesForExtendedLayout`. Views calling `.IgnoreSafeArea()` get `UIRectEdge.All`; others keep `UIRectEdge.None`. `ViewWillAppear` re-checks and propagates background color to container.

**Rationale:** Hardcoded `UIRectEdge.None` prevented edge-to-edge rendering. Background colors couldn't fill safe area insets, causing letterboxing. Fix respects existing Comet API (`.IgnoreSafeArea()`) that was wired to `ISafeAreaView` but never read.

**Impact:** All Comet iOS apps. Views using `.IgnoreSafeArea()` render edge-to-edge.

---

### View.ToolbarItems() Extension: Environment-Based Toolbar Items

**Author:** Holden (Lead Architect)  
**Date:** 2026-04-04  
**Status:** Implemented

**Decision:** Added `ToolbarItems(params ToolbarItem[])` and `GetToolbarItems()` as environment extensions on `View`, following title/background/safe area pattern. iOS `NavigationViewHandler` reads these when pushing view and applies to `CometViewController`'s `NavigationItem.RightBarButtonItems`. Pushed view's items take priority; falls back to parent `NavigationView`'s items.

**Rationale:** Toolbar items were only set on root view controller during `CreatePlatformView()`. Pushed views had no way to specify their own toolbar items. Environment-based approach is consistent with all per-view metadata handling.

**Impact:** All squad members writing navigation. Usage: `new DetailPage().Title("Detail").ToolbarItems(new ToolbarItem("plus", () => { ... }))`.

---

### DevFlow IApplication Binding: Support CometApp and Non-Controls Hosts

**Author:** Holden (Lead Architect)  
**Date:** 2026-04-04  
**Status:** Implemented

**Decision:** `DevFlowAgentService` now tracks both `_app` (Application, Controls) and `_iApp` (IApplication, interface-only). `BoundApplication` property returns whichever is available (preferring Controls). When `Application.Current` is null after 30 retries, falls back to `StartServerOnly()` which resolves `IApplication` from DI via `IPlatformApplication.Current.Services`.

**Rationale:** `CometApp` implements `IApplication` but not `Application`, so `Application.Current` is never set. Previous code had no fallback, leaving agent unbound. Dual-field approach avoids breaking Controls apps while enabling Comet.

**Impact:** All DevFlow consumers using Comet. DevFlow endpoints now work immediately for Comet apps.

---

### 2026-04-04T02:54:48Z: User Directives (Batch)

**By:** David Ortinau  

**What:**
1. Target score is 9/10 minimum on visual review for every page
2. DevFlow is the ONLY acceptable inspection tool — if DevFlow doesn't meet need, FIX IT (we are DevFlow maintainers)
3. Safe area / letterboxing must be fixed — background edge-to-edge, no white/black bars
4. Native toolbar items must work — if Comet lacks API, implement it (reference .NET MAUI + MauiReactor source)
5. Minimal page-level styling — use global styles, fix at theme level not per-control
6. Run skill-trainer between every turn before starting next turn
7. Ralph runs continuously until 9/10 on New Shot, then carries fixes to all other pages

---

### 2026-04-04T13:24:38Z: Native Platform Controls Directive

**By:** David Ortinau  

**What:** No fake/custom UI elements for platform controls:
- Tab bar MUST be native UITabBar (not custom overlay)
- Toolbar items MUST be native UIBarButtonItem (not ZStack overlay)
- Reference how .NET MAUI does TabbedPage/Shell for native tab bar
- Reference how NavigationPage handles ToolbarItems
- If Comet isn't wiring these natively, fix Comet's implementation
- Native look comes for free — minimal styling needed

---

### Amos Deep Refactor — Centralized Modifiers

**Author:** Amos (Controls & API Dev)  
**Date:** 2026-04-06  
**Status:** Implemented

**Decision:** Centralized repeating control styling into `CoffeeModifiers` with control-type-aware modifiers (casting to Comet controls where needed) and generic `RoundedBorder`/`ClipShape` usage for shared styling.

**Reason:** Avoid inline styling across pages/components while keeping Comet control extensions type-safe. Reduces inline styling calls from 199 to 23 (88% reduction).

**Scope:** CometBaristaNotes sample pages/components and modifier catalog. Expanded to cover all control types (Buttons, Borders, FormFields, Sliders, Toggles, Icons). 28 files updated, net -47 lines. Build passes with 0 errors.

**Impact:** All squad members working on pages — use modifiers instead of inline styling.

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
