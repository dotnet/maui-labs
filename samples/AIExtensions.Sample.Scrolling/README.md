# ScrollAnchor

Keeps the end of a `CollectionView` or `ScrollView` in view while its content changes: new entries in a log, a chat
answer that streams in, a keyboard that shrinks the viewport. When the user scrolls away to read, it stops following,
and it follows again once they scroll back to the end.

This is a sample library, not a package. It is written as a self-contained, MAUI-shaped feature so that it can be
proposed to dotnet/maui and copied there; see [Toward dotnet/maui](#toward-dotnetmaui).

## Usage

```xml
<ContentPage xmlns:scroll="clr-namespace:AIExtensions.Sample.Scrolling;assembly=AIExtensions.Sample.Scrolling">
    <CollectionView ItemsSource="{Binding Entries}" scroll:ScrollAnchor.Mode="End" />
</ContentPage>
```

```csharp
ScrollAnchor.SetMode(log, ScrollAnchorMode.End);

// Offer a "jump to latest" button while the user reads history.
log.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == ScrollAnchor.IsAnchoredProperty.PropertyName)
        jumpToLatest.IsVisible = !ScrollAnchor.GetIsAnchored(log);
};
jumpToLatest.Clicked += (_, _) => ScrollAnchor.ScrollToEnd(log);
```

It works on any `StructuredItemsView` (`CollectionView`, grouped or not, with or without a header and footer) and on a
vertical `ScrollView`.

## API

| Member | Description |
| --- | --- |
| `ScrollAnchorMode` | `None = 0` (the default) or `End = 2`. A `[Flags]` enum with Blazor's values, leaving `Start = 1` for feeds that prepend. |
| `ScrollAnchor.ModeProperty`, `GetMode`, `SetMode` | Attached: which edge of the content to keep in view. |
| `ScrollAnchor.IsAnchoredProperty`, `GetIsAnchored` | Read-only attached: whether the view follows its end right now. `false` wherever it can't: before the view has a handler, while it scrolls horizontally, and on platforms without an adapter. |
| `ScrollAnchor.ScrollToEnd(view, animate = true)` | Scrolls to the end, and follows it again. |

Why this shape:

- **Attached properties**, not a `CollectionView` subclass, so it applies to the lists and scroll views an app already
  has. In dotnet/maui it would become a regular bindable property on `StructuredItemsView` and `ScrollView`.
- **`ScrollAnchorMode`**, not the proposal's `ItemsAnchorMode`, because it applies to `ScrollView` too. The values mirror
  Blazor's `VirtualizeAnchorMode` (.NET 11).
- **`IsAnchored` is read-only** (a `BindablePropertyKey`). The anchor engages by being at the end, which
  `ScrollToEnd` gets the view to.

## Behavior of `End`

- It starts engaged, so a view shows its end when it first appears.
- While engaged, it keeps the end in view through items appended or removed, any item growing or shrinking (including
  the last one while text streams into it), header and footer changes, and viewport resizes (keyboard, rotation, window
  resize). Following never animates.
- A user scroll (drag, fling, mouse wheel or trackpad) disengages it once the user has moved the viewport more than 8
  device-independent units away from the end. Content growing during the gesture doesn't count as moving away, and
  neither does a bounce springing back from past the end.
- Offset changes the platform makes while laying out (re-measuring rows, clamping after items are removed) never
  disengage it. An offset change the user isn't making, with no size change at all, that moves away from the end is a
  deliberate scroll (the keyboard, accessibility, code moving the native view) and disengages it.
- A `ScrollTo` the app requests is judged by where it lands: engaged only if that's the end.
- Being at the end engages it again, however the view got there: the user, `ScrollTo`, `ScrollToEnd`, or content that
  shrank to fit.

## How it works

`ScrollAnchorTracker` is the platform-agnostic state machine. Its inputs are the offset, the content's extent and the
viewport, in device-independent units, plus whether the user is scrolling and whether the platform reported a size
change. Its outputs are whether it's anchored and the offset to scroll to. `ScrollAnchorController` connects a tracker to
a view: it creates the platform adapter when the view gets a handler (`HandlerChanged`), disposes it when the handler
goes (`HandlerChanging`), creates or disposes it when a new `ItemsLayout` or `Orientation` starts or stops the view
scrolling vertically, and notes the app's `ScrollTo` requests (`ScrollToRequested`). The adapters only read the
native view's own metrics and input state, and scroll it.

| | iOS and Mac Catalyst | Android `CollectionView` | Android `ScrollView` |
| --- | --- | --- | --- |
| Native view | `UICollectionView` or `UIScrollView` (the handler's `PlatformView`) | `RecyclerView` | `NestedScrollView` |
| Metrics | `contentOffset`, `contentSize`, `adjustedContentInset`, `bounds` | the laid-out rows' positions | `ScrollY`, the content's height and margins, padding |
| User scrolling | `isDragging` or `isDecelerating`, with the pan gesture recognizer reporting drags | `ScrollState` isn't idle: dragging or settling | not available (see [Limitations](#limitations)) |
| When it looks | key-value observing of those properties, coalesced into one look per main-queue turn | a pre-draw listener: once per frame, after layout | the same |

On Android, the offset and the distance to the end are measured from the laid-out rows. `RecyclerView`'s own scroll
offset and range are averages of the visible rows' heights, which jump as rows of other sizes scroll in.

It adds nothing to the app's items, header, or footer, uses no timers or delays, and doesn't reflect into MAUI. It
leaves the native views' delegates and listeners to MAUI's handlers: it observes through key-value observing, a gesture
recognizer target, and the view tree observer.

Everywhere else (Windows, and the `net10.0` build), setting the mode has no effect and `IsAnchored` stays `false`.

## Limitations

- **Vertical only:** horizontal lists and `CarouselView` aren't supported, and `Start` isn't implemented.
- **Android `ScrollView`:** `NestedScrollView` keeps its drag and fling state private, and MAUI's gesture handling owns
  the view's touch listener. So a scroll counts as the user's only when it moves away from the end without a size
  change; a drag in the same frame as content growth is pulled back to the end until it is past the threshold.
- **End of a fling or drag:** without the scroll view's delegate (MAUI's) on iOS, nothing reports that a deceleration
  ended. If its last frame doesn't move the offset, a pending snap to the end waits for the next change. On Android, a
  drag that ends without a fling may not draw another frame, with the same effect.
- **Code that moves the native view directly** (not through `ScrollTo`) with an animation: while rows self-size during
  the animation, it can be pulled back to the end until it has moved past the threshold.
- The threshold (8 device-independent units) isn't configurable.
- **Windows** isn't implemented, and is unverified. An adapter would use `ScrollViewer`'s offset, extent and viewport,
  its manipulation events for user scrolls, and `ChangeView`.

## Toward dotnet/maui

Notes for the CollectionView anchoring proposal:

- Inside MAUI, the adapters can use the exact signals the handlers own: the scroll view delegate's dragging and
  deceleration callbacks on iOS, and MAUI's `RecyclerViewScrollListener` on Android. From outside, an
  `OnScrollListener` isn't an option: `MauiRecyclerView` calls `ClearOnScrollListeners()` whenever it updates its own.
- Measuring the content's extent, rather than tracking the last item, is what makes grouped lists, headers, footers,
  and growing items work the same way.
- The threshold is an open question: Blazor uses 2 pixels; 8 device-independent units here absorbs rounding and inset
  differences across platforms.
- A requested `ScrollTo` should be judged by where it lands, not fought.
- `ItemsUpdatingScrollMode.KeepLastItemInView` could become `End` without its "scrolled away" state, or stay as it is.

## Tests

`tests/AIExtensions/AIExtensions.Sample.Scrolling.Tests` compiles the platform-agnostic sources for `net10.0` and tests
the tracker's rules and the attached properties' lifecycle, with a fake handler and adapter:

```bash
dotnet test tests/AIExtensions/AIExtensions.Sample.Scrolling.Tests
```

The library's platform builds need the MAUI workloads, so CI builds them through the chat playground, which references
this project.

## Try it

The chat playground's AI diagnostics sidebar (`samples/AIExtensions.Sample.ChatPlayground/Views/ChatLogsView.xaml`), a
grouped `CollectionView`, anchors to its end. The playground also has DevFlow actions that need no model or network:

| Action | Does |
| --- | --- |
| `scroll-anchor-logs` (`count`, `traces`) | Writes entries to the sidebar, spread over new traces (one group each). |
| `scroll-anchor-log-stream` (`count`, `intervalMilliseconds`) | Writes one entry every interval, in the background. |
| `scroll-anchor-demo` | Opens a page with an anchored grouped `CollectionView` and `ScrollView`, and buttons to add, grow, stream into, remove, and clear their content. |
| `scroll-anchor-state` | Reports whether each anchored view follows its end. |
