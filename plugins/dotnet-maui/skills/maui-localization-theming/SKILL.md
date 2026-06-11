---
name: maui-localization-theming
description: >-
  Implement .NET MAUI localization, culture switching, RTL layout, platform
  language declarations, AppThemeBinding, DynamicResource theming, and runtime
  theme switching. USE FOR: RESX resources, translated strings, culture-aware
  formatting, right-to-left UI, app language settings, light/dark/system theme
  support, design tokens, and live theme updates. DO NOT USE FOR: general page
  layout (use maui-ui-patterns), accessibility audits (use maui-accessibility),
  or app assets/splash/icon configuration (use maui-app-assets-lifecycle).
---

# MAUI Localization and Theming

Use this skill when a MAUI app needs translated strings, culture-specific
formatting, right-to-left support, or runtime theme changes.

## Workflow

1. Inspect existing `Resources`, `ResourceDictionary`, `App.xaml`, and platform
   language declarations.
2. Put user-visible strings in RESX resources or the app's existing localization
   service.
3. Use culture-aware formatting for dates, numbers, and currency.
4. If runtime language switching is required, centralize culture updates and
   notify UI bindings.
5. Add RTL support through `FlowDirection` and test with an RTL culture.
6. Define colors, spacing, and styles as resources.
7. Use `AppThemeBinding` for simple light/dark values and `DynamicResource` for
   values that change at runtime.
8. Set `Application.Current.UserAppTheme` only when the user explicitly chooses
   a theme; use `Unspecified` for system theme.

## RESX Pattern

Use a default resource file plus culture-specific files:

```text
Resources/Strings/AppResources.resx
Resources/Strings/AppResources.es.resx
Resources/Strings/AppResources.fr.resx
```

Reference generated resources from XAML or ViewModels instead of hard-coded
strings. Keep resource keys stable and descriptive.

## Runtime Culture Switching

When the user changes language:

- Set `CultureInfo.DefaultThreadCurrentCulture`.
- Set `CultureInfo.DefaultThreadCurrentUICulture`.
- Update any generated resource culture property if the app uses one.
- Notify bound localized strings, recreate affected pages, or use a localization
  manager that raises change notifications.
- Persist the selected culture in `Preferences` only after the user chooses it.

Do not assume changing the thread culture automatically refreshes every existing
binding. Existing pages usually need notification or recreation.

## Platform Language Declarations

| Platform | Check |
| --- | --- |
| iOS/Mac Catalyst | Include supported localizations such as `CFBundleLocalizations` when platform language metadata is required. |
| Android | Confirm app language/resource configuration if using platform-specific localized resources. |
| Windows | Confirm package/resource language metadata if the app has Windows-specific resources. |

## RTL Guidance

- Use `FlowDirection="MatchParent"` for most pages and layouts.
- Set `FlowDirection="RightToLeft"` only when forcing a specific surface.
- Avoid left/right naming in resources; prefer start/end semantics in code and
  design tokens.
- Verify icons and gestures that imply direction.

## Theme Patterns

```xml
<Color x:Key="PageBackgroundColor">
    <AppThemeBinding Light="#FFFFFF" Dark="#111111" />
</Color>

<Label TextColor="{DynamicResource PrimaryTextColor}" />
```

Runtime theme changes should update resource dictionaries or
`Application.Current.UserAppTheme`:

```csharp
Application.Current!.UserAppTheme = AppTheme.Dark;
```

Use `DynamicResource` for resources that must react after the page is created.
Use `StaticResource` only when values are not expected to change at runtime.

## Validation Checklist

- User-visible strings are resource-backed.
- Culture changes update existing UI or intentionally recreate affected pages.
- RTL cultures have been considered for layout direction and icons.
- Theme resources use `AppThemeBinding` or `DynamicResource` appropriately.
- User theme selection can return to system default.
