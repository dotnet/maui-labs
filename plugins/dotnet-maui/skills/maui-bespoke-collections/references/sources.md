# Portable source ledger

Research checked **2026-10-12**; mobile behavior, interactions and Apple profiling updated
**2026-10-13**.
Vendor pages are live; sample revisions below are
source-inspected. Refresh version-sensitive advice for the consuming project.
Native documentation is not a C# compile test; publisher demonstrations are not
our measurements. These references remain inside the installed skill package.

## Local execution evidence

### R4
maui-labs nonshipping
[mobile photo-masonry sample](https://github.com/dotnet/maui-labs/tree/17ce2e9245387b0ab355df14498d41066d7818d6/samples/BespokeCollections)
and its
[evidence record](https://github.com/dotnet/maui-labs/blob/17ce2e9245387b0ab355df14498d41066d7818d6/samples/BespokeCollections/EVIDENCE.md):
SDK 10.0.401/workload set 10.0.401, MAUI 10.0.110, Apple 26.5.10318/Xcode 26.6,
Android 36.1.69. The implementation and results were obtained in the research
worktree, not the R1 baseline commit. Build/runtime facts apply to this narrow
slice, not every platform or generated recipe. Physical performance remains
unqualified. In a checkout inspect the actual sample/evidence files; they are
not bundled with an individually installed skill.

## Retained-content hosting and field maintenance

### H1
MAUI 10.0.41 at `d15964ef4f0a6d0740b655ba7c6be9b96be8b187`:
[TemplatedItemViewHolder](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items/Android/TemplatedItemViewHolder.cs)
and [ItemContentView](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items/Android/ItemContentView.cs):
compatible content reuse, binding/logical ownership, constrained measure and
native arrangement. These include internal helpers, not public custom-handler
base classes or a ready-to-copy universal wrapper.

### H2
[LayoutExtensions](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Layouts/LayoutExtensions.cs)
and [LayoutViewGroup](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Platform/Android/LayoutViewGroup.cs):
public IView desired-size/frame margin accounting versus native backing layout
padding/measure, logical/pixel conversion and arrange. Internal handler helpers
may perform a legitimate final exact text pass; that is not permission for a
second incompatible estimated-height authority.

### H3
[Items2 TemplatedCell2](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items2/iOS/TemplatedCell2.cs)
and [GeneralWrapperView](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Platform/iOS/GeneralWrapperView.cs):
constrained point-space width/variable height, cached constraints, distinct
supplementary sizing and matching deferred arrange. Wrapper/internal methods
are source evidence, not public APIs or newly compiled retained-text examples.

User-supplied 2026-10-13 field feedback informed the operational checkpoints,
fixture-validity, loading-height and cleanup recommendations. Its executing
distribution revision/provenance was unknown, and reported environment/agent
failures were not independently diagnosed or attributed as architectural defects.
No private application paths/code, raw transcripts or runtime data were incorporated.

## MAUI

### M1
[Custom handler creation](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/handlers/create?view=net-maui-10.0): public mappers, lifecycle and registration.

### M2
[CollectionView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/?view=net-maui-10.0): .NET 10 optimized UIKit handlers; separate .NET 11 Windows repeater moniker, not a 10.0.41 baseline fact.

### M3
[Compiled bindings](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/data-binding/compiled-bindings?view=net-maui-10.0): template scope, type safety and trim/AOT requirements.

### M4
MAUI 10.0.41 source, `d15964ef4f0a6d0740b655ba7c6be9b96be8b187`:
[UIKit cells](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items2/iOS/TemplatedCell2.cs),
[Android adapter](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items/Android/Adapters/ItemsViewAdapter.cs),
[Windows handler](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items/ItemsViewHandler.Windows.cs).
Useful prior art, not supported public internal-cell APIs.

### M5
[MAUI .NET 8 performance work](https://devblogs.microsoft.com/dotnet/dotnet-8-performance-improvements-in-dotnet-maui/): historical publisher-measured JNI/marshaling and item-context storage lessons.

## Apple

### AP1
[Understanding hitches](https://developer.apple.com/documentation/xcode/understanding-hitches-in-your-app)
and [render-loop explanation](https://developer.apple.com/videos/play/tech-talks/10855/):
commit versus render hitches; variable deadlines; expected/actual buffered frame
lifetime versus presentation cadence; hitch duration and provider-specific ratios.
The current Organizer categories differ from historical 5/10 ms/s guidance.
Record the chosen provider/version and project budget rather than declaring one
historical threshold universal. Current Apple documentation checked 2026-10-13.

### AP2
Installed `xctrace 27.1 (27A9269)` help for record, export and symbolicate:
explicit device/PID, bounded capture, separate TOC/XPath modes, dSYM input and
fresh run/output semantics. A task-owned native macOS Swift utility was compiled,
recorded with Time Profiler + Points of Interest, and exported successfully.
The initial custom category yielded zero workload rows; `.pointsOfInterest`
yielded exactly one completed named interval. CPU rows were also exported.
This is command/signpost smoke evidence, not a MAUI control or native frame/hitch
benchmark, and does not certify physical iOS, Catalyst or AppKit scrolling.
Use installed help again for a different Xcode; export schemas are not fixed APIs.

### AP3
[Recording performance data](https://developer.apple.com/documentation/os/recording-performance-data),
[OSSignposter](https://developer.apple.com/documentation/os/ossignposter)
and [animation intervals](https://developer.apple.com/documentation/os/ossignposter/beginanimationinterval(_:id:)):
balanced named intervals/state and distinct concurrent IDs; Swift API availability
and native animation tracking. The compiled native smoke validates the selected
Swift API/category on its Mac, not a public C# bridge.
[macios OSLog](https://github.com/dotnet/macios/blob/354c2a645855dfdf774cc5e098cd214764f475b5/src/CoreFoundation/OSLog.cs)
is a logging surface, not an OSSignposter binding. Legacy signpost macros aren't
ordinary DllImport entry points.

### AP4
[Apple diagnostic properties](https://github.com/dotnet/macios/blob/354c2a645855dfdf774cc5e098cd214764f475b5/docs/building-apps/build-properties.md#enablediagnostics),
[DiagnosticConfiguration](https://github.com/dotnet/macios/blob/354c2a645855dfdf774cc5e098cd214764f475b5/docs/building-apps/build-properties.md#diagnosticconfiguration),
[Mono diagnostic tracing](https://github.com/dotnet/runtime/blob/f05194509ddbce06bd9459d05eb9d0bd51ec2e4c/docs/design/mono/diagnostics-tracing.md)
and [dotnet-dsrouter](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dsrouter):
Release Mono components/EventSource/trimming, CoreCLR/NativeAOT distinctions,
router transport/profiles, development-only endpoints and supported provider
coverage. Internal runtime sample builder instructions aren't MAUI SDK properties.
Local router/trace 9.0.661903 help verified profiles, printed-configuration workflow
and bounded collection flags; no new managed mobile trace was executed.

### AP5
[Scrolling/deceleration metric](https://developer.apple.com/documentation/xctest/xctossignpostmetric/scrollinganddecelerationmetric),
[signpost metric](https://developer.apple.com/documentation/xctest/xctossignpostmetric)
and [XCTHitchMetric](https://developer.apple.com/documentation/xctest/xcthitchmetric):
native XCTest scope/availability; the signpost metric needs matching intervals.
Native UI-test/metric coverage must be verified for the actual collection/input.
These are not automatically APIs in .NET xUnit or DevFlow; no new XCTest run is claimed.

### AP6
[Organizer's historical scrolling metric](https://developer.apple.com/videos/play/wwdc2020/10076/),
[MXAnimationMetric ratio](https://developer.apple.com/documentation/metrickit/mxanimationmetric/hitchtimeratio)
and [newer HitchTimeMetric](https://developer.apple.com/documentation/metrickit/hitchtimemetric):
tracked scrolling/animation denominator, units, reporting/cohort boundaries and
version-sensitive API changes. Current Swift HitchTimeMetric documentation lists
27.0 availability; that is not a C# binding in the sample's Apple 26.5 pack.
Do not substitute aggregate telemetry for a particular controlled traversal.

### U1
[WWDC 2019 layout](https://developer.apple.com/videos/play/wwdc2019/215/): UIKit/AppKit compositional sections, sizing, chrome and orthogonal scrolling; iOS 13/macOS 10.15 introductions.

### U2
[WWDC 2020 collections](https://developer.apple.com/videos/play/wwdc2020/10097/): iOS 14 list sections, registrations and snapshots.

### U3
[WWDC 2021 performance](https://developer.apple.com/videos/play/wwdc2021/10252/): registration lifetime, stable IDs, iOS 15 reconfiguration/prefetch/image preparation and byte-aware caching.

### U4
[Modern collections sample](https://developer.apple.com/documentation/uikit/implementing-modern-collection-views): actual UIKit/AppKit supplementary and orthogonal implementations. Current sample project minimums are not API introduction dates.

### U5
[Visible-item invalidation hook](https://developer.apple.com/documentation/uikit/nscollectionlayoutsection/visibleitemsinvalidationhandler): bounded per-layout visible-item effects.

### U6
[CHT waterfall implementation](https://github.com/chiahsien/CHTCollectionViewWaterfallLayout/blob/ca75bd8ae2ca50dd0c621fe1f521be805df44fd4/Source/CHTCollectionViewWaterfallLayout.m): aspect ratios, column/chrome caches, but linear union-rectangle scans and full preparation.

### U7
[IGListKit FAQ](https://instagram.github.io/IGListKit/best-practices-and-faq.html): stable identity, section ownership, initial large reload tradeoff and estimated-layout limitations.

### U8
[Texture collection nodes](https://texturegroup.org/docs/containers-ascollectionnode.html): asynchronous/thread-safe node architecture, not ordinary cell reuse or background MAUI view creation.

### U9
[Historical iCarousel](https://github.com/nicklockwood/iCarousel/blob/c9e043e1fa767f88d31de8dae95585345e8e7676/iCarousel/iCarousel.m): native pools/perspective; archived repository, not a current dependency recommendation.

### A1
[NSCollectionView](https://developer.apple.com/documentation/appkit/nscollectionview) and
[compositional layout](https://developer.apple.com/documentation/appkit/nscollectionviewcompositionallayout): native item controllers and sections.

### A2
[AppKit prefetch](https://developer.apple.com/documentation/appkit/nscollectionviewprefetching) and
[macios bindings](https://github.com/dotnet/macios/blob/354c2a645855dfdf774cc5e098cd214764f475b5/src/appkit.cs): verify SDK availability; inspected identifiers require NSObject and layout attributes lack UIKit Transform3D.

## Android

### B1
[RecyclerView guide](https://developer.android.com/develop/ui/views/layout/recyclerview): adapters, holders and standard geometry.

### B2
[Rendering performance](https://developer.android.com/topic/performance/issues/render): full invalidation, nested pools, prefetch, cheap binds and texture work.

### B3
[DiffUtil](https://developer.android.com/reference/androidx/recyclerview/widget/DiffUtil): immutable inputs, cost and UI publication.

### B4
[ConcatAdapter config](https://developer.android.com/reference/androidx/recyclerview/widget/ConcatAdapter.Config) and
[RecyclerView](https://developer.android.com/reference/androidx/recyclerview/widget/RecyclerView): compatibility, temporary positions, recycling and viewport HasFixedSize.

### B5
[Flowers adapter sample](https://github.com/android/views-widgets-samples/blob/2238cc873501f9cda63605051de11832bb736a8a/RecyclerViewKotlin/app/src/main/java/com/example/recyclersample/flowerList/FlowersAdapter.kt): useful anatomy; do not copy full-object identity/ID-only content comparison for mutable-content records.

### B6
[Network/database paging](https://developer.android.com/topic/libraries/architecture/paging/v3-network-db): one database truth path, remote keys, refresh/races; architecture, not mandatory Kotlin dependencies.

### B7
[LayoutManager](https://developer.android.com/reference/androidx/recyclerview/widget/RecyclerView.LayoutManager) and
[staggered grid](https://developer.android.com/reference/androidx/recyclerview/widget/StaggeredGridLayoutManager): recycler lifecycle, prefetch, span visibility and gap policy.

### B8
[Google Flexbox](https://github.com/google/flexbox-layout): archived; eager layout differs from recycling manager.

### B9
[Material carousel](https://github.com/material-components/material-components-android/blob/60ff09436d5d477a4b9d02940f31eb01e1250620/docs/components/Carousel.md): masking roots, first-item sizing, orientations and snapping, not 3D coverflow.

## Windows

### W1
[ItemsRepeater](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/items-repeater): low-policy host, key/index mapping, external scroll owner and ignored loading interfaces.

### W2
[ItemsView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/itemsview) and
[1.4 introductions](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-4): ItemContainer and fixed-height LinedFlowLayout.

### W3
[UI virtualization](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview) and
[data virtualization](https://learn.microsoft.com/en-us/windows/apps/develop/performance/listview-and-gridview-data-optimization): current WinUI panels, finite viewport, incremental/range loading.

### W4
[VirtualizingLayoutContext](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.virtualizinglayoutcontext?view=windows-app-sdk-1.8):
per-context state, realization, anchoring and recycling.

### W5
[General FlowLayout](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.flowlayout?view=windows-app-sdk-2.0-experimental): research found experimental API, not stable 1.8.

### W6
[Gallery layouts](https://github.com/microsoft/WinUI-Gallery/tree/d78c358505b4f42e1ade9f1b1b6d79abf415f998/WinUIGallery/Layouts): ActivityFeed/VariedImageSize samples have specialized row assumptions, linear lookup or broad invalidation to harden.

### P1
[WPF control performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls) and
[group virtualization](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.virtualizingpanel.isvirtualizingwhengrouping?view=windowsdesktop-10.0).

### P2
[VirtualizingWrapPanel](https://github.com/sbaeumlisberger/VirtualizingWrapPanel/tree/77a7d3a3c65ce02896f0aae131df3f0b5e136c3a): source-inspected candidate; metadata size provider recommended, publisher's throughput claim is not ours.

### P3
[WPF dispatcher](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model).

### P4
[WPF 3D](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/3-d-graphics-overview) and
[Viewport2DVisual3D](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.media3d.viewport2dvisual3d?view=windowsdesktop-10.0):
PerspectiveCamera and interactive 2D cards in native 3D; ordinary RenderTransform is not perspective.

### P5
[WPA](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-analyzer): native ETW/WPR evidence.

## GTK

### G1
[List framework](https://docs.gtk.org/gtk4/section-list-widget.html): model/factory/selection, direct scroll parenting and bounded native widgets.

### G2
[Factory lifecycle](https://docs.gtk.org/gtk4/class.SignalListItemFactory.html).

### G3
[ListView](https://docs.gtk.org/gtk4/class.ListView.html) and
[GridView](https://docs.gtk.org/gtk4/class.GridView.html): native orientations/roles.

### G4
[SectionModel](https://docs.gtk.org/gtk4/iface.SectionModel.html) and
[ListView headers](https://docs.gtk.org/gtk4/property.ListView.header-factory.html): since 4.12;
[grid source](https://github.com/GNOME/gtk/blob/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/gtk/gtkgridview.c) marks grid headers 4.26.

### G5
[FlowBox source](https://github.com/GNOME/gtk/blob/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/gtk/gtkflowbox.c): creates widgets for all added items; not viewport recycling.

### G6
[Native demos](https://github.com/GNOME/gtk/tree/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/demos/gtk-demo): Colors model still has large pointer/retention/sorting costs.

### G9
[Sysprof](https://developer.gnome.org/documentation/tools/sysprof.html).

## Data

### D1
[Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination): unique keyset order, indexes, offset and seek limits.
### D2
[Efficient queries](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying): projection/N+1/bounds.
### D3
[SQLite row values](https://www.sqlite.org/rowvalue.html): scrolling continuation.
### D4
[Sqlite async limits](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async): synchronous execution.
### D5
[WAL](https://www.sqlite.org/wal.html): concurrency, checkpoints and durability.
### D6
[DbContext ownership](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/): non-thread-safe contexts.
### D7
[HttpClient](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines): client/handler lifetime.

## maui-labs

### R1
Baseline `56064071bfeaae0dd9aa962b6079d39a5281b875`:
[AppKit collection](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers/CollectionViewHandler.cs)
is a custom ScrollView virtualizer, not NSCollectionView;
[GTK collection](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/CollectionViewHandler.cs)
recreates templates on bind and uses ListView, not GridView;
[WPF collection](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Windows.WPF/src/Windows.WPF/Handlers/CollectionViewHandler.cs)
uses eager WrapPanel for grid. Inspect current source before relying on those findings.

## Collection interactions

Checked 2026-10-13. These establish native/source capabilities, not new interaction
runtime certification. The existing masonry sample does not implement reorder,
swipe deletion, pull refresh or item context menus.

### I1
[MAUI drag/drop](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/gestures/drag-and-drop?view=net-maui-10.0):
recognizers/packages/platform args transfer data; not automatic CollectionView reorder.
Stock CollectionView has [ReorderableItemsView inheritance](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Items/CollectionView.cs)
and [CanReorderItems/CanMixGroups/ReorderCompleted](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Items/ReorderableItemsView.cs)
in MAUI 10.0.41; inspect handler/source limits before replacing it.
### I2
[MAUI SwipeView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/swipeview?view=net-maui-10.0):
Reveal/Execute, directional actions and Windows touch-only swipe. Separate .NET 11
IconColor/TextColor sections are not .NET 10 APIs.
### I3
[RefreshView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/refreshview?view=net-maui-10.0),
[10.0.41 source](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/RefreshView/RefreshView.cs):
IsRefreshing=true triggers Command; IsRefreshEnabled is distinct from IsEnabled.
### I4
[MAUI context menus](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/context-menu?view=net-maui-10.0),
[Windows handler](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Handlers/MenuFlyoutHandler/MenuFlyoutHandler.Windows.cs),
[UIKit handler](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Core/src/Handlers/MenuFlyoutHandler/MenuFlyoutHandler.iOS.cs):
Catalyst/Windows documented scope; dynamic add/remove implemented on Windows,
empty handler methods on UIKit. Blanket documentation warning is not uniform parity.
### I5
[MenuItem](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Menu/MenuItem.cs),
[SwipeItem](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/SwipeView/SwipeItem.cs):
framework Activate/OnInvoked already dispatch commands/events.
### I6
[UIKit drag/drop](https://developer.apple.com/documentation/uikit/supporting-drag-and-drop-in-collection-views),
[placeholder commit](https://developer.apple.com/documentation/uikit/uicollectionviewdropplaceholdercontext/commitinsertion(datasourceupdates:)):
delegates/providers/proposals; CommitInsertion supplies current path and performs
native insertion, false when placeholder is gone.
### I7
[Diffable reorder](https://developer.apple.com/documentation/uikit/uicollectionviewdiffabledatasourcereference/reorderinghandlers),
[Apple sample](https://developer.apple.com/documentation/uikit/implementing-modern-collection-views):
DidReorder final identifiers/difference update the backing store after native reorder.
Inspected ReorderableListViewController.swift in official archive; SHA-256
`26c83ac53bdb97a360e015a2d5a9a28f870b39fb46675aed299d0748bf2b0377`.
Sample license inspected; no source incorporated.
### I8
[List swipe provider](https://developer.apple.com/documentation/uikit/uicollectionlayoutlistconfiguration-c.class/trailingswipeactionsconfigurationprovider),
[full swipe](https://developer.apple.com/documentation/uikit/uiswipeactionsconfiguration/performsfirstactionwithfullswipe):
iOS/Catalyst 14 list provider, not universal masonry; full swipe defaults true.
### I9
[Context sample](https://developer.apple.com/documentation/uikit/adding-context-menus-in-your-app),
[multi/empty context](https://developer.apple.com/documentation/uikit/uicollectionviewdelegate/collectionview(_:contextmenuconfigurationforitemsat:point:)):
native UIKit menu APIs; multi-item/empty-space routing since 16 versus older 13 route.
### I10
[UIRefreshControl](https://developer.apple.com/documentation/uikit/uirefreshcontrol):
scroll-host target/action and EndRefreshing. Inspected installed Apple 26.5.10318
API XML for named UIKit list/context/reorder bindings; no new handler compile/runtime proof.
### I11
[ItemTouchHelper.Callback](https://developer.android.com/reference/androidx/recyclerview/widget/ItemTouchHelper.Callback):
move/swipe, handles, directions, draw/clear and edge/custom-manager drag visibility.
### I12
[Android transfer](https://developer.android.com/develop/ui/views/touch-and-input/drag-drop),
[multi-window permissions](https://developer.android.com/develop/ui/views/touch-and-input/drag-drop/multi-window):
ClipData/global/URI access since API 24+, separate from ItemTouchHelper.
### I13
[SwipeRefreshLayout API](https://developer.android.com/reference/androidx/swiperefreshlayout/widget/SwipeRefreshLayout),
[manual refresh alternative](https://developer.android.com/develop/ui/views/touch-and-input/swipe/add-swipe-interface),
[Paging example](https://github.com/android/architecture-components-samples/blob/e849ce3004ccd1132a121cf513bbcb7996d95c30/PagingWithNetworkSample/app/src/main/java/com/android/example/paging/pagingwithnetwork/reddit/ui/RedditActivity.kt):
one scrolling child, nested/child-up checks and refresh load-state presentation.
Native example dependencies are not managed package requirements.
### I14
[Android menus](https://developer.android.com/develop/ui/views/components/menus):
floating context, ActionMode and anchored overflow have distinct selection/UX roles.
### I15
[WinUI reorder](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.canreorderitems?view=windows-app-sdk-1.8),
[Gallery source](https://github.com/microsoft/WinUI-Gallery/blob/d78c358505b4f42e1ade9f1b1b6d79abf415f998/WinUIGallery/Samples/ListView/ListViewPage.xaml.cs):
AllowDrop/CanDragItems, grouped/VariableSizedWrapGrid exclusions. Sample first-cell
height and realized-container assumptions are not safe variable-geometry targeting.
### I16
[WinUI swipe](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/swipe):
Reveal/Execute, per-item SwipeControl and source mutation/close behavior.
### I17
[WinUI refresh](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/pull-to-refresh):
touch container, RequestRefresh and deferrals; timer demonstration is not repository success.
### I18
[WinUI data transfer](https://learn.microsoft.com/en-us/windows/apps/develop/data/drag-and-drop),
[menus](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/menus-and-context-menus),
[ItemsView](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemsview?view=windows-app-sdk-1.8):
UIElement events/data packages; ItemsView is not ListViewBase.
### I19
[AppKit delegate](https://developer.apple.com/documentation/appkit/nscollectionviewdelegate),
[validation](https://developer.apple.com/documentation/appkit/nscollectionviewdelegate/collectionview(_:validatedrop:proposedindexpath:dropoperation:)):
pasteboard registration, writer/validate/accept, modern section paths since 10.11.
### I20
[NSView menu(for:)](https://developer.apple.com/documentation/appkit/nsview/menu(for:)):
native event-sensitive NSMenu, not UIKit menu/swipe/refresh interchangeability.
### I21
[GTK drag/drop](https://docs.gtk.org/gtk4/drag-and-drop.html),
[drag-end](https://docs.gtk.org/gtk4/signal.DragSource.drag-end.html),
[native demo](https://github.com/GNOME/gtk/blob/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/demos/gtk-demo/dnd.c):
controllers/providers/typed versus async drop; Move delete_data ownership.
The widget canvas is not model-backed collection virtualization.
### I22
[GTK list framework](https://docs.gtk.org/gtk4/section-list-widget.html),
[edge overshot](https://docs.gtk.org/gtk4/signal.ScrolledWindow.edge-overshot.html),
[PopoverMenu](https://docs.gtk.org/gtk4/class.PopoverMenu.html):
item/position/factory ownership, boundary notification and native action/menu roles.
### I23
[WPF transfer](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview):
DoDragDrop/DataObject/routed events/effect negotiation, not automatic source mutation.
### I24
[WPF PlacementTarget](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.contextmenu.placementtarget?view=windowsdesktop-10.0):
popup owning context, distinct from row visual-tree ancestry.
[Popup overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/popup)
documents the separate native window used by ContextMenu.
### I25
[WCAG dragging](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html):
single-pointer non-drag alternatives are separate from keyboard support; web
guidance used as a native design principle, not a compliance verdict.
### I26
maui-labs baseline `56064071bfeaae0dd9aa962b6079d39a5281b875`:
[AppKit handlers](https://github.com/dotnet/maui-labs/tree/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers),
[GTK handlers](https://github.com/dotnet/maui-labs/tree/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers),
[GTK gesture bridge](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Platform/GtkGestureExtensions.cs),
[WPF refresh/swipe](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Windows.WPF/src/Windows.WPF/Handlers/CollectionHandlers.cs),
[WPF gestures](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Windows.WPF/src/Windows.WPF/Platform/GestureManager.cs).
Inspected current worktree too. Partial/substituted gestures and framework command
double-dispatch risks are source findings; no backend fixes/runtime proof claimed.
