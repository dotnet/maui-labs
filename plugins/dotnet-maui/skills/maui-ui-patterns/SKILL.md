---
name: maui-ui-patterns
description: >-
  Build or update vendor-neutral .NET MAUI UI pages and components with
  responsive layouts, resources, visual states, AutomationIds, accessibility
  hooks, and loading/empty/error states. USE FOR: XAML or C# UI layout,
  Grid/FlexLayout choices, ResourceDictionary styles, design tokens,
  CollectionView empty/loading states, responsive phone/tablet/desktop UI, and
  adding stable AutomationIds. DO NOT USE FOR: Shell/DI/ViewModel architecture
  (use maui-app-architecture), accessibility audits (use maui-accessibility), or
  Syncfusion/vendor-specific control generation.
---

# MAUI UI Patterns

Use this skill when creating or reshaping a MAUI page or reusable component.
Prefer readable layouts, shared resources, stable automation hooks, and explicit
UI states over coordinate-based or screenshot-only UI.

## Workflow

1. Inspect the existing UI style: XAML, C# Markup, MauiReactor, or Blazor Hybrid.
2. Choose layout primitives based on content:
   - `Grid` for structured forms and dashboards.
   - `FlexLayout` for wrapping content and responsive chip/card layouts.
   - `VerticalStackLayout` / `HorizontalStackLayout` for simple linear groups.
   - `CollectionView` for repeated data; avoid wrapping it in `ScrollView`.
3. Move repeated colors, spacing, and text styles into resources.
4. Add `AutomationId` to important interactive elements.
5. Add loading, empty, error, and success states for data-driven screens.
6. Add basic accessibility hooks while building the UI; route deeper audits to
   `maui-accessibility`.
7. Verify with build plus DevFlow tree/screenshot when available.

## Layout Guardrails

| Avoid | Prefer |
| --- | --- |
| Absolute coordinates for normal app UI | `Grid`, `FlexLayout`, and adaptive resources |
| Nested `ScrollView` around `CollectionView` | `CollectionView` scrolling directly |
| Repeated inline colors/margins everywhere | `ResourceDictionary` styles and spacing resources |
| Text-only UI automation | Stable `AutomationId` values |
| One giant page without states | Loading, empty, error, and content states |

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

## Responsive Patterns

- Use idiom- or platform-specific resources for spacing and column counts.
- Prefer adaptive layout decisions in XAML/resources over platform-specific code
  unless behavior truly differs by platform.
- Keep touch targets large enough for mobile even when the desktop layout is more
  dense.
- Test small phone, tablet, and desktop widths when the page is meant to scale.

## Validation Checklist

- Interactive controls have stable `AutomationId`s.
- Repeated styling is moved to resources.
- Data-driven screens have empty/loading/error behavior.
- `CollectionView` is not nested inside a parent `ScrollView`.
- The layout can scale across the intended device sizes.

