# holden — History

Session history for holden.

## Learnings

### 2025-07-22 — BaristaNotes Rewrite Plan Review

**Context:** Reviewed David's 7-phase, 39-todo rewrite plan for CometBaristaNotes.

**Framework state verified:**
- Token<T>, ViewModifier, ControlStyle<T>, ThemeManager, Theme, ButtonStyles, ColorTokens, DefaultThemeStyles — all production-ready in src/Comet/Styles/
- Component<TState> with SetState — production-ready, already used by all BaristaNotes pages
- TabView — functional but styling is handler-delegated (no fluent style API)
- Picker, NavigationView — both solid

**Key architectural facts:**
- Current app Theme.cs uses static Color constants (`Theme.Primary`, etc.), NOT Comet Token<T>
- Namespace collision risk: `CometBaristaNotes.Components.Theme` vs `Comet.Styles.Theme`
- All 15 pages reference the static Theme class — migration to tokens touches everything
- BaristaApp already uses TabView with 3 tabs; first tab is CoffeeDashboardPage (needs swap to ShotLoggingPage, not a full navigation rewrite)
- No dark palette colors exist anywhere — must be defined from scratch or extracted from original MauiReactor AppColors.cs
- Syncfusion.Maui.Gauges NuGet must be removed from .csproj when gauges are replaced

**File paths:**
- App theme constants: `sample/CometBaristaNotes/Components/Theme.cs`
- App entry: `sample/CometBaristaNotes/BaristaApp.cs`
- Form factories: `sample/CometBaristaNotes/Components/FormFields.cs` (static `FormHelpers` class)
- Framework tokens: `src/Comet/Styles/ColorTokens.cs`, `src/Comet/Styles/Token.cs`
- Framework styles: `src/Comet/Styles/BuiltInStyles.cs` (contains ButtonStyles)
- Framework themes: `src/Comet/Styles/Theme.cs`, `src/Comet/Styles/ThemeManager.cs`

### 2025-07-24 — CoffeeTheme Token System & Theme Wiring

**Context:** Created the Comet token-based theme system for BaristaNotes and resolved the Theme namespace collision.

**What was done:**
- Created `sample/CometBaristaNotes/Styles/CoffeeTheme.cs` — full token infrastructure:
  - `CoffeeTokens` static class: Token<Color> for SurfaceElevated, TextPrimary, TextSecondary, TextMuted, Success, Warning, Error, Info; Token<double> for SpacingXXL; Token<string> for font families
  - `CoffeeThemeData` — extra data bag attached to Theme via ConditionalWeakTable (no framework change needed)
  - `CoffeeTheme.Light` and `CoffeeTheme.Dark` — complete Theme instances with ColorTokenSet, TypographyTokenSet (Manrope fonts, app-specific sizes), SpacingTokenSet, ShapeTokenSet, ColorScheme, and legacy color properties
  - `CoffeeTheme.Initialize(IThemeService)` — wires ThemeService.ThemeChanged → ThemeManager.SetTheme
  - `CoffeeTheme.ForMode(AppThemeMode)` — resolves System/Light/Dark to the correct Theme
- Renamed `Components/Theme.cs` → `Components/CoffeeColors.cs` (class name `Theme` → `CoffeeColors`)
- Updated all 20+ files referencing `Theme.Primary` etc. to use `CoffeeColors.Primary`
- Updated `BaristaApp.cs` to call `CoffeeTheme.Initialize(themeService)` at startup
- Updated `ThemeService.ApplyTheme()` to call `ThemeManager.SetTheme(CoffeeTheme.ForMode(mode))`
- Marked `AppColors.cs` as `[Obsolete]` — tokens replace it

**Architecture decision:**
- Used ConditionalWeakTable<Theme, CoffeeThemeData> for app-specific tokens that don't fit Material 3 ColorTokenSet. This avoids modifying the framework's Theme class while keeping strong typing.
- CoffeeColors static class is a bridge — pages still use `CoffeeColors.Primary` inline. Phase 4 will migrate to token-resolved colors.
- Both Light and Dark themes set all Material 3 slots (ColorTokenSet + ColorScheme + legacy properties) so both ButtonStyles and ThemeExtensions resolve correctly.

**Build status:** Zero new errors. Pre-existing errors remain: Icons.cs FontImageSource ambiguity, ShotLoggingPage Syncfusion references.

### 2025-07-25 — Phase 2: App Navigation, Routes & Settings Polish

**Context:** Wired tab icons, verified navigation routes, polished SettingsPage.

