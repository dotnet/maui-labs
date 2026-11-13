# Sources and evidence ledger

[Research index](README.md)

All entries were consulted on **2026-10-12**. This ledger records what each source
supports, not an endorsement of every implementation choice in it.
The separately dated [interaction ledger](interactions-sources.md) records
2026-10-13 research on drag/drop, deletion, refresh and context actions.

## Method and confidence

- **Documented:** native/vendor documentation or conference guidance establishes
  an API or behavior.
- **Source-inspected:** selected implementation files were read; conclusions refer
  to that revision, not every released package version.
- **Synthesis/proposal:** architecture, algorithms, default policies, and skill
  milestones derived from the evidence. They still require implementation tests.
- **Measured by the publisher:** a source contains measurements in its own
  workload. Those numbers are not transferable performance guarantees.
- **Not established here:** runtime behavior of newly generated C# bindings,
  device frame times, and a fastest cross-platform implementation.

GitHub examples below use inspected revisions. Vendor API pages are live and may
change. Apple pages that require JavaScript were read through their public DocC
JSON representations. Documentation monikers, release notes, source annotations,
and actual repository dependencies were checked rather than assuming that a
search result or current sample builds against the repo's baseline.

Third-party code was not incorporated into the repository. Before any later reuse
or binding work, review its actual license, maintenance, dependencies, and target
compatibility; public source and a popular example are not sufficient approval
to ship a dependency.

## MAUI

### M1

[Create custom controls with MAUI handlers](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/handlers/create?view=net-maui-10.0).
Supports the control/handler boundary, property and command mappers, platform
partials, registration, and connect/disconnect ownership. Does not prescribe a
collection adapter or a virtualization engine.

### M2

[CollectionView overview](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/?view=net-maui-10.0).
Documents default optimized UIKit handlers in .NET 10 and UI-thread collection
updates. The returned article also contains a **separate .NET 11 moniker** describing
a Windows `ItemsRepeater`-based handler. That is not the 10.0.41 Windows baseline.

### M3

[Compiled bindings](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/data-binding/compiled-bindings?view=net-maui-10.0).
Supports item-level `x:DataType`, binding-context scoping, and trimming/NativeAOT
requirements. Compiled binding does not remove native measurement or image costs.

### M4

Official MAUI source at tag **10.0.41**, commit
`d15964ef4f0a6d0740b655ba7c6be9b96be8b187`:

- [UIKit `TemplatedCell2`](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items2/iOS/TemplatedCell2.cs):
  template-aware reuse, binding-context changes, cached constraints,
  measure/arrange separation, and supplementary-cell sizing.
- [Android `ItemsViewAdapter`](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items/Android/Adapters/ItemsViewAdapter.cs):
  adapter/view-holder roles, view-type/template mapping, recycling and disposal.
  Its position-returning `GetItemId` is not a domain-key recipe for a new adapter
  that enables stable IDs.
- [Windows `ItemsViewHandler`](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/src/Controls/src/Core/Handlers/Items/ItemsViewHandler.Windows.cs):
  `ListViewBase` host, native collection source, templates, scroll events and
  cleanup.
