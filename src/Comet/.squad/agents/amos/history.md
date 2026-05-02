# amos — History

Session history for amos.

## Learnings

**2026-04-03 — Centralized typography modifiers for BaristaNotes**

- Introduced `TypographyModifier` and a semantic typography set (`Headline`, `SubHeadline`, `TitleSmall`, `Body`, `SecondaryText`, `MutedText`, `Caption`, `SmallText`, `TinyText`, `MicroText`, `FormLabel`, `FormValue`, `CardTitle`, `CardSubtitle`, `BodyStrong`, `LabelStrong`, `BadgeText`, `ValueText`) in `sample/CometBaristaNotes/Styles/CoffeeModifiers.cs`.
- Button modifiers now own typography (font family/size) to keep pages/components free of inline font chains.
- Refactored pages/components (`Pages/ShotLoggingPage.cs`, `SettingsPage.cs`, `ActivityFeedPage.cs`, `BagDetailPage.cs`, `BagFormPage.cs`, `BeanDetailPage.cs`, `BeanManagementPage.cs`, `EquipmentDetailPage.cs`, `EquipmentManagementPage.cs`, `ProfileFormPage.cs`, `UserProfileManagementPage.cs`, plus `Components/FormFields.cs` and `ShotRecordCard.cs`) to apply `CoffeeModifiers` for text styling.
- `FormHelpers.MakeCard` and `MakeListCard` now apply `CoffeeModifiers.Card/ListCard` to centralize card styling.

**2025-07-22 — Icons, ControlStyles, and page cleanup for BaristaNotes**

- `CometBaristaNotes.Components.Theme` was renamed to `CoffeeColors` per team decision. Always use `CoffeeColors` for palette/sizing constants, not `Theme`.
- `Comet.FontImageSource` conflicts with `Microsoft.Maui.Controls.FontImageSource` in global usings. Use a `using FontIcon = Comet.FontImageSource;` alias. Comet's version takes `(fontFamily, glyph, size, color)` in its constructor — no `FontFamily`/`Size` properties like MAUI's.
- `Comet.Button` and `Comet.View` conflict with MAUI types in `ControlStyle<T>` declarations. Use aliases: `using CometButton = Comet.Button;`.
- `ControlStyle<T>.Set(environmentKey, value)` only accepts string keys from `EnvironmentKeys`. Shape/clip/border can't be set this way — use `ViewModifier` subclasses for that (see `CoffeeModifiers.cs`).
- Pre-existing build errors in `ShotLoggingPage.cs` (Syncfusion `RangePointer`/`ShapePointer`) are not from my changes.

**Files changed:**
- `Components/Icons.cs` — expanded from 33 to 36 glyph constants + 27 factory methods
- `Styles/CoffeeControlStyles.cs` — new file with PrimaryButton, SecondaryButton, DangerButton, CardStyle
- `BaristaApp.cs` — swapped first tab from CoffeeDashboardPage to ShotLoggingPage ("New Shot")
- Deleted: `Pages/CoffeeDashboardPage.cs`, `Pages/CoffeeBeanDetailPage.cs`, `Pages/ShotDetailPage.cs`

**2025-07-22 — Phase 2: Components & Management Pages**

- Rewrote `Components/RatingDisplay.cs` — now shows large average rating (36pt), total shots, best/worst stats, and distribution bars (rating 4→0) with Grid-based fill bars using primary color on surface variant background. Uses CoffeeModifiers.Card.
- Updated `Components/ShotRecordCard.cs` — replaced inline hex colors with CoffeeColors tokens, added equipment row (machine + grinder with icons), profiles (made by/for with person icons), and colored rating badge (circle: 0-1 red, 2 yellow, 3-4 green with white text). Uses CoffeeModifiers.Card. Replaced sentiment icons with numeric badge.
- Rewrote `Pages/BeanManagementPage.cs` — full card layout per bean: name (bold), factory icon + roaster, globe icon + origin, calendar icon + date. Uses NavigationView.Navigate(this, ...) for proper Comet nav. Empty state with coffee icon.
- Rewrote `Pages/EquipmentManagementPage.cs` — card layout with name (bold) + type label. Empty state uses espresso machine icon from coffee-icons font via new `iconFontFamily` parameter on MakeEmptyState.
- Rewrote `Pages/UserProfileManagementPage.cs` — card layout with name (bold) + "Member since" with calendar icon. NavigationView.Navigate(this, ...) for navigation.
- Added `Distribution` dict (int→int) to `RatingAggregate` model and updated `BuildAggregate` in `InMemoryDataStore` to populate it.
- Extended `FormHelpers.MakeEmptyState` with optional `iconFontFamily` parameter (defaults to MaterialIcons) to support coffee-icons font glyphs.
- All management pages now use `NavigationView.Navigate(this, target)` instead of `Navigation?.Navigate(target)` — matches preferred Comet pattern from AGENTS.md.
- Pre-existing errors (Syncfusion, MockAIAdviceService, MockSpeechRecognitionService) unchanged — not from my changes.