**What was done:**
- **BaristaApp.cs**: Replaced non-existent PNG tab icons ("tab_coffee.png" etc.) with SF Symbol names ("cup.and.saucer.fill", "list.bullet.rectangle.portrait.fill", "gearshape.fill"). The iOS CUITabView handler already resolves SF Symbols via `UIImage.GetSystemImage()`. Android handler ignores icons (title-only menus).
- **Navigation routes verified**: All sub-page navigation was already correctly wired by Amos in Phase 1:
  - Settings → Equipment/Beans/Profiles management pages (via `Navigation?.Navigate(new Page())`)
  - BeanManagement → BeanDetailPage(beanId), BeanDetail → BagDetailPage(bagId) / BagFormPage(beanId)
  - EquipmentManagement → EquipmentDetailPage(equipmentId)
  - UserProfileManagement → ProfileFormPage(profileId)
  - ActivityFeed → ShotLoggingPage(shotId) for edit mode
  - All pages have constructor params for IDs. All use `Navigation?.Navigate()`.
- **SettingsPage.cs**: Polished theme selector — 3 buttons now in a `FormHelpers.MakeCard`-wrapped Grid with `"*","*","*"` columns so they fill width evenly (was 100px fixed). Icon color changes with selection state. Thicker border (2px) on selected card vs 1px on unselected.
- **CometBaristaNotes.csproj**: Bumped iOS/MacCatalyst SupportedOSPlatformVersion from 15.0 → 17.0 (required by .NET 11 SDK).

**Architecture decisions:**
- SF Symbols for tab icons is the right call on Apple platforms. Android tab icon support is title-only in CometTabView — a handler enhancement is needed later if Android icon support matters.
- NavigationView wrapping per-tab is already correct (MakeTab helper). Each tab has its own navigation stack.

**Build status:** Zero new errors. 16 pre-existing errors (Syncfusion in ShotLoggingPage, mock service interface gaps).

### 2025-07-25 — Phase 2: Detail Page Rewrites (Bean, Equipment, Profile)

**Context:** Rewrote three detail pages to match original MauiReactor BaristaNotes layout using FormHelpers factories, CoffeeModifiers, CoffeeTheme tokens, and new components.

**What was done:**

1. **BeanDetailPage.cs** (full rewrite):
   - Added `using static Comet.CometControls`, `using CometBaristaNotes.Styles`
   - Notes field → `FormHelpers.MakeFormEditor` with 100px height (was MakeFormEntry — single-line)
   - Error display → red Border with translucent background + red stroke (was plain text)
   - Added section dividers between form, ratings, bags, shots
   - Rating section: "BEAN RATINGS" header + RatingDisplayFactory.Create + distribution bars with sentiment icons
   - Bags section: empty state with centered text, bag cards (roast date + status badge, notes, stats row with shot count + avg rating, chevron, tap → BagDetailPage), "Add Bag" secondary button
   - Shot history: empty state via FormHelpers.MakeEmptyState, ShotRecordCardFactory cards with tap → ShotLoggingPage, "Load More" pagination (20 per page)
   - Delete bean via DisplayAlertAsync confirmation popup
   - Page size bumped from 10 → 20 shots

2. **EquipmentDetailPage.cs** (full rewrite):
   - Added proper Comet/Styles usings + `using static Comet.CometControls`
   - Type picker display names updated: "PuckScreen" → "Puck Screen" (matching original)
   - Notes field → `MakeFormEditor` with 100px height (was MakeFormEntry)
   - Error display → red bordered card (matching BeanDetail pattern)
   - Archive moved store.ArchiveEquipment() after confirmation (was before)

3. **ProfileFormPage.cs** (updated):
   - Replaced inline `BuildAvatar()` with `CircularAvatar` and `ProfileImagePicker` components
   - Edit mode: ProfileImagePicker (120px) with camera badge overlay, tap → PickPhoto
   - Create mode: CircularAvatar placeholder + "Save first to add photo" message
   - Error display upgraded to red bordered card pattern
   - Delete confirmation uses PageHelper.GetCurrentPage() + DisplayAlertAsync (was guarded by null check only)
   - Avatar size increased from 100 → 120

**Architecture notes:**
- All three pages now share identical error display pattern (red Border card)
- All use `FormHelpers.MakeFormEditor` for multi-line fields (consistent 100px height)
- ProfileImagePicker takes `Action<string>? onImagePicked` — the page owns MediaPicker logic, component only triggers the callback
- No new framework changes needed — all pages use existing Comet APIs

