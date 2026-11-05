---
name: maui-controls-deep-dive
description: >-
  Apply advanced MAUI control guidance. USE FOR: stock CollectionView loading, drag/drop and reorder commands, SwipeView deletion, RefreshView pull refresh, item context actions, EmptyView, AutomationId, .NET 10 SafeAreaEdges, GraphicsView, gestures and animations. DO NOT USE FOR: general layout, full accessibility, profiling, or bespoke native handler implementation.
---

# MAUI Controls Deep Dive

Use this skill when the user is already working with MAUI controls and needs
details beyond basic layout guidance. Keep control choices aligned with current
MAUI APIs.

## Response Checklist

- For list scenarios, prefer `CollectionView` with `EmptyView`,
  `RemainingItemsThreshold`, and stable `AutomationId` hooks.
- For edge-to-edge scenarios, call out `.NET 10` `SafeAreaEdges` values.
- For `GraphicsView`, keep `IDrawable.Draw` hot-path guidance and add semantic
  alternatives (`SemanticProperties` / accessible companion UI).

## Workflow

1. Inspect the target framework, resolved MAUI version, backend and UI style.
2. Identify the control area: `CollectionView`, safe area, gestures, animations,
   or `GraphicsView`.
3. Apply the focused guidance below. Route broad accessibility audits to
   `maui-accessibility`; for GraphicsView-specific semantics, apply
   `SemanticProperties` and `AutomationId` directly (see GraphicsView section).
   Route performance profiling to `maui-performance`.
   For stock collection interactions, read
   [drag, delete, refresh and actions](references/collection-interactions.md).
   Route custom native collection implementation to `maui-bespoke-collections`;
   do not replace a working CollectionView just to add ordinary commands.
4. Validate the control on the intended device sizes and platforms.

## CollectionView

Use `CollectionView` for repeated data and let it own scrolling:

- Do not wrap it in `ScrollView`.
- Use `EmptyView`, header/footer, and surrounding `Grid` rows for non-item UI.
- Use `RemainingItemsThreshold` / `RemainingItemsThresholdReachedCommand` for
  incremental loading.
- Use `SelectionMode`, `SelectedItem`, and `SelectionChangedCommand` instead of
  tap gestures on item roots when selection is the intent.
- Keep item templates lightweight. Avoid nested layouts and expensive converters
  in large lists.
- Prefer stable `AutomationId` values on the list and critical item controls.
- For .NET 10 iOS/Mac Catalyst behavior, check current handler guidance before
  opting into or reverting CollectionView handlers.

Example state shell:

```xml
<Grid RowDefinitions="Auto,*">
    <ActivityIndicator
        AutomationId="orders-loading"
        IsRunning="{Binding IsBusy}"
        IsVisible="{Binding IsBusy}" />

    <CollectionView
        Grid.Row="1"
        AutomationId="orders-list"
        ItemsSource="{Binding Orders}"
        RemainingItemsThreshold="5"
        RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}">
        <CollectionView.EmptyView>
            <Label
                AutomationId="orders-empty"
                Text="No orders found." />
        </CollectionView.EmptyView>
    </CollectionView>
</Grid>
```

### Collection interactions

Keep the scrolling CollectionView and use focused public controls/commands.
Inspect built-in CollectionView CanReorderItems/CanMixGroups and handler/source
limits first; a generic DragGestureRecognizer alone does not reorder ItemsSource.
SwipeView reveals/invokes actions; removing the intended model key is app logic.
RefreshView can wrap the finite collection directly; IsRefreshing=true can already
invoke Command, so avoid duplicate dispatch and use separate operation ownership.
FlyoutBase.ContextFlyout is documented for Catalyst/Windows, not every mobile
long-press. Preserve pointer/keyboard and visible action alternatives.

The [interaction reference](references/collection-interactions.md) covers
version/backend limits, compiled-template command context, async races and tests.

## Safe Areas

For .NET 10+ safe-area work, use `SafeAreaEdges` on the root container instead
of hard-coded iOS-only padding:

| Value | Effect |
|---|---|
| `SafeAreaEdges.Container` | Insets only the device bezel/notch boundary |
| `SafeAreaEdges.SoftInput` | Also avoids the on-screen keyboard |
| `SafeAreaEdges.All` | All edges including status bar and navigation areas |

```xml
<!-- XAML: apply on the outermost layout that should respect device edges -->
<Grid SafeAreaEdges="Container">
    ...
</Grid>
```

```csharp
// C# Markup
new Grid { SafeAreaEdges = SafeAreaEdges.Container }
```

Keep safe-area behavior declarative. Test with notches, rounded corners, desktop
title bars, and soft keyboard scenarios when the page uses edge-to-edge layout.
If the MAUI version is unknown, use `maui-current-apis` before changing APIs.

## Gestures

- Use `TapGestureRecognizer` for click/tap. Do not use removed or obsolete
  click gesture APIs.
- Use `PointerGestureRecognizer` for hover/desktop pointer affordances.
- Use `PanGestureRecognizer`, `SwipeGestureRecognizer`, and
  `PinchGestureRecognizer` only where the gesture maps to an obvious UI action.
- Do not attach competing gestures to deeply nested controls without testing
  hit-testing and scrolling interactions.
- Preserve accessibility alternatives: commands, buttons, menu items, keyboard
  accelerators, or semantic descriptions as appropriate.

## Animations

- Prefer built-in async animation helpers such as `FadeTo`, `ScaleTo`,
  `TranslateTo`, and `RotateTo` for view animations.
- Await or coordinate animations so state changes do not race.
- Cancel or ignore stale animations when view models unload or commands rerun.
- Avoid animation-only feedback for important state; expose semantic state and
  visual states too.
- Keep list item animations modest; animating many recycled cells can hurt
  scrolling performance.

## GraphicsView

Use `GraphicsView` for custom drawing, charts, signatures, or lightweight
visualizations:

- Put drawing code in an `IDrawable`.
- Treat `Draw(ICanvas canvas, RectF dirtyRect)` as a hot path: avoid allocations,
  blocking I/O, async calls, and service lookups.
- Store state outside the drawable and call `Invalidate()` when state changes.
- Handle pointer/touch input at the view or page layer and translate it to
  drawing state.
- For important information in the drawing, provide accessible alternatives
  outside the canvas: wrap the `GraphicsView` alongside a `Label` or use
  `SemanticProperties.Description` on the view so screen readers announce it.
  `AutomationId` on the `GraphicsView` enables UI-test targeting.
- Use `SemanticScreenReader.Announce` for dynamic changes that must be audible.
- If drawing stutters or allocates heavily, consult `maui-performance`.

## Validation Checklist

- `CollectionView` owns its scrolling and has empty/loading behavior where data
  can be absent.
- Safe-area code matches the detected MAUI version and target platforms.
- Gestures have accessible alternatives for critical actions.
- Animations cannot leave the UI in stale state after cancellation or navigation.
- `GraphicsView` drawing is allocation-conscious and exposes important content
  outside the canvas.