- [Native package versions](https://github.com/dotnet/maui/blob/d15964ef4f0a6d0740b655ba7c6be9b96be8b187/eng/Versions.props):
  Windows App SDK `1.8.251106002`.

These files are prior art, not a promise that internal classes are a supported
extension surface.

### M5

[.NET 8 performance improvements in MAUI](https://devblogs.microsoft.com/dotnet/dotnet-8-performance-improvements-in-dotnet-maui/).
Publisher-measured examples include Android scrolling costs from Java interop,
batching drawable setters, string marshaling, and reducing a Windows
150,000-row collection's initially allocated item-context storage. Relevant
lesson: measure managed/native crossings and per-item metadata as well as views.
Historical optimization evidence, not current default build-property guidance.

## UIKit and related case studies

### U1

[WWDC 2019: Advances in Collection View Layout](https://developer.apple.com/videos/play/wwdc2019/215/).
UIKit **and AppKit** compositional layouts, item/group/section composition,
estimated sizing, supplementary/decoration views, and orthogonal sections.
Introductions: iOS 13 and macOS 10.15.

### U2

[WWDC 2020: Advances in UICollectionView](https://developer.apple.com/videos/play/wwdc2020/10097/).
iOS 14 section snapshots, list sections, cell registrations, and content/background
configuration. UIKit list configuration is not an AppKit API.

### U3

[WWDC 2021: Make blazing fast lists and collection views](https://developer.apple.com/videos/play/wwdc2021/10252/).
Stable item identifiers, registration lifetime, iOS 15 nonanimated diff behavior,
`reconfigureItems`, preparation versus display, prefetch lifecycle, asynchronous
content, image decoding, thumbnail preparation, and byte-aware caching.
Publisher demonstrations explain mechanisms; they do not establish our frame rates.

### U4

[Apple: Implementing modern collection views](https://developer.apple.com/documentation/uikit/implementing-modern-collection-views),
with the [official sample archive](https://docs-assets.developer.apple.com/published/76a6e639ef2e/ImplementingModernCollectionViews.zip).
The article and these archive files were inspected:

- UIKit `PinnedSectionHeaderFooterViewController.swift` and
  `OrthogonalScrollingViewController.swift`.
- AppKit `SectionHeadersFootersWindowController.swift` and
  `OrthogonalScrollingWindowController.swift`.

They demonstrate actual native supplementary views, distinct reusable identifiers,
native diffable sources, estimated header/footer sizes, and orthogonal sections.
The downloaded archive SHA-256 was
`26c83ac53bdb97a360e015a2d5a9a28f870b39fb46675aed299d0748bf2b0377`.
Its current project requirements include iOS/Catalyst 18 and Xcode 16; those are
**sample project requirements**, not the introduction versions of all its APIs.
The ZIP was used for reading only, not built or redistributed.

### U5

[UIKit `visibleItemsInvalidationHandler`](https://developer.apple.com/documentation/uikit/nscollectionlayoutsection/visibleitemsinvalidationhandler).
Documents a per-layout-cycle hook for modifying the section's visible items;
iOS 13 / Catalyst 13.1 availability. This is useful for bounded visible-item
effects, not for scanning or rebuilding all item geometry every frame.

### U6

[CHTCollectionViewWaterfallLayout source](https://github.com/chiahsien/CHTCollectionViewWaterfallLayout/blob/ca75bd8ae2ca50dd0c621fe1f521be805df44fd4/Source/CHTCollectionViewWaterfallLayout.m),
revision `ca75bd8ae2ca50dd0c621fe1f521be805df44fd4`.
Actual shortest-column placement, aspect-ratio sizing, section headers/footers,
attribute caches, width-change invalidation, and union rectangles of 20 attributes.
Source inspection also shows linear scans of union rectangles and full geometry
rebuild in `prepareLayout`; this is not an O(log N) large-dataset implementation.
Repository metadata reported not archived; latest inspected commit 2026-03-18.

### U7

[IGListKit best practices and FAQ](https://instagram.github.io/IGListKit/best-practices-and-faq.html),
with [repository revision](https://github.com/Instagram/IGListKit/tree/23650fad95a99fa89bbe763250b75770dcd43bcf)
`23650fad95a99fa89bbe763250b75770dcd43bcf`.
Supports stable unique diff IDs, section-controller composition, initial large-data
reload versus ongoing diffs, and the distinction between controller ownership
and native cell reuse. Its FAQ explicitly limits first-class estimated Auto Layout
support. Not archived at inspection; does not imply a compatible .NET binding.

### U8

[Texture introduction](https://texturegroup.org/docs/getting-started.html) and
[`ASCollectionNode`](https://texturegroup.org/docs/containers-ascollectionnode.html).
Documents thread-safe nodes, asynchronous layout/drawing, state/range management,
and an internal `UICollectionView` subclass. Crucially, the documented collection
nodes **do not use cell reuse** and ordinary UIKit cells do not gain node-specific
async benefits merely by being hosted there.
[Repository revision](https://github.com/TextureGroup/Texture/tree/d8aa8c796dd7770ea177989253ef1cf8878cd50a):
`d8aa8c796dd7770ea177989253ef1cf8878cd50a`; not archived at inspection.
An alternative architecture to study, not a drop-in `Task.Run` optimization for
MAUI `View` trees.

### U9

[Historical iCarousel README](https://github.com/nicklockwood/iCarousel/blob/c9e043e1fa767f88d31de8dae95585345e8e7676/README.md)
and [implementation](https://github.com/nicklockwood/iCarousel/blob/c9e043e1fa767f88d31de8dae95585345e8e7676/iCarousel/iCarousel.m).
Revision `c9e043e1fa767f88d31de8dae95585345e8e7676`, dated 2017-02-17.
Native UIKit/AppKit views, view pools, bounded visible-item calculations, perspective
and 3D coverflow transforms were inspected. Repository reported **archived**.
Useful historical mechanism reference; not a maintained dependency recommendation
or evidence of current OS, accessibility, or .NET compatibility.

## AppKit

### A1

[AppKit `NSCollectionView`](https://developer.apple.com/documentation/appkit/nscollectionview).
Documents native item view controllers, modern data-source-based sections,
supplementary/decoration views, custom layouts, and item recycling. Prefer this
modern path over the old content-array/prototype collection architecture.

### A2

[`NSCollectionViewCompositionalLayout`](https://developer.apple.com/documentation/appkit/nscollectionviewcompositionallayout).
DocC metadata explicitly gives macOS **10.15** introduction. Shares layout concepts
with UIKit, but the top-level view/controller and API surfaces are AppKit.

### A3

[`NSCollectionViewPrefetching`](https://developer.apple.com/documentation/appkit/nscollectionviewprefetching).
Documents native AppKit prefetch/cancellation callbacks and `prefetchDataSource`.
Availability and binding support must still be checked against the consuming SDK.

### A4

[dotnet/macios AppKit bindings](https://github.com/dotnet/macios/blob/354c2a645855dfdf774cc5e098cd214764f475b5/src/appkit.cs),
revision `354c2a645855dfdf774cc5e098cd214764f475b5`, dated 2026-10-05.
Inspected `NSCollectionView.PrefetchDataSource`, `INSCollectionViewPrefetching`,
`NSCollectionViewCompositionalLayout`, and
`NSCollectionViewDiffableDataSource<SectionIdentifierType, ItemIdentifierType>`.
The latter's C# identifier types are constrained to `NSObject`.
The inspected `NSCollectionViewLayoutAttributes` surface exposes frame, size,
alpha and z-index, **not UIKit's `Transform3D` property**.
This is upstream source evidence, not a compile check of the repo's installed pack.

## Android

### B1

[Create dynamic lists with RecyclerView](https://developer.android.com/develop/ui/views/layout/recyclerview).
Native adapter, view-holder, layout-manager responsibilities and built-in linear,
grid and staggered geometry.

### B2

[Slow rendering / rendering performance](https://developer.android.com/topic/performance/issues/render).
Official trace-guided guidance on full invalidation, minimal diffs, nested recycler
pools, initial prefetch, shallow item trees, cheap binds, draw-property animations,
thumbnail sizing and texture uploads. Describes Perfetto FrameTimeline and
refresh-rate-dependent deadlines. Some examples retain older API terminology;
use current tracing tools and installed APIs.

### B3

[`DiffUtil`](https://developer.android.com/reference/androidx/recyclerview/widget/DiffUtil).
Immutable diff inputs, background diff calculation/main-thread publication,
`ListAdapter`/`AsyncListDiffer`, O(N + D^2) expected diff cost, and extra move-detection
cost. Historical timings on a Nexus 5X are not modern .NET MAUI benchmark results.

### B4

[`ConcatAdapter`](https://developer.android.com/reference/androidx/recyclerview/widget/ConcatAdapter)
and [its config](https://developer.android.com/reference/androidx/recyclerview/widget/ConcatAdapter.Config).
Adapter composition, binding positions, view-type isolation and compatibility of
shared holder types. Stable-ID policy must be configured deliberately rather than
assuming a child adapter's IDs automatically become global IDs.

### B5

Official [RecyclerView Kotlin sample](https://github.com/android/views-widgets-samples/tree/2238cc873501f9cda63605051de11832bb736a8a/RecyclerViewKotlin),
revision `2238cc873501f9cda63605051de11832bb736a8a`.
Inspected `flowerList/FlowersAdapter.kt` and `FlowersListActivity.kt`: holder-local
current item, `ListAdapter`, and `ConcatAdapter` for a header plus content.
**Caveat:** its diff callback compares full object equality for identity and only
ID equality for content. That is not the desired stable-ID/content-change
comparison for mutable-content records. Use B3's contract instead of copying it.

### B6

[Paging overview](https://developer.android.com/topic/libraries/architecture/paging/v3-overview)
and [network + database / RemoteMediator](https://developer.android.com/topic/libraries/architecture/paging/v3-network-db).
Request deduplication, load states, repository boundaries, refresh/append/prepend,
database source of truth, invalidation, remote keys and race handling.
These are Kotlin/Android APIs; the current overview emphasizes Compose. Reusing
the architecture does not require introducing Kotlin Flow, Room, or Compose into
a .NET MAUI RecyclerView handler.

### B7

[`RecyclerView.LayoutManager`](https://developer.android.com/reference/androidx/recyclerview/widget/RecyclerView.LayoutManager).
Recycler-owned view acquisition, scrap/recycle lifecycle, layout and scroll
contracts, initial/adjacent prefetch, accessibility and predictive-animation duties.

### B8

[`StaggeredGridLayoutManager`](https://developer.android.com/reference/androidx/recyclerview/widget/StaggeredGridLayoutManager).
Per-span visible positions and gap-handling behavior. Interpret visibility across
all spans and allow for `NO_POSITION`; staggered layout is not a promise of
cross-platform deterministic placement.

### B9

[Google Flexbox README](https://github.com/google/flexbox-layout/blob/366b461fd0425bfaa9872b1d9af9da3055d3bc74/README.md),
revision `366b461fd0425bfaa9872b1d9af9da3055d3bc74`.
Differentiates eager `FlexboxLayout` from recycling `FlexboxLayoutManager`;
documents unsupported manager features such as wrap-reverse.
GitHub repository metadata reported **archived**. It is a useful reference or a
deliberately reviewed dependency/fork, not an automatic default for new controls.

### B10

[Material carousel guidance](https://github.com/material-components/material-components-android/blob/60ff09436d5d477a4b9d02940f31eb01e1250620/docs/components/Carousel.md)
and [`CarouselLayoutManager`](https://github.com/material-components/material-components-android/blob/60ff09436d5d477a4b9d02940f31eb01e1250620/lib/java/com/google/android/material/carousel/CarouselLayoutManager.java),
revision `60ff09436d5d477a4b9d02940f31eb01e1250620`.
Verified horizontal/vertical strategies, masking/keylines, `MaskableFrameLayout`
roots, first-item sizing, snapping, fill bookkeeping, and resize listener cleanup.
Not a native 3D coverflow engine or a heterogeneous variable-width wrap layout.

### B11

[`RecyclerView.setHasFixedSize`](https://developer.android.com/reference/androidx/recyclerview/widget/RecyclerView#setHasFixedSize(boolean)).
The corresponding AndroidX source method was inspected: the flag concerns the
**RecyclerView's own size being independent of adapter content**, not identical
item heights. A fixed viewport can contain variable-height rows.

## Windows / WinUI

### W1

[ItemsRepeater](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/items-repeater).
Virtualizing, low-policy collection primitive; external scroll owner; indexed
sources, `IKeyIndexMapping`, element lifecycle, and lack of built-in selection.
Explicitly says `ISupportIncrementalLoading`, `IItemsRangeInfo` and `ISelectionInfo`
have no effect on `ItemsRepeater`.

### W2

[ItemsView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/itemsview).
Repeater/scroll-view/item-container composition, selection and interaction,
swappable layouts, fixed-height/variable-width `LinedFlowLayout`, and the
`ItemContainer` item-template root requirement. It is not MAUI's `ItemsView`.

### W3

[Windows App SDK 1.4 release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-4).
Establishes introduction of `ItemsView`, `ItemContainer`, `LinedFlowLayout`,
transition providers and `VisibleRect`. This is a minimum-introduction reference,
not a recommendation to downgrade to 1.4.

### W4

[Optimize ListView and GridView performance](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview).
Current WinUI guidance: finite viewport, virtualizing `ItemsStackPanel` /
`ItemsWrapGrid`, nonvirtualizing `VariableSizedWrapGrid`/`WrapGrid`/`StackPanel`,
element reduction, placeholders and progressive native item rendering.

### W5

[ListView and GridView data virtualization](https://learn.microsoft.com/en-us/windows/apps/develop/performance/listview-and-gridview-data-optimization).
Current Windows App SDK documentation for incremental loading, random-access
placeholders, range-informed caches and asynchronous data access. Distinguishes
loaded count from total count and warns about cache ownership across multiple views.

### W6

[`VirtualizingLayoutContext`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.virtualizinglayoutcontext?view=windows-app-sdk-1.8).
Realization rectangle, per-context layout state, recommended anchor index,
element acquisition/recycling, layout origin and extent responsibilities.

### W7

[`FlowLayout`](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.flowlayout?view=windows-app-sdk-2.0-experimental).
The attempted 1.8 documentation URL redirected to **2.0-experimental**.
The API is marked `Experimental` and derives from `VirtualizingLayout`.
Do not confuse this general wrap layout with stable `LinedFlowLayout`.

### W8

[WinUI Gallery](https://github.com/microsoft/WinUI-Gallery/tree/d78c358505b4f42e1ade9f1b1b6d79abf415f998),
revision `d78c358505b4f42e1ade9f1b1b6d79abf415f998`.
Inspected:

- `Samples/ItemsRepeater/ItemsRepeaterPage.xaml.cs`: layout selection, element
  prepared/clearing counters, `IKeyIndexMapping` source, and Composition effects.
- `Samples/ItemsView/ItemsViewPage.xaml`: real `ItemContainer` templates and
  `LinedFlowLayout`/stack/grid selection.
- `Layouts/ActivityFeedLayout.cs`: realization-window row arithmetic and
  `context.LayoutState`; its three-item rows are a specialized sample assumption.
- `Layouts/VariedImageSizeLayout.cs`: shortest-column masonry, cached bounds,
  incremental measurement, **linear start-index lookup**, and broad invalidation.
- `WinUIGallery.csproj`: experimental API warning suppression exists; a Gallery
  example is not proof that an API is in the app's stable SDK.

These are useful implementation examples, not complete arbitrary-count,
multi-context, anchored production layouts.

## WPF and Windows profiling

### P1

[Optimize WPF control performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls).
UI versus data virtualization, `VirtualizingStackPanel`, recycling, state leaking
across containers, template complexity and deferred scrolling.

### P2

[`VirtualizingPanel.IsVirtualizingWhenGrouping`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.virtualizingpanel.isvirtualizingwhengrouping?view=windowsdesktop-10.0).
Grouping has an explicit virtualization setting; group templates and panel
hierarchies must preserve the scrolling/virtualizing contract.

### P3

[VirtualizingWrapPanel](https://github.com/sbaeumlisberger/VirtualizingWrapPanel/tree/77a7d3a3c65ce02896f0aae131df3f0b5e136c3a)
README and `src/VirtualizingWrapPanel/VirtualizingWrapPanel.cs`,
revision `77a7d3a3c65ce02896f0aae131df3f0b5e136c3a`, dated 2026-07-19.
Not archived at inspection. Actual configurable caches, recycling/grouping,
size-provider, viewport/extent and realization work were inspected.
The README's million-item claim is **the publisher's claim**, not our benchmark.
Variable-size documentation recommends `ItemSizeProvider` and notes correctness
limitations without it. This is a wrap candidate, not a built-in WPF control or
automatically a masonry solution.

### P4

[WPF threading model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model).
Dispatcher affinity, short UI work items, asynchronous publication, and deadlock
risks from synchronous task waits.

### P5

[Windows Performance Analyzer](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-analyzer).
WPR/ETW captures and WPA analysis. Combine native traces with .NET allocation and
GC evidence rather than substituting a visual screenshot for frame measurements.

### P6

[WPF 3D overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/3-d-graphics-overview)
and [Viewport2DVisual3D](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.media3d.viewport2dvisual3d?view=windowsdesktop-10.0).
PerspectiveCamera/Viewport3D provide native projection and Viewport2DVisual3D
hosts interactive 2D content in 3D. Ordinary RenderTransform is not perspective
and the 3D host does not implement collection virtualization itself.

## GTK4 / Linux

### G1

[GTK list widget overview](https://docs.gtk.org/gtk4/section-list-widget.html).
Models/factories/selection, bounded native item realization, direct
`GtkScrolledWindow` parenting, identity versus position, custom list models,
bulk notifications and data-structure tradeoffs.

### G2

[`GtkSignalListItemFactory`](https://docs.gtk.org/gtk4/class.SignalListItemFactory.html).
Exact setup/bind/unbind/teardown responsibilities and repeated binding of the same
native list item. Essential to distinguishing container recycling from child reuse.

### G3

[`GtkListView`](https://docs.gtk.org/gtk4/class.ListView.html) and
[`GtkGridView`](https://docs.gtk.org/gtk4/class.GridView.html).
Native linear/grid virtualization, horizontal/vertical orientation, selection
and accessibility roles. The existing repo comment that ListView is always vertical
is not an authoritative native capability description.

### G4

[`GtkSectionModel`](https://docs.gtk.org/gtk4/iface.SectionModel.html) and
[`GtkListView:header-factory`](https://docs.gtk.org/gtk4/property.ListView.header-factory.html).
Raw API metadata inspected: both are available since **4.12**. Header factories
configure `GtkListHeader`, not ordinary `GtkListItem`.

### G5

[`gtkgridview.c`](https://github.com/GNOME/gtk/blob/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/gtk/gtkgridview.c),
revision `f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d`.
Its `header-factory` property and get/set functions are explicitly marked
**Since: 4.26**. The published API pages identify library version **4.24.1** and
the published grid header-factory page did not exist at inspection.
The generic SectionModel prose mentions GridView, but that does not make grid
headers available on a 4.12/4.24 deployment or in GirCore 0.7.0.

### G6

[`GtkFlowBox`](https://docs.gtk.org/gtk4/class.FlowBox.html) and
[its model-binding implementation](https://github.com/GNOME/gtk/blob/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/gtk/gtkflowbox.c).
Source `gtk_flow_box_bound_model_changed` creates and inserts a widget for every
added model item. Model binding is not viewport recycling.

### G7

Official GTK demos at
[`f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d`](https://github.com/GNOME/gtk/tree/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/demos/gtk-demo):

- `listview_colors.c`: real `GtkGridView`, property-expression binding, a custom
  lazy-object `GListModel`, and change notifications. Its 16-million-item model
  still allocates a large pointer table and the source warns that sorting the
  largest datasets can stall.
- `listview_settings.c`: native tree flattening/selection plus sort/filter models
  and `GtkColumnView` composition.

The demos establish mechanisms, not constant-memory data access or our throughput.

### G8

[GTK NEWS](https://github.com/GNOME/gtk/blob/f7f8a1ff05bbfa0aad3fe453e3e13cde8a40710d/NEWS).
4.24.1/4.24 release context, recent list correctness/frame-clock work and a
frametime overlay in the preceding development series. Toolkit version and
renderer must be recorded in performance comparisons; current main is not the
same as deployed stable GTK.

### G9

[GNOME Sysprof](https://developer.gnome.org/documentation/tools/sysprof.html).
Native/system callgraph and main-loop delay investigation. Pair with managed
allocation/GC profiling and GTK frame information.

## Data access

### D1

[EF Core pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination).
Fully unique ordering, indexed keyset pagination, offset costs and mutation
anomalies, composite sort keys and random-access tradeoffs.

### D2

[EF Core efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying).
Projection, bounded results, query plans, indexing, N+1 lazy loading and read-only
query considerations. ORM selection is not dictated by the collection control.

### D3

[SQLite row values / scrolling window queries](https://www.sqlite.org/rowvalue.html).
Primary explanation of offset work and indexed continuation comparisons.
Application queries still need unique tie-breakers and consistent collations.

### D4

[Microsoft.Data.Sqlite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).
Async ADO.NET methods execute synchronously in this provider. An `Async` method
name is not proof that a query can safely run on the UI thread.

### D5

[SQLite write-ahead logging](https://www.sqlite.org/wal.html).
Reader/writer concurrency, one writer at a time, checkpointing, long-lived read
transaction effects and durability tradeoffs. WAL is not a nonblocking UI API.

### D6

[DbContext lifetime and threading](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/).
Short unit-of-work ownership, explicit disposal, factory-based creation and
non-thread-safe context use. Parallel page loads cannot share a context
indiscriminately.

### D7

[HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines).
Connection/client lifetime and factory choices. Handler-specific behavior must
be checked on mobile; a desktop `SocketsHttpHandler` tuning recipe is not universal.

## maui-labs source observations

All R entries refer to baseline
`56064071bfeaae0dd9aa962b6079d39a5281b875`.

### R1

Collection handlers:

- [AppKit](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers/CollectionViewHandler.cs#L44-L66):
  custom virtualization state; [visibility/reuse](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers/CollectionViewHandler.cs#L322-L546);
  [full reload path](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/MacOS/src/MacOS/Handlers/CollectionViewHandler.cs#L982-L1131).
- [GTK creation/factory](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/CollectionViewHandler.cs#L70-L248):
  native list virtualization and template recreation;
  [measure/lifecycle](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/CollectionViewHandler.cs#L298-L406);
  [reload and layout mappers](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/CollectionViewHandler.cs#L605-L760).
- [WPF layout selection](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Windows.WPF/src/Windows.WPF/Handlers/CollectionViewHandler.cs#L476-L509)
  and [container/template lifecycle](https://github.com/dotnet/maui-labs/blob/56064071bfeaae0dd9aa962b6079d39a5281b875/platforms/Windows.WPF/src/Windows.WPF/Handlers/CollectionViewHandler.cs#L595-L733).

### R2

[Root SDK](../../global.json),
[MAUI/product versions](../../eng/Versions.props), and
[GTK project dependencies](../../platforms/Linux.Gtk4/src/Linux.Gtk4/Linux.Gtk4.csproj).
Inspected baseline: .NET SDK 10.0.401, MAUI 10.0.41, GirCore GTK package 0.7.0.
These are evidence for this research, not permanent version requirements.

### R3

[Skill contribution guide](../../plugins/CONTRIBUTING.md),
[existing performance skill](../../plugins/dotnet-maui/skills/maui-performance/SKILL.md),
[controls guidance](../../plugins/dotnet-maui/skills/maui-controls-deep-dive/SKILL.md),
and [plugin manifest](../../plugins/dotnet-maui/plugin.json).
Establish skill placement, progressive references, body size, routing boundaries
and evaluation structure. Evaluation model choices must follow the executing
user's policies rather than copying the guide's example selections blindly.

### R4

Backend integration surfaces:
[AppKit base](../../platforms/MacOS/src/MacOS/Handlers/MacOSViewHandler.cs),
[AppKit dispatcher](../../platforms/MacOS/src/MacOS/Platform/MacOSDispatcher.cs),
[GTK base](../../platforms/Linux.Gtk4/src/Linux.Gtk4/Handlers/GtkViewHandler.cs),
[GTK dispatcher](../../platforms/Linux.Gtk4/src/Linux.Gtk4/Platform/GtkDispatcherProvider.cs),
and [WPF base](../../platforms/Windows.WPF/src/Windows.WPF/Handlers/ViewHandlerOfT.Windows.cs).
Source confirms backend-specific geometry, mapper and dispatcher integration;
do not assume stock platform implementations for these targets.

### R5

[AppKit runtime test host](../../platforms/MacOS/tests/MacOS.RuntimeTests/README.md)
and [GTK backend runtime guidance](../../platforms/Linux.Gtk4/README.md).
Provide existing native-test surfaces, collection resize/grid examples and
explicit separation of portable tests, headless native checks and real rendering.
No existing test was run as part of this documentation-only research.