**Files changed:**
- `Models/BaristaModels.cs` — added Distribution to RatingAggregate
- `Services/InMemoryDataStore.cs` — BuildAggregate populates distribution dict
- `Components/RatingDisplay.cs` — full rewrite (42→115 lines)
- `Components/ShotRecordCard.cs` — updated (125→173 lines)
- `Components/FormFields.cs` — added iconFontFamily param to MakeEmptyState
- `Pages/BeanManagementPage.cs` — full rewrite (60→119 lines)
- `Pages/EquipmentManagementPage.cs` — full rewrite (60→96 lines)
- `Pages/UserProfileManagementPage.cs` — full rewrite (60→96 lines)

**2025-07-24 — Phase 3: Detail/Form Pages & Activity Feed**

- Rewrote `Pages/BagDetailPage.cs` — full layout match with original MauiReactor version. Bean name (read-only), editable roast date, notes with 500-char limit, status toggle (complete/active), shot count stat card, RatingDisplay (or "No ratings" fallback), related shots section using ShotRecordCardFactory, save/delete buttons with confirmation dialog. Uses NavigationView.Navigate/Pop for Comet nav.
- Rewrote `Pages/BagFormPage.cs` — create-only form: header with bean name, roast date entry, notes with char limit, validation (no future dates, 500 char max) with styled error display, primary "Add Bag" button. Validation extracted to separate method.
- Rewrote `Pages/ActivityFeedPage.cs` — full activity feed with filter support. Four empty states: loading (ActivityIndicator), no shots (coffee icon), no matching shots (filter_off icon + "Clear Filters" button), error (warning icon + "Retry"). Filter toolbar with active filter count badge. Shot cards via ShotRecordCardFactory with tap-to-navigate to ShotLoggingPage. Load more button for pagination. Data change notifier subscription for live updates.
- Updated `Pages/ShotFilterPopup.cs` — fixed rating chips to use 0-4 scale (was 1-5), "Clear All" now invokes onClear callback and dismisses popup (was only resetting chip visuals without dismissing). Kept chip toggle, CoffeeTheme token colors (primary bg when selected, surface when not).
- Added IShotService methods: `GetShotsForBag(bagId)`, `GetFilteredShots(ShotFilterCriteria)`, `GetBeansWithShots()`, `GetPeopleWithShots()` — implemented in both InMemoryDataStore and SqliteDataStore.
- Comet DatePicker has no OnDateChanged callback, so roast date uses text field entry (pragmatic choice — matches existing pattern).
- All pages use `Component<TState>` pattern with `SetState` per team wisdom.

**Files changed:**
- `Services/IBaristaServices.cs` — added 4 new IShotService methods
- `Services/InMemoryDataStore.cs` — implemented new IShotService methods
- `Services/SqliteDataStore.cs` — implemented new IShotService methods
- `Pages/BagDetailPage.cs` — full rewrite (179→218 lines)
- `Pages/BagFormPage.cs` — full rewrite (84→128 lines)
- `Pages/ActivityFeedPage.cs` — full rewrite (142→234 lines)
- `Pages/ShotFilterPopup.cs` — updated (243→233 lines)

**2025-07-25 — Zero-Error Build: Framework Issues Cleanup**

- All 152 build errors were in `ShotLoggingPage.cs` — a structural break where Holden's new Component<TState> top half (lines 1-210) was welded to the old imperative MAUI+Syncfusion bottom half (lines 211-1194). The Render() method body was truncated mid-expression.
- Replaced the broken bottom half (lines 211-1194) with a clean ~130-line Comet Render method. Uses existing state class and SaveShot logic. All form fields wired through SetState. Rating row uses sentiment icons with OnTap. Placeholder for Holden's full rewrite.
- Fixed 39 additional errors uncovered after ShotLoggingPage cleanup:
  - `FontWeight.SemiBold` → `FontWeight.Semibold` (8 instances in CoffeeTheme.cs) — .NET MAUI uses lowercase 'b'
  - `AppTheme` ambiguity (Comet.Styles.AppTheme vs Microsoft.Maui.ApplicationModel.AppTheme) — fully qualified to `Comet.Styles.AppTheme`
  - `BoxView(color)` factory calls → `new Comet.BoxView(color)` — BoxView has no CometControls factory; also ambiguous with Microsoft.Maui.Controls.BoxView
  - `.Modifier(CoffeeModifiers.Card)` — needed `using Comet.Styles;` for the extension method
  - `NavigationView.Navigate/Pop` — shadowed by CometControls factory method; fixed to `Comet.NavigationView.Navigate/Pop`
  - `OnTap(() => ...)` → `OnTap(_ => ...)` — delegate is `Action<T>` not `Action`
  - `double` → `float` casts for `.Frame(width:, height:)` parameters