**Build status:** Zero new errors. 16 pre-existing errors unchanged (Syncfusion, SqliteDataStore, MockVoiceCommandService).

### 2025-07-25 — Phase 4: ShotLoggingPage Major Rewrite

**Context:** Rewrote the most complex page in CometBaristaNotes — the shot logging form. Previous version was 1078 lines of imperative MAUI controls with Syncfusion radial gauge dependencies.

**What was done:**
- **Full rewrite** of ShotLoggingPage.cs (1078 → 799 lines, -26%)
- **Eliminated all Syncfusion references** — removed `using Syncfusion.Maui.Gauges`, `SfRadialGauge`, `RangePointer`, `ShapePointer`, all imperative MAUI control aliases (`MauiLabel`, `MauiBorder`, etc.)
- **Adopted Component<ShotLoggingPageState>** pattern with proper SetState for all mutations
- **State shape**: 25 properties covering core extraction (DoseIn, GrindSetting, ExpectedTime, ExpectedOutput, ActualTime, ActualOutput, PreinfusionTime), drink type, rating (0-4 index), tasting notes, bag/bean selection, equipment (machine/grinder/accessories), user profiles (maker/recipient), edit mode fields (Timestamp, BeanName), UI state (IsLoading, ErrorMessage), and placeholder hooks (ShowAdviceSection, ShowVoiceSection)

**Syncfusion gauge replacement:**
- Replaced radial gauges with styled numeric entry fields in a card layout
- DoseIn and ActualOutput displayed as centered numeric TextFields with +/- buttons
- Extraction ratio shown as large text "1:{ratio}" between the entries
- Ratio color-coded: green (CoffeeColors.Success) when 2.0-2.5, yellow (CoffeeColors.Warning) otherwise

**Form fields (matching original):**
- All fields use FormHelpers factories: MakeFormEntry, MakeFormPicker, MakeFormSlider, MakeFormEditor, MakeSectionHeader, MakeCard, MakeEmptyState
- Drink Type picker: 6 options (Espresso, Americano, Latte, Cappuccino, Flat White, Cortado)
- Rating: 5 sentiment icons with labels (Terrible/Bad/Average/Good/Excellent)
- Equipment: Machine picker, Grinder picker, Accessory multi-select chips
- User selection: Made By / Made For with avatar circles and cycle-on-tap
- Bag picker with empty state handling
- Pre-infusion time entry (optional)

**Edit mode:**
- Constructor takes shotId, loads existing shot data
- Shows read-only header with bean name + timestamp
- "Update Shot" button instead of "Save Shot"
- Navigation?.Pop() on save in edit mode

**Placeholder hooks for other agents:**
- `ShowAdviceSection` state bool + `RenderAdvicePlaceholder()` — empty card for AI advice agent
- `ShowVoiceSection` state bool + `RenderVoicePlaceholder()` — empty card for voice UI agent

**Architecture:**
- Uses `using static Comet.CometControls` for factory methods
- CoffeeColors static constants for all colors/spacing/radii
- InMemoryDataStore.Instance for data (matching all other pages)
- No DI injection — uses service locator pattern consistent with existing pages

**Build status:** Zero new errors from ShotLoggingPage. 76 unique pre-existing errors remain in other files (CoffeeTheme.cs FontWeight.SemiBold, various NavigationView/BoxView/Modifier issues in other pages).

### 2025-07-26 — ShotLoggingPage Complete Rebuild (337→1103 lines)

**Context:** Amos's build-fix sweep replaced the full ShotLoggingPage (799 lines from my Phase 4 rewrite + Naomi's AI/voice additions) with a 337-line placeholder to fix compilation errors. David requested a complete rebuild combining all three layers: my original form rewrite, Naomi's AI advice UI, and Naomi's voice overlay.

**What was done:**
- **Full rebuild** of ShotLoggingPage.cs (337 → 1103 lines)
- **State shape expanded**: Added AI state (IsLoadingAdvice, AdviceResponse, ShowPromptDetails, AdviceError), voice state (IsVoiceSheetOpen, IsRecording, VoiceTranscript, VoiceState, VoiceChatHistory), and equipment index tracking (SelectedMachineIndex, SelectedGrinderIndex)
- **VoiceChatMessage model**: Added as page-local class for chat history entries

