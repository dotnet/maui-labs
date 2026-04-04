### DevFlow IView Resolution: Platform-Native Visibility and Bounds for Non-VisualElement Views

**Author:** Holden (Lead Architect)
**Date:** 2025-07-27
**Status:** Implemented

**Decision:** Added three virtual extension points to `VisualTreeWalker` (`ResolveIViewWindowBounds`, `ResolveIViewPlatformVisibility`, `PopulateIViewNativeInfo`) that mirror the existing `VisualElement`-only methods but accept `IView`. Platform subclasses override these to query the handler's native view directly.

**Rationale:** Comet views implement `IView` but not `VisualElement`. The DevFlow tree walker previously only resolved bounds/visibility for `VisualElement`, causing 98% of Comet elements to report invisible with null bounds. The fix follows the existing virtual method pattern rather than adding Comet-specific logic to the core walker — any framework that produces `IView`-but-not-`VisualElement` elements benefits.

**Impact:** All DevFlow consumers. Comet apps now report correct visibility and bounds in the visual tree. Text extraction covers ILabel, ITextButton, IEntry, IEditor, ISearchBar, plus reflection fallback via CometViewResolver.

---

### CometViewController Safe Area: Check ISafeAreaView.IgnoreSafeArea

**Author:** Holden (Lead Architect)
**Date:** 2025-07-27
**Status:** Implemented

**Decision:** `CometViewController.LoadView()` now checks the current view's `ISafeAreaView.IgnoreSafeArea` property before setting `EdgesForExtendedLayout`. Views that call `.IgnoreSafeArea()` get `UIRectEdge.All`; all others keep `UIRectEdge.None`. `ViewWillAppear` re-checks and propagates background color to the container view.

**Rationale:** The previous hardcoded `UIRectEdge.None` prevented edge-to-edge rendering. Background colors couldn't fill safe area insets, causing visible letterboxing. The fix respects the existing Comet API (`.IgnoreSafeArea()` extension) that was already wired to `ISafeAreaView` but never read by the view controller.

**Impact:** All Comet iOS apps. Views using `.IgnoreSafeArea()` now render edge-to-edge. Default behavior (no safe area extension) is unchanged.

---

### View.ToolbarItems() Extension: Environment-Based Toolbar Items Per View

**Author:** Holden (Lead Architect)
**Date:** 2025-07-27
**Status:** Implemented

**Decision:** Added `ToolbarItems(params ToolbarItem[])` and `GetToolbarItems()` as environment-based extension methods on `View`, following the same pattern as `.Title()`. The iOS `NavigationViewHandler` reads these when pushing a view and applies them to the new `CometViewController`'s `NavigationItem.RightBarButtonItems`. Pushed view's items take priority; falls back to the parent `NavigationView`'s items.

**Rationale:** Previously, toolbar items were only set during `CreatePlatformView()` on the root view controller. Pushed views had no way to specify their own toolbar items. The environment-based approach is consistent with how Comet handles all per-view metadata (title, background, safe area, etc.).

**Impact:** All squad members writing navigation. To add toolbar items to a pushed view: `new DetailPage().Title("Detail").ToolbarItems(new ToolbarItem("plus", () => { ... }))`.