- Build result: 0 errors, 1448 warnings (warnings are pre-existing and acceptable).

**Learnings:**
- `Comet.BoxView` conflicts with `Microsoft.Maui.Controls.BoxView` in files that have both `using static Comet.CometControls` and implicit MAUI usings. Always fully qualify as `Comet.BoxView`.
- `CometControls.NavigationView(View)` factory method shadows the `Comet.NavigationView` class when using `using static`. Must fully qualify static Navigate/Pop calls.
- `.Frame(width:, height:)` takes `float?`, not `double`. Always cast `double` values.
- `OnTap<T>(Action<T>)` receives the view as parameter — use `_ =>` discard, not `() =>`.

**Files changed:**
- `Pages/ShotLoggingPage.cs` — replaced broken Render (1194→337 lines)
- `Styles/CoffeeTheme.cs` — FontWeight.Semibold + AppTheme qualification
- `Components/CircularAvatar.cs` — BoxView + Frame float casts
- `Components/ProfileImagePicker.cs` — BoxView + Frame float casts + OnTap fix
- `Components/RatingDisplay.cs` — BoxView + using Comet.Styles
- `Components/ShotRecordCard.cs` — using Comet.Styles
- `Components/FormFields.cs` — Frame float cast
- `Pages/ActivityFeedPage.cs` — using Comet.Styles + NavigationView fix
- `Pages/BagDetailPage.cs` — BoxView + NavigationView fix
- `Pages/BagFormPage.cs` — NavigationView fix
- `Pages/BeanManagementPage.cs` — using Comet.Styles + NavigationView fix
- `Pages/EquipmentManagementPage.cs` — using Comet.Styles + NavigationView fix
- `Pages/UserProfileManagementPage.cs` — using Comet.Styles + NavigationView fix

**2025-07-25 — Fix MCT001: CommunityToolkit builder chain ordering**

- Build error `MCT001: .UseMauiCommunityToolkit() must be chained to .UseMauiApp<T>()` blocked all CometBaristaNotes builds.
- Root cause: two issues. (1) In `MauiProgram.cs`, `.UseMauiCommunityToolkit()` was a standalone `builder.` statement instead of being fluently chained after `.UseCometApp<T>()` / `.UseCometSampleDebugHost()`. (2) Inside `SampleRuntimeDebugExtensions.cs`, the helper's internal `.UseMauiApp<CometSampleDebugHostApplication>()` call on line 44 was also flagged by the MCT source generator analyzer because it's compiled per-project.
- Fix in `MauiProgram.cs`: chained `.UseMauiCommunityToolkit().UseUXDiversPopups()` directly after both the DEBUG and RELEASE app setup calls.
- Fix in `SampleRuntimeDebugExtensions.cs`: added `#pragma warning disable/restore MCT001` around the internal `.UseMauiApp<T>()` call with a comment explaining the caller chains CommunityToolkit after the helper returns.
- Build result: 0 errors, 27 warnings (all pre-existing).

**Learnings:**
- MCT001 is a CommunityToolkit Roslyn source generator diagnostic that scans ALL `.UseMauiApp<T>()` call sites in a project, not just the main MauiProgram. Shared helper files that call `.UseMauiApp<T>()` internally will trigger MCT001 even if the caller chains `.UseMauiCommunityToolkit()` afterward. Use `#pragma warning disable MCT001` in helpers where the chaining responsibility is delegated to the caller.
- Always chain `.UseMauiCommunityToolkit()` fluently in the same expression as `.UseMauiApp<T>()` / `.UseCometApp<T>()` — separate `builder.` statements don't satisfy the analyzer.

**Files changed:**
- `sample/CometBaristaNotes/MauiProgram.cs` — chained CommunityToolkit + UXDivers after app setup
- `sample/Shared/RuntimeDebug/SampleRuntimeDebugExtensions.cs` — pragma suppress MCT001

