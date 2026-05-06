# Changelog

All notable changes to Comet are documented in this file. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).


## [Unreleased]

### Added

- **Unified reactive state system.** Introduced `Signal<T>`, `Computed<T>`,
  `Effect`, `ReactiveScope`, `ReactiveScheduler`, `PropertySubscription<T>`,
  and `SignalList<T>` as the foundation for all state management. For a
  practical guide to using these primitives, see the
  [Reactive State Guide](reactive-state-guide.md). Signals are
  thread-safe with lock-based writes and lock-free reads. Computed values use
  lazy memoization with version-based invalidation. The reactive scheduler
  coalesces multiple writes into a single batch flush.

- **PropertySubscription binding primitive.** `PropertySubscription<T>`
  provides three binding modes: static value, `Func`-based with automatic
  dependency tracking, and bidirectional `Signal`-based with write-back.
  Property-level reads are isolated from body-level reads through nested
  `ReactiveScope` instances, enabling fine-grained updates without full body
  rebuilds.

- **ReactiveScope dependency tracking.** `[ThreadStatic]` scope stack captures
  which reactive sources are read during evaluation. Nested scopes support
  property-level isolation. Background thread reads are untracked by design.

- **ReactiveScheduler batching.** Static scheduler collects dirty effects and
  views, then processes them in a two-phase flush (effects first, then views).
  Includes a max flush depth guard (100 iterations) to detect infinite loops
  and a `SuppressNotifications` flag to prevent re-dirtying during view
  transfers.

- **ReactiveDiagnostics.** Runtime diagnostic utilities for inspecting the
  reactive dependency graph.

- **Reactive analyzers.** Roslyn analyzers that warn about common reactive
  anti-patterns such as reading `.Value` outside a lambda.

- **comet-migrate tool.** CLI tool to automate migration from the legacy
  `State<T>`/`Binding<T>` API to the new `Signal<T>`/`PropertySubscription<T>`
  system.

- **Design token and style system.** `ControlStyle<T>`, `ViewModifier`,
  `ColorTokens`, `TypographyTokens`, `SpacingTokens`, `ShapeTokens` for
  structured theming. Style builders are generated for each control alongside
  the control wrapper class.

- **Theme system.** `Theme` base class with `ThemeColors` presets (Light and
  Dark). `Theme.Current` setter auto-applies and fires `ThemeChanged` event.
  `DefaultThemeStyles.Register(theme)` populates control styles from the
  active theme's color scheme.

- **Key-aware reconciliation.** Opt-in `.Key(string)` fluent API for stable
  identity during view diffing. Uses O(1) dictionary lookup when keys are
  present, falls back to index-based diffing when absent.

- **Component base class.** `Component<TState>` and
  `Component<TState, TProps>` extend `View` with typed state, props, and
  lifecycle. `SetState()` batches mutations through the reactive scheduler.
  `IComponentWithState` interface enables hot reload state transfer.

- **Typed navigation.** `CometShell.RegisterRoute<TView>()`,
  `GoToAsync<TView>()`, and `Navigate<TView>()` with props injection and
  `IQueryAttributable` fallback.

- **CollectionView handler.** Full `CollectionView` support with incremental
  loading (`RemainingItemsThreshold`), swipe actions, and pull-to-refresh.

- **CarouselView handler.** `CarouselViewHandler` with platform-specific
  support.

- **BlazorWebView integration.** Embed Blazor Hybrid content inside Comet
  views.

- **BindableLayout.** Attach data-bound item templates to any layout container.

- **ValueConverters.** Converter infrastructure for binding transformations.

- **AnimationBuilder.** Fluent API for constructing view animations.

- **TabView.** Tab-based navigation container with platform-native tab bars.

- **Infinite scroll.** `RemainingItemsThreshold` support for `CollectionView`
  to trigger incremental data loading.

- **MenuFlyout, ImageBrush, KeyboardAccelerator, PolySegments.** Additional
  MAUI parity features.

- **TableView cells.** `ImageCell` and `ViewCell` implementations.

- **Windows CometViewHandler.** Platform handler for Windows (WinUI).

- **macOS support.** `net10.0-macos` (AppKit) build infrastructure, handler
  stubs, `CometWindowHandler` for creating visible `NSWindow` instances, and
  `CometMacApp` sample.

- **Native MetadataUpdateHandler.** Direct .NET Hot Reload support through the
  native `MetadataUpdateHandler` attribute.

- **Platform automation identifiers.** Every Comet view receives a stable
  auto-generated platform ID for Appium and accessibility inspection.

- **Accessibility extensions.** `SemanticDescription`, `SemanticHint`,
  `SemanticHeadingLevel`, `AutomationName`, `AutomationHelpText`,
  `IsInAccessibleTree`, and `IsReadOnly` fluent extensions.

- **CometControlsGallery.** 50+ demo pages covering controls, state, layout,
  animations, styling, and reactive state validation. Adaptive phone layout
  with platform-aware text for iOS.

- **CometBaristaNotes sample.** Full MVU conversion of BaristaNotes with SQLite
  persistence (EF Core), Syncfusion gauges, MaterialSymbols and Manrope fonts,
  shot filter popup, and themed styling.

- **CometWeather sample.** WeatherTwentyOne app converted to MVU.

- **CometFeatureShowcase sample.** BindableLayout, ValueConverters,
  AnimationBuilder, TabView, and infinite scroll demos.

- **CometStressTest sample.** Performance and stress test scenarios.

- **CometAllTheLists sample.** Five different list/collection implementations.

- **CometTaskApp sample.** Task manager demonstrating TabView navigation with
  `Component<TState>`.