**Form fields (complete):**
- Dose In + Expected Output in a card with large "1:{ratio}" display, color-coded green (2.0-2.5) or yellow
- Grind Setting, Expected Time, Actual Time, Actual Output, Pre-infusion Time
- Bag picker with empty state + "New Bean" button for inline creation
- Drink Type picker (6 options)
- Machine picker, Grinder picker (filtered from equipment by type)
- Accessory multi-select chips (toggle selection with visual state)
- Made By / Made For user selectors (avatar circle, cycle-on-tap)
- Rating row with 5 sentiment icons + label text
- Tasting Notes editor (100px height via MakeFormEditor)
- Save Shot / Update Shot primary button + Cancel secondary button

**AI Advice section (below save, visible in edit mode or after first save):**
- "Get AI Advice" button with magic icon, bordered pill style
- Loading state: ActivityIndicator + "Analyzing your shot..."
- Results: adjustment cards (parameter + direction + amount), reasoning text, source indicator
- Prompt transparency toggle (MakeToggleRow) showing raw prompt when enabled
- Error state with retry button
- Service resolved via IPlatformApplication DI (IAIAdviceService)

**Voice overlay (ZStack layer):**
- Floating mic FAB at bottom-right with shadow
- Bottom sheet overlay with semi-transparent scrim
- Handle bar + close button
- ScrollView chat history (user messages right-aligned with Primary bg, AI messages left-aligned with Surface bg)
- State text: "Tap to speak" / "Listening..." / "Processing..."
- Large mic button (pulsing red when recording, primary when idle)
- Full speech → voice command → result pipeline via ISpeechRecognitionService + IVoiceCommandService