**2025-07-26 — ShotLoggingPage visual rewrite to match original BaristaNotes**

- Complete rewrite of ShotLoggingPage.cs render section (lines 424-842 replaced with ~600 lines of new code) to match the original MauiReactor BaristaNotes layout at 8/10 fidelity.
- **Hero section above fold**: DoseGauges → TimeSlider → UserAvatars → RatingSelector → TastingNotes → Add Shot button. Matches original's information hierarchy exactly.
- **Additional Details below fold**: Divider → muted header → Bag/Grind/Time/Output/DrinkType pickers → Equipment pickers → AI Advice section.
- **Gauge replacement**: Syncfusion radial gauges replaced with circular ring displays (120px, 14px stroke in Primary color) with 28pt bold centered value, scale labels in 3x3 Grid, and −/coffee-icon/+ stepper buttons. This is the only permitted visual difference from the original.
- **Slider**: Layered ZStack approach (rounded 50px Border capsule background + Slider on top) matching original's `FormSliderField` component design.
- **Navigation title**: Removed `.Title(title)` from BaristaApp.MakeTab; added large 34pt bold inline title as first form element. Changed nav bar background to `CoffeeColors.Background` to match page.
- **User selection**: Circular avatars (60px) with "Made by" → arrow → "For" labels, cycle-on-tap behavior.
- **Rating**: 5 sentiment face icons from MaterialIcons font, selected state in Primary color.
- Preserved all existing functionality: voice FAB, voice overlay, AI advice, accessory chips, edit mode, error display.

**Learnings:**
- `new Comet.Grid(...)` required to avoid ambiguity with `Microsoft.Maui.Controls.Grid` when global MAUI usings are active. Same applies to `new Comet.BoxView(color)`.
- Comet `TextEditor` does not expose `Placeholder` property through its generated API. `EnvironmentKeys.Editor.Placeholder` exists but has no handler mapper — setting it has no effect. Use external label instead.
- Grid cells with `.Cell(row:, column:)` and `.GridColumnSpan(n)` work well for layered layouts (e.g., slider capsule background + slider in same Grid cell).
- No `.HCenter()` method in Comet — use `HStack(new Spacer(), content, new Spacer())` sandwich pattern for horizontal centering.
- `VStack(spacing:)` and `HStack(spacing:)` spacing parameter is `float`, not `double`.
- Setting `.Title("")` on a page in Render() works to clear the nav bar title when the NavigationView was created without `.Title()`.

**Files changed:**
- `sample/CometBaristaNotes/Pages/ShotLoggingPage.cs` — full render section rewrite (~600 new lines), added `using Comet.Styles`
- `sample/CometBaristaNotes/BaristaApp.cs` — removed `.Title(title)` from page, changed nav bar colors to match page background

**2026-04-03 — Diagnosed & fixed splash screen hang on iOS Simulator**