- **State management benchmarks.** XAML vs MVU benchmark suite with batched
  variants and iteration setup.

- **Comprehensive test suite.** 640+ tests covering reactive primitives, view
  lifecycle, hot reload, components, navigation, reconciliation, themes,
  styles, accessibility, and platform interop.

- **Documentation.** Getting started guide, reactive state guide, state
  management deep dive, migration guide, architecture decision records,
  testing guide, accessibility guide, troubleshooting FAQ, and changelog.

### Changed

- **Source generator updated for PropertySubscription.** The Roslyn source
  generator (`CometViewSourceGenerator`) now produces `PropertySubscription<T>`
  overloads for all generated controls alongside the original `Binding<T>`
  constructors.

- **View.cs migrated from StateManager to ReactiveScope.** Body evaluation,
  dependency tracking, and state transfer now use the reactive scope system
  instead of the legacy `StateManager`.

- **Handwritten controls migrated from Binding to PropertySubscription.**
  `Button`, `Text`, `TextField`, `Slider`, `Toggle`, and other manually
  authored controls use `PropertySubscription<T>` for property binding.

- **Upgraded to .NET 10.** All projects target `net10.0-*` frameworks. Source
  generator updated for C# 14 compatibility.

- **MAUI SDK version bumped to 10.0.41.** Updated `MauiVersion` in
  `Directory.Build.targets`.

- **Slider and Entry binding behavior.** `ReactiveScope` is suppressed during
  binding evaluation to prevent body rebuilds during drag/type interactions.

- **RadioButton handler.** Fixed handler registration and implemented native
  `CUIRadioButton` for Mac Catalyst with left alignment, mutual exclusion,
  and label binding.

- **Text word wrap.** Adapted for MAUI 10 which removed `MaxLines` and
  `LineBreakMode` from `ILabel`.

- **CollectionView inheritance.** `CollectionView<T>` now inherits from
  `CollectionView` instead of `ListView<T>`.

- **State implicit conversions deprecated.** `State<T>` and `Binding<T>`
  implicit conversions emit deprecation warnings directing users to
  `Signal<T>`.

### Removed

- **Legacy StateManager.** The `StateManager` class and its associated
  `Binding<T>` / `BindingObject` tracking infrastructure have been removed.
  All state management now flows through `Signal<T>`, `ReactiveScope`, and
  `ReactiveScheduler`.

- **Legacy State class internals.** Internal state tracking via
  `INotifyPropertyRead` events and the property-name-based binding system
  has been replaced by the version-based `IReactiveSource` protocol.

- **Fake MapView control.** Removed the placeholder `MapView` that was not
  backed by a real handler.

- **MediaElement.** Removed as it belongs to the .NET MAUI Community Toolkit,
  not the MAUI SDK.

### Fixed

- **Slider drag resets.** Eliminated body rebuild loop on drag tick by
  suppressing `ReactiveScope` during binding evaluation.

- **Entry focus loss on keystroke.** Same root cause as slider drag; resolved
  by the `PropertySubscription` fine-grained update path.

- **StackOverflowException during state updates.** Added reentrance guard in
  `ReactiveScheduler` with `SuppressNotifications` flag and max flush depth
  limit.

- **Background signal writes not updating UI.** Fixed double-queuing in
  `ReactiveScheduler.EnsureFlushScheduled()` when signal writes originate
  from background threads.

- **Sidebar navigation not updating.** `Reactive<T>` now implements
  `IReactiveSource`, enabling proper subscription tracking.

- **Gallery BindingState path.** Restored correct `BindingState` path and
  added two-way callbacks for gallery demo pages.

- **iOS black screen on iOS 26.** Fixed `page.Loaded` not firing by upgrading
  to .NET 10 and updating the source generator for C# 14.

- **macOS startup crash.** Fixed `ThreadHelper.RunOnMainThread` to handle
  the AppKit main thread correctly.

- **RadioButton infinite recursion.** Fixed `CrossPlatformMeasure` infinite
  recursion by reverting to native rendering with null `PresentedContent`.

- **ShapeView stroke color.** Fixed type mismatch between `Color` and `Brush`
  for stroke color, and corrected dash pattern rendering.

- **Pan and Pointer gestures.** Fixed gesture recognizers losing tracking
  mid-interaction.

- **CALayerInvalidGeometry crash on iOS.** Replaced eagerly-laid-out grid
  helpers with HStack/VStack for stability during initial layout.

- **RecursiveLock issues in StateManager.** Fixed lock ordering and missing
  using statements before removing StateManager entirely.

- **State pipeline allocations.** Eliminated closure allocations and lock
  contention in the hot path. Typed backing field and stable binding fast
  path for `State<T>`.

- **Locale-independent currency formatting.** Fixed test failures caused by
  locale-dependent number formatting.

- **CS0104 ambiguity.** Removed types that conflicted with
  `Microsoft.Maui.Controls` namespace.

- **WebView navigation.** Fixed navigation event handling in gallery pages.

- **SearchBar, CollectionView, CarouselView, and asymmetric corners.** Fixed
  rendering and interaction bugs across multiple controls.

- **Inline state binding anti-pattern.** Fixed sample pages that were reading
  `.Value` directly instead of using lambda bindings.

- **CometHostHandler measurement.** Fixed handler to measure content instead
  of filling constraints.


## See Also

- [Migration Guide](migration-guide.md) -- how to move from the classic API
  surface to the evolved MVU API, including handling breaking changes.
- [Reactive State Guide](reactive-state-guide.md) -- the new unified reactive
  state system introduced in this release.