**Build rules followed (from Amos's fix list):**
- `Microsoft.Maui.FontWeight` used explicitly (3 occurrences)
- `.OnTap(_ => ...)` with view parameter everywhere
- `(float)` casts for Frame dimensions where needed
- No `Comet.NavigationView` shadowing
- `VStack(spacing: 0f)` to avoid float/LayoutAlignment ambiguity

**Architecture:**
- ZStack root: form ScrollView + voice FAB + voice overlay (conditional)
- Services resolved lazily via IPlatformApplication.Current?.Services (matching ActivityFeedPage pattern)
- CancellationTokenSource management for AI advice and speech recognition
- Render decomposed into 8 methods: Render, RenderFormContent, BuildRatingRow, BuildAccessoryChips, BuildUserSelector, RenderAdviceSection, RenderVoiceFAB, RenderVoiceOverlay

**Build status:** Zero errors. 107 warnings (all pre-existing in CoffeeTheme.cs nullability and other files).

### 2025-07-27 — Three Framework-Level Fixes: DevFlow IView, Safe Area, Toolbar Items

**Context:** David identified three framework-level bugs blocking proper DevFlow inspection and edge-to-edge rendering for Comet apps.

**FIX 1: DevFlow VisualTreeWalker — Comet IView visibility/bounds**

**Problem:** The `CreateElementInfo()` IView branch (for Comet views that aren't VisualElement) reported `isVisible: false` and `bounds: null` for 98% of elements. Root causes:
1. `iView.Visibility != Visibility.Collapsed` — Comet views may not set Visibility in their environment, so it defaults correctly to Visible, but the real issue is that the *platform native view* might actually be rendered and visible even when the IView.Visibility check looks wrong
2. `IView.Frame` isn't populated by Comet's layout system the same way as VisualElement.Frame
3. No `ResolveWindowBounds()` call for IView — platform-native bounds resolution only existed for VisualElement
4. `IText` check at line 1276 didn't match Comet's source-generated Text control (implements ILabel, not IText)

**What was done:**
- **VisualTreeWalker.cs (Core):** Added three virtual methods for platform subclasses to override:
  - `ResolveIViewWindowBounds(IView)` — same pattern as `ResolveWindowBounds(VisualElement)` but for non-VE IViews
  - `ResolveIViewPlatformVisibility(IView)` — checks native platform view Hidden/Alpha/size
  - `PopulateIViewNativeInfo(ElementInfo, IView)` — fills NativeType, accessibility info
- **IView branch enhanced:** Calls all three virtual methods. Back-fills Bounds from WindowBounds when IView.Frame is empty. Checks ILabel, ITextButton, IEntry, IEditor, ISearchBar for text extraction (not just IText). Falls back to CometViewResolver.TryExtractText for reflection-based text.
- **PlatformVisualTreeWalker.cs (Agent):** Overrides all three methods with iOS/Android/Windows implementations. iOS uses `ConvertRectToView`, `UIView.Hidden`, `UIView.Alpha`. Android uses `GetLocationInWindow`, `ViewStates.Visible`. Windows uses `TransformToVisual`.
- **CometViewResolver.cs (Core):** Added `TryExtractText()` method that extracts text via reflection — checks `Value` property (source-generated Text control), `Text` property, `Binding<T>.CurrentValue` wrappers, and environment-based `Text.Value` key.

**FIX 2: Safe Area / Edge-to-Edge Background**

**Problem:** `CometViewController.LoadView()` hardcoded `EdgesForExtendedLayout = UIRectEdge.None`, preventing content from extending under the status bar. Background color only filled content rect, causing white/black letterboxing.

**What was done:**
- **CometViewController.cs:** `LoadView()` now checks `ISafeAreaView.IgnoreSafeArea` on the current view. When true, sets `EdgesForExtendedLayout = UIRectEdge.All` + `ExtendedLayoutIncludesOpaqueBars = true`.
- **ViewWillAppear:** Also re-checks safe area setting (pushed views may differ from root). Propagates the view's background color to the CometView container so it extends into safe area insets.
- CUINavigationController already has `View.BackgroundColor = UIColor.SystemBackground` — no change needed there.

**FIX 3: Toolbar Items on Pushed Views**

**Problem:** Toolbar items were only set during `CreatePlatformView()` on the root view controller. Pushed views never got toolbar items. No dynamic update mechanism existed.

**What was done:**
- **EnvironmentData.cs:** Added `EnvironmentKeys.View.ToolbarItems` constant.
- **ViewExtensions.cs:** Added `ToolbarItems(params ToolbarItem[])` extension method and `GetToolbarItems()` getter, using the environment system (same pattern as `.Title()`).
- **NavigationViewHandler.iOS.cs:** Extracted toolbar item creation into a shared `ApplyToolbarItems(CometViewController, View, NavigationView)` static method. Called for both root VC and pushed VCs. Pushed view's own toolbar items (via `.ToolbarItems()`) take priority; falls back to NavigationView's items.
- Navigation lambda now calls `ApplyToolbarItems(newVc, toView, nav)` after creating each pushed CometViewController.

**Build status:** All three projects build with zero errors:
- Comet.SourceGenerator: 0 errors, 0 warnings
- Comet (net11.0-ios): 0 errors (1334 pre-existing warnings)
- DevFlow.Agent.Core: 0 errors, 0 warnings
- DevFlow.Agent: 0 errors, 0 warnings
- Comet.Tests: 973 passed, 1 pre-existing failure (template test), 26 skipped

### 2025-07-24 — FIX 4: DevFlow _app Binding for Comet Apps

**Problem:** `CometApp` implements `IApplication` (MAUI interface) but NOT `Microsoft.Maui.Controls.Application`. DevFlow's startup loop retried `Application.Current` 30 times (15 seconds) and always failed for Comet apps. It fell back to `StartServerOnly()` which left `_app = null`, causing ALL DevFlow endpoints to return "Agent not bound to app" errors.

**Root cause chain:**
1. `CometApp` registered via `builder.Services.TryAddSingleton<IApplication, TApp>()` in `UseCometApp<T>()`
2. `Application.Current` is a static on `Microsoft.Maui.Controls.Application` — never set for Comet
3. DevFlow `AgentServiceExtensions` only checked `Application.Current`, no fallback
4. All endpoint handlers gated on `_app == null`

**What was done:**
- **DevFlowAgentService.cs:**
  - Added `_iApp` field (`IApplication?`) alongside `_app` (`Application?`)
  - Added `BoundApplication` property: returns `(IApplication?)_app ?? _iApp`
  - Added `StartServerOnly()` method that calls `TryResolveIApplicationFromDI()` using `IPlatformApplication.Current?.Services?.GetService<IApplication>()`
  - Added `BindIApp(IApplication)` for late-binding (routes to `_app` if it's a Controls Application)
  - Replaced ALL `_app` read-access with `BoundApplication` across every endpoint handler
  - Added `TryBubbleCometGestureTap()` — walks parent chain to find Comet tap gestures (child Text views don't carry their own gestures)
  - Added `case IView iViewNoHandler:` in HandleTap switch for views without platform handlers
- **VisualTreeWalker.cs:**
  - Added `IApplication` overloads for `WalkTree`, `GetElementById`, `HitTestByBounds`, `Query`, `QueryCss`
  - Original `Application` overloads delegate to new `IApplication` overloads
  - Fixed `HitTestByBounds` to safely cast `IWindow → Window` for `Page`/`Navigation` access
- **AgentServiceExtensions.cs:**
  - iOS and macOS fallback paths now call `service.StartServerOnly(mainDispatcher)` instead of just logging failure
  - Captured `mainDispatcher` before `Task.Run()` to ensure correct thread for dispatch

**Key architectural insight:** The `IApplication` interface provides `Windows` collection and `IVisualTreeElement` tree walking. `Page`, `Navigation.ModalStack`, and `Application.Current` are Controls-specific. All endpoint code must use safe casts (`window as Window`) when accessing these.

**Build status:** All projects build clean, 970 tests pass (4 pre-existing failures, 26 skipped).

### 2025-07-25 — Theming Architecture: Comet vs MauiReactor ThemeKey

**Context:** David requested research comparing Comet's theming system to MauiReactor's `ThemeKey()` pattern. Full analysis produced as decision document in `.squad/decisions/inbox/holden-theming-architecture.md`.

**Key findings:**
- Comet's token system (Token<T>, ThemeManager, ViewModifier) is architecturally **more powerful** than MauiReactor's ThemeKey pattern
- MauiReactor's `ThemeKey` is a string-indexed, per-control-type dictionary lookup wrapping MAUI's Style system
- Comet's `ViewModifier` + `.Modifier()` achieves the same ergonomics without string indirection: `.Modifier(CoffeeModifiers.CardTitle)` ≈ `.ThemeKey(ThemeKeys.CardTitle)`
- The real gap: CoffeeModifiers currently use `CoffeeColors.X` (static constants) instead of `CoffeeTokens.X` (theme-resolving tokens), so they don't switch with light/dark mode
- **No framework changes needed** — fix is entirely at the app level

**Architecture decision: Do NOT add `.ThemeKey()` to framework.** Reasons:
1. Type safety — concrete modifier references vs string keys
2. No per-control-type dispatch tables needed — ViewModifier.Apply works on any View
3. Composability — `.Then()` chains work out of the box
4. Pattern already exists and is in production use

**Recommended pattern — TypographyModifier:**
```csharp
public class TypographyModifier : ViewModifier
{
    readonly Token<FontSpec> _typography;
    readonly Token<Color> _color;
    public TypographyModifier(Token<FontSpec> typography, Token<Color> color) { ... }
    public override View Apply(View view) => view
        .Typography(_typography)
        .Color(ThemeManager.TokenBinding(_color));
}
```

**Work items identified (all app-level):**
1. Add TypographyModifier class to CoffeeModifiers.cs
2. Rewrite all CoffeeModifiers to use Token<T> resolvers
3. Add semantic modifier names (Headline, SubHeadline, SecondaryText, CardTitle, etc.)
4. Migrate pages from inline `.FontFamily().FontSize().Color()` to `.Modifier()`

**Key file paths:**
- MauiReactor reference: `~/work/BaristaNotes/BaristaNotes/BaristaNotes/Resources/Styles/ApplicationTheme.cs` (ThemeKey definitions)
- MauiReactor reference: `~/work/BaristaNotes/BaristaNotes/BaristaNotes/Resources/Styles/ThemeKeys.cs` (string constants)
- Comet modifiers: `sample/CometBaristaNotes/Styles/CoffeeModifiers.cs`
- Comet tokens: `sample/CometBaristaNotes/Styles/CoffeeTheme.cs` (CoffeeTokens + CoffeeThemeData)
- Framework ViewModifier: `src/Comet/Styles/ViewModifier.cs`
- Framework token resolution: `src/Comet/Styles/Token.cs`, `src/Comet/Styles/ThemeManager.cs`

## 2026-04-05 — Theming Architecture Research (Scribe Orchestration)

**Status:** ✅ Complete

**Task:** Research Comet's theming architecture vs MauiReactor's ThemeKey pattern. Produce architecture proposal.

**Findings:**
- Comet has deep, production-ready token-based system (Token<T>, ThemeManager, ViewModifier, ControlStyle<T>)
- Gap vs MauiReactor is **purely adoption** — modifiers use hardcoded statics instead of theme-aware tokens
- Recommendation: Make CoffeeModifiers token-aware (use TypographyTokens + CoffeeTokens + ThemeManager.TokenBinding)
- Add semantic modifier names (Headline, SubHeadline, Card, FormField, etc.) mirroring MauiReactor vocabulary
- Create reusable TypographyModifier class for typography token + color token combinations
- **No framework changes needed** — all work is app-level

**Deliverable:** `.squad/decisions/inbox/holden-theming-architecture.md` (merged to decisions.md)

**Handoff:** Ready for Amos implementation.
