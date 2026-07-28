---
name: maui-ui-patterns
description: >-
  Fix .NET MAUI UI layout, state, and automation issues: CollectionView not scrolling, DataTemplate binding problems, command binding back to page view models, Grid/FlexLayout responsive sizing, and binding-driven states. USE FOR: Auto vs Star Grid rows, avoiding ScrollView parents around CollectionView, rejecting HeightRequest fixes, replacing BindableLayout for large lists, stable AutomationIds, x:DataType item templates, RelativeSource AncestorType command bindings, EmptyView/loading/error states, responsive resources, and UI property bindings. DO NOT USE FOR: Shell routes, full accessibility audits, or vendor-specific controls.
---

# MAUI UI Patterns

Use this skill when creating or reshaping a MAUI page or reusable component.
Prefer readable layouts, shared resources, stable automation hooks, and explicit
UI states over coordinate-based or screenshot-only UI.

## Workflow

1. Inspect the existing UI style: XAML, C# Markup, MauiReactor, or Blazor Hybrid.
   If the requested page/component cannot be found but the user asked for a UI
   pattern, still provide a self-contained snippet or create the requested file
   at a sensible path instead of stopping with only a clarification request.
2. Choose layout primitives based on content:
   - `Grid` for structured forms and dashboards.
   - `FlexLayout` for wrapping content and responsive chip/card layouts.
   - `VerticalStackLayout` / `HorizontalStackLayout` for simple linear groups.
   - `CollectionView` for repeated data; avoid wrapping it in `ScrollView`.
3. Move repeated colors, spacing, and text styles into resources.
4. Add `AutomationId` to important interactive elements.
5. Add loading, empty, error, and success states for data-driven screens.
   For `CollectionView` loading/empty-state requests, show a visible loading
   element such as `ActivityIndicator`, a `CollectionView.EmptyView`, and stable
   `AutomationId` values for loading/list/empty elements.
6. Add basic accessibility hooks while building the UI; route deeper audits to
   `maui-accessibility`.
7. Verify with build plus DevFlow tree/screenshot when available.

## Layout Guardrails

| Avoid | Prefer |
| --- | --- |
| Absolute coordinates for normal app UI | `Grid`, `FlexLayout`, and adaptive resources |
| Nested `ScrollView` around `CollectionView` | `CollectionView` scrolling directly |
| Fixed `HeightRequest` to force list scrolling | Star-sized `Grid` row so the list receives remaining space |
| Repeated inline colors/margins everywhere | `ResourceDictionary` styles and spacing resources |
| Text-only UI automation | Stable `AutomationId` values |
| One giant page without states | Loading, empty, error, and content states |
| `BindableLayout` for long or incremental lists | `CollectionView` virtualization and incremental loading |

## Example UI State Pattern

```xml
<Grid RowDefinitions="Auto,*">
    <ActivityIndicator
        AutomationId="products-loading"
        IsRunning="{Binding IsBusy}"
        IsVisible="{Binding IsBusy}" />

    <CollectionView
        Grid.Row="1"
        AutomationId="products-list"
        ItemsSource="{Binding Products}">
        <CollectionView.EmptyView>
            <Label
                AutomationId="products-empty"
                Text="No products found."
                HorizontalOptions="Center"
                VerticalOptions="Center" />
        </CollectionView.EmptyView>
    </CollectionView>
</Grid>
```

## Typed DataTemplate Command Pattern

When a `DataTemplate` has an item `x:DataType`, bindings inside the template are
scoped to the item, not the page view model. Bind the row root to the item for
automation, and bind commands back to the page view model explicitly:

```xml
<CollectionView
    AutomationId="products-list"
    ItemsSource="{Binding Products}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:Product">
            <Grid AutomationId="{Binding Sku, StringFormat='product-{0}'}">
                <Grid.GestureRecognizers>
                    <TapGestureRecognizer
                        Command="{Binding Source={RelativeSource AncestorType={x:Type vm:ProductsViewModel}}, Path=SelectProductCommand}"
                        CommandParameter="{Binding .}" />
                </Grid.GestureRecognizers>
            </Grid>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

Do not remove `x:DataType` just to reach the page command. Use an explicit
source (`RelativeSource`, `x:Reference`, or a binding proxy pattern that matches
the app's conventions) so compiled item bindings remain intact.

## Responsive Patterns

- Use idiom- or platform-specific resources for spacing and column counts.
- Prefer adaptive layout decisions in XAML/resources over platform-specific code
  unless behavior truly differs by platform.
- When column widths come from resources, prefer object-element
  `ColumnDefinition`/`RowDefinition` syntax. Do not embed
  `{StaticResource ...}` inside comma-separated `ColumnDefinitions` strings.
- Keep touch targets large enough for mobile even when the desktop layout is more
  dense.
- Test small phone, tablet, and desktop widths when the page is meant to scale.

## Validation Checklist

- Interactive controls have stable `AutomationId`s.
- Repeated styling is moved to resources.
- Data-driven screens have empty/loading/error behavior.
- `CollectionView` is not nested inside a parent `ScrollView`.
- The layout can scale across the intended device sizes.