- Root cause: uncommitted local changes had added a **duplicate DevFlow agent registration** — a project reference to the local `Microsoft.Maui.DevFlow.Agent` (namespace `Microsoft.Maui.DevFlow.Agent`) on top of the NuGet `Redth.MauiDevFlow.Agent` (namespace `MauiDevFlow.Agent`) already inherited from `sample/Directory.Build.targets`. The `MauiProgram.cs` also had a direct `builder.AddMauiDevFlowAgent()` call (local version) alongside `builder.EnableSampleRuntimeDebugging()` (which internally calls the NuGet version's `AddMauiDevFlowAgent()`).
- Each `AddMauiDevFlowAgent()` call does `Task.Run(() => brokerReg.TryRegisterAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult()` — a 5-second main-thread blocking WebSocket connection attempt to the broker. With two competing implementations, that's 10+ seconds of blocking during `CreateMauiApp()`, plus two lifecycle event registrations both polling for `Application.Current` and trying to start HTTP servers.
- Fix: removed the uncommitted local additions (DevFlow project reference from csproj, `using Microsoft.Maui.DevFlow.Agent`, and the direct `builder.AddMauiDevFlowAgent()` call), restoring files to committed HEAD state. Built fresh binary and deployed to iOS 26.2 simulator.
- App now launches past splash screen and shows the full tab UI (New Shot, Activity, Settings) with the ShotLoggingPage gauges, time slider, user avatars, and rating faces all rendering correctly.

**Learnings:**
- `sample/Directory.Build.targets` already provides `Redth.MauiDevFlow.Agent` NuGet + shared `SampleRuntimeDebugExtensions.cs` for ALL samples in Debug mode. Do NOT add a second DevFlow agent via project reference — the two implementations use different namespaces (`MauiDevFlow.Agent` vs `Microsoft.Maui.DevFlow.Agent`) and will both register independently, causing double broker blocking and port conflicts.
- `EnableSampleRuntimeDebugging()` already calls `AddMauiDevFlowAgent()` internally. Never call `AddMauiDevFlowAgent()` separately after `EnableSampleRuntimeDebugging()`.
- Always build a fresh binary before diagnosing "stuck on splash" — stale binaries from dirty working trees are the #1 red herring.
- iOS simulator deploy via `dotnet build -t:Run` can hang in MSBuild restore for multi-TFM projects. Workaround: build with `-f net11.0-ios` first, then manually install with `xcrun simctl install <UDID> <path.app>` and launch with `xcrun simctl launch --console <UDID> <bundle-id>`.

**2026-04-03 — New Shot page visual polish: horseshoe arc gauges & layout compaction**

- Replaced flat circular ring gauges with custom horseshoe arc gauges using `GaugeArcDrawable` + `Comet.GraphicsView`. The arc spans 270° (lower-left to lower-right) with rounded line caps, matching the reference BaristaNotes app's gauge style.
- `Comet.GraphicsView` has a `.Draw` property (`Action<ICanvas, RectF>`), not an `IDrawable` setter. Wire via `gv.Draw = drawable.Draw`.
- MAUI.Graphics coordinate system: 0° = 3 o'clock, positive = counter-clockwise. Horseshoe: startAngle=225° (lower-left), sweep 270° clockwise.
- Scale labels are overlaid inside the gauge container using a ZStack (GraphicsView + Grid), preventing horizontal overflow. Previous approach with labels in external Grid rows caused clipping at screen edges.
- Label Grid uses fixed-width columns `{24, "*", 24}` — Auto-width columns caused center value text truncation.
- Compaction pass to bring "Add Shot" button above the fold: main VStack spacing SpacingS→SpacingXS, title bottom margin removed, tasting notes 48→40px, rating icons 48→44px, Add Shot margin SpacingS→SpacingXS.
- State defaults changed: Rating 2→3, Time 0→40 to match reference screenshot.
- Added floating toolbar pill (mic + camera icons) in top-right corner.

**Learnings:**
- `ICanvas.DrawArc(x, y, w, h, startAngle, endAngle, clockwise, closed)` — the clockwise parameter is critical; setting it wrong draws the complement arc.
- `StrokeLineCap = LineCap.Round` gives the rounded arc ends matching the reference.
- ZStack overlay is the right pattern for labels-inside-gauge — avoids parent layout clipping issues that occur with labels in separate rows outside the gauge container.
- iPhone 16 Pro (874pt viewport) vs iPhone 15 Pro Max (932pt) — 58pt difference means designs captured on Max devices may not fit on smaller Pro screens without compaction.

**Files changed:**
- `sample/CometBaristaNotes/Pages/ShotLoggingPage.cs` — GaugeArcDrawable class, RenderSingleGauge with ZStack overlay, RenderToolbar, layout compaction throughout

**2026-04-04 — Visual alignment: spacing, centering, edge-to-edge, global styles**

- **FormFields.cs**: Removed `CoffeeModifiers.SectionHeader` from `MakeSectionHeader` — it set FontSize 22 conflicting with the desired 13pt. Added `.VerticalTextAlignment(TextAlignment.Center)` to `MakeFormEntry`, `MakeFormPicker`, and `MakeFormEntryWithLimit` TextField/Picker controls. Changed `MakeFormEntryWithLimit` TextField background from `CoffeeColors.SurfaceVariant` to `Colors.Transparent` (already inside a styled Border). Replaced inline Border styling (CornerRadius/Background/StrokeThickness) with `.Modifier(CoffeeModifiers.FormField)` on `MakeFormEntry`, `MakeFormPicker`, and `MakeFormEntryWithLimit`.
- **ShotLoggingPage.cs**: Main VStack spacing changed from `SpacingXS` (4px) to `SpacingM` (16px) matching original's `Spacing(AppSpacing.M)`. Time slider height increased from 44px to 50px matching `CoffeeColors.FormFieldHeight`. Tasting Notes TextEditor height increased from 40px to 80px matching original's `HeightRequest(80)`. ScrollView top padding added (`SpacingXL` = 32px) for status bar clearance. Added `.IgnoreSafeArea()` on root content for edge-to-edge background. Removed compensating top margins on title, Add Shot button, divider, user selection row, rating selector, and time slider that were working around the previous 4px VStack spacing.
- **All pages**: Added `.IgnoreSafeArea()` to every page's root return statement — ActivityFeedPage, SettingsPage, BagDetailPage (3 return paths), BagFormPage, BeanDetailPage, BeanManagementPage (2 return paths), EquipmentDetailPage, EquipmentManagementPage (2 return paths), UserProfileManagementPage (2 return paths), ProfileFormPage.
- **Default rating**: Already set to 3 (index 3 = happy smile) in state init — no change needed.
- Build result: 0 errors, 28 warnings (all pre-existing).

**Learnings:**
- When VStack spacing changes, audit ALL child margins for compensating padding that is no longer needed. The old 4px spacing had caused SpacingXS/SpacingS margins to be added to many children for visual breathing room — with 16px spacing, these become excessive.
- `CoffeeModifiers.FormField` applies ClipShape, Background, Frame height, and Padding — don't duplicate any of these on the Border that receives the modifier. The inner control still needs its own `.Background(Colors.Transparent)` to avoid overriding the modifier's background.
- Picker and TextField both support `.VerticalTextAlignment(TextAlignment.Center)` for centering text within a taller container.

**Files changed:**
- `sample/CometBaristaNotes/Components/FormFields.cs` — MakeSectionHeader, MakeFormEntry, MakeFormPicker, MakeFormEntryWithLimit
- `sample/CometBaristaNotes/Pages/ShotLoggingPage.cs` — spacing, sizing, padding, margins, IgnoreSafeArea
- `sample/CometBaristaNotes/Pages/ActivityFeedPage.cs` — IgnoreSafeArea
- `sample/CometBaristaNotes/Pages/SettingsPage.cs` — IgnoreSafeArea
- `sample/CometBaristaNotes/Pages/BagDetailPage.cs` — IgnoreSafeArea (3 paths)
- `sample/CometBaristaNotes/Pages/BagFormPage.cs` — IgnoreSafeArea
- `sample/CometBaristaNotes/Pages/BeanDetailPage.cs` — IgnoreSafeArea
- `sample/CometBaristaNotes/Pages/BeanManagementPage.cs` — IgnoreSafeArea (2 paths)
- `sample/CometBaristaNotes/Pages/EquipmentDetailPage.cs` — IgnoreSafeArea
- `sample/CometBaristaNotes/Pages/EquipmentManagementPage.cs` — IgnoreSafeArea (2 paths)
- `sample/CometBaristaNotes/Pages/UserProfileManagementPage.cs` — IgnoreSafeArea (2 paths)
- `sample/CometBaristaNotes/Pages/ProfileFormPage.cs` — IgnoreSafeArea

**2025-07-23 — Status bar, vertical compaction, and edge-to-edge polish**

- **Status bar fix (app-level):** CometWindow uses MAUI's default WindowHandler on iOS (not CometWindowHandler, which is macOS-only). But IgnoreSafeArea wasn't propagating through the TabView→NavigationView→Page hierarchy because `EdgesForExtendedLayout` was only checked on the page VC, not the outer NavigationView wrapper. Fix: (1) Set `.Background(CoffeeColors.Background).IgnoreSafeArea()` on the NavigationView in `MakeTab`. (2) Use `NSTimer.CreateScheduledTimer(0.5, ...)` in `FinishedLaunching` to set `UIWindow.BackgroundColor` and `RootViewController.View.BackgroundColor` after the window exists. (3) Change `CUINavigationController` and `CUITabView` from `UIColor.SystemBackground` to `UIColor.Clear` so the window background shows through.
- **Vertical compaction:** VStack spacing 16→12→8 (SpacingS). Title font 34→28pt. Tasting notes 80→60→48px. These changes compensate for having an inline title (the original uses iOS large nav bar title which doesn't consume VStack space).
- **CometViewController.ViewDidAppear window bg:** Added code to set `View.Window.BackgroundColor` when IgnoreSafeArea is true, falling back to `_containerView.BackgroundColor`. Helps but insufficient alone because the tab's CometViewController sees NavigationView as CurrentView, not the page.
- **Key learning:** `CUITabView.Setup()` creates CometViewControllers directly per tab. The tab's CurrentView is NavigationView, not the inner page. So IgnoreSafeArea must be set on the NavigationView wrapper, not just the page body.
- **Key learning:** The "black status bar" on iPhone 16 Pro was actually the Dynamic Island hardware cutout + black UIWindow background. Setting window bg to match page color resolves it.
- **Key learning:** `UIColor.SystemBackground` in the CUINavigationController and CUITabView blocks the window background from showing through. Using `UIColor.Clear` allows the window background to propagate.

### Files modified (framework):
- `src/Comet/Platform/iOS/CometViewController.cs` — ViewDidAppear window bg propagation
- `src/Comet/Platform/iOS/CUINavigationController.cs` — SystemBackground → Clear
- `src/Comet/Platform/iOS/CUITabView.cs` — SystemBackground → Clear

### Files modified (sample):
- `sample/CometBaristaNotes/BaristaApp.cs` — .Background().IgnoreSafeArea() on NavigationView
- `sample/CometBaristaNotes/MauiProgram.cs` — NSTimer window bg + using Microsoft.Maui.Platform
- `sample/CometBaristaNotes/Pages/ShotLoggingPage.cs` — VStack 8, title 28pt, tasting notes 48px

**2026 — Remove fake toolbar overlay, native toolbar items & tab bar cleanup**

- Removed custom `RenderToolbar()` ZStack overlay from ShotLoggingPage.cs (the floating pill with mic + camera icons).
- Removed `RenderToolbar()` call from the Render() method's ZStack — form content + voice FAB remain.
- Added native toolbar items (mic + camera) to the New Shot tab's NavigationView in BaristaApp.cs using SF Symbol names (`mic.fill`, `camera.fill`). The handler checks `IconGlyph.Contains('.')` → resolves via `UIImage.GetSystemImage` → renders as native UIBarButtonItem.
- Restored `TabBarBackgroundColor`, `TabBarTintColor`, `TabBarUnselectedColor` in BaristaApp — these are appropriate theming (color only, not shape).
- Removed the real problem: `CornerRadius = 20` + `MaskedCorners` + `MasksToBounds` in framework `CUITabView.ApplyTabBarAppearance()` (lines 77-84). This was making the native UITabBar look like a custom floating pill. Color-only appearance APIs preserved.
- Tab icons verified: SF Symbols "cup.and.saucer.fill" / "list.bullet.rectangle.portrait.fill" / "gearshape.fill" resolve via UIImage.GetSystemImage in CUITabView.Setup.
- Build: 0 errors, pre-existing warnings only. Tests: 973 pass, 1 pre-existing failure.

**Learnings:**
- `Comet.ToolbarItem` conflicts with `Microsoft.Maui.Controls.ToolbarItem`. Always fully qualify as `Comet.ToolbarItem`.
- For SF Symbol toolbar items, use object initializer `new Comet.ToolbarItem { IconGlyph = "symbol.name" }` — omit IconFontFamily so the handler takes the SF Symbol path (`IconGlyph.Contains('.')` check).
- `NavigationView.ToolbarItems` list feeds into ApplyToolbarItems on the root VC. For pushed views, use `View.ToolbarItems()` extension instead.
- The rounded-corner pill styling on tabs was a framework-level concern in CUITabView, not a sample-level problem. Removing colors from the sample just suppressed the trigger but didn't fix the root cause.

**Files changed:**
- `src/Comet/Platform/iOS/CUITabView.cs` — removed rounded-corner code from ApplyTabBarAppearance
- `sample/CometBaristaNotes/BaristaApp.cs` — SF Symbol toolbar items, restored tab bar color APIs
- `sample/CometBaristaNotes/Pages/ShotLoggingPage.cs` — removed RenderToolbar() method and call

**2026-07-11 — D002-D007: Visual Review Fixes (Form Centering, Signal Performance, TextEditor)**

- **D002 — Form field vertical centering:** Converted `MakeFormEntry` from Border-wrapping to Grid-overlay pattern matching the reference (`~/work/BaristaNotes`). Added `.VerticalLayoutAlignment(LayoutAlignment.Center)` to TextField, Picker, and read-only Text controls in `MakeFormEntry`, `MakeFormPicker`, `MakeReadOnlyField`, and `MakeFormEntryWithLimit`. Required `using LayoutAlignment = Microsoft.Maui.Primitives.LayoutAlignment` alias to resolve ambiguity with `Microsoft.Maui.Controls.LayoutAlignment`.
- **D003+D004 — Slider/stepper performance:** Replaced `SetState` calls for dose, yield, and time with `Signal<double>` fields (`_doseIn`, `_yieldOut`, `_time`). Stepper buttons now mutate signals directly. Time slider uses `SignalExtensions.Slider(_time, 0, 60)` for two-way binding. `RenderSingleGauge` signature changed from `(decimal value, ..., Action<decimal> onChange)` to `(Signal<double> signal, ..., double min/max/step)`. Signals synced to ShotRecord in SaveShot; reset to defaults on new-shot save.
- **D005 — TextEditor typing fix:** Added `SignalExtensions.TextEditor(Signal<string>)` factory to framework (`src/Comet/Helpers/SignalExtensions.cs`). Uses `PropertySubscription<string>` with `IEditor.Text` for two-way binding. Replaced `string _tastingNotes` field with `Signal<string> _tastingNotes`. TextEditor now uses `SignalExtensions.TextEditor(_tastingNotes)` — no OnTextChanged/SetState.
- **D006 — Equipment popup:** Already implemented (`.OnTap(_ => ShowEquipmentPopup())` on equipment button, `ShowEquipmentPopup()` with `ModalView.Present`). No changes needed.
- **D007 — Avatar row centering:** Added `.Frame(width: 32f)` to the arrow icon for consistent width, added `.HorizontalLayoutAlignment(LayoutAlignment.Center)` to the HStack.
- Build: 0 errors (maccatalyst, iOS). Tests: 973 passed, 1 pre-existing failure (template test).

**Learnings:**
- `LayoutAlignment` is ambiguous in MAUI samples: `Microsoft.Maui.Controls.LayoutAlignment` vs `Microsoft.Maui.Primitives.LayoutAlignment`. Comet's `VerticalLayoutAlignment`/`HorizontalLayoutAlignment` extensions expect `Primitives`. Always add `using LayoutAlignment = Microsoft.Maui.Primitives.LayoutAlignment;` in sample files.
- `SignalExtensions.Slider()` and `SignalExtensions.TextEditor()` must be called fully qualified when `using static Comet.CometControls` is in scope, since CometControls has its own `Slider()`/`TextEditor()` factories that take plain values.
- Signal two-way binding eliminates full-page rebuilds for interactive controls (slider, stepper, text editor). Pattern: declare `Signal<T>` field → pass to `SignalExtensions.Factory()` → read `.Value` in SaveShot.
- ViewModifiers should avoid control-specific extension methods on `View` (e.g., `StrokeColor`, `OnColor`) unless casting to the Comet control type; use `RoundedBorder`/`ClipShape` for generic styling.

**Files changed:**
- `src/Comet/Helpers/SignalExtensions.cs` — added `TextEditor(Signal<string>)` factory
- `sample/CometBaristaNotes/Components/FormFields.cs` — Grid overlay for MakeFormEntry/MakeFormEntryWithLimit, VerticalLayoutAlignment.Center on all form controls, LayoutAlignment alias
- `sample/CometBaristaNotes/Pages/ShotLoggingPage.cs` — Signal<double> for dose/yield/time, Signal<string> for tastingNotes, Signal-bound slider/TextEditor, avatar row centering, LayoutAlignment alias

## 2026-04-05 — Theming System Implementation & Page Refactor (Scribe Orchestration)

**Status:** ✅ Complete & Verified

**Task:** Implement centralized theming system and refactor all pages/components based on Holden's architecture proposal.

**Deliverables:**
1. **TypographyModifier** — Framework-level class for theme-aware typography + color (src/Comet/Styles/)
2. **CoffeeModifiers Refactor** — 19 semantic modifiers (Headline, SubHeadline, CardTitle, Card, FormField, PrimaryButton, etc.)
   - All now use TypographyTokens + CoffeeTokens instead of hardcoded CoffeeColors
   - Modifiers now reactively update on light/dark theme changes
3. **Page Refactoring** — 25 pages/components refactored
   - Removed 399 net lines of inline styling
   - Pattern: `.FontSize().FontWeight().Color()` chains → `.Modifier(CoffeeModifiers.X)`
4. **Visual Alignment Fixes**
   - Form entry helpers use `.Modifier(CoffeeModifiers.FormField)`
   - Section headers corrected
   - All page root views call `.IgnoreSafeArea()` for edge-to-edge background
   - VStack spacing standardized to SpacingM (16px)

**Quality Metrics:**
- ✅ Build: 0 errors, 35 warnings
- ✅ Visual parity: SSIM 0.003 dissimilarity (pre/post)
- ✅ App launch: Successful
- ✅ DevFlow tree: All views visible, correct bounds
- ✅ Screenshots: Identical layouts

**Adoption Rules for Team:**
- Use `.Modifier(CoffeeModifiers.X)` for all text styling
- Wrap form field Borders in `.Modifier(CoffeeModifiers.FormField)`
- Use CoffeeTokens.X for theme-aware colors (not CoffeeColors.X statics)

**Records Created:**
- `.squad/orchestration-log/20260405T023519Z-amos-theming-refactor.md`
- Merged to decisions.md via scribe
