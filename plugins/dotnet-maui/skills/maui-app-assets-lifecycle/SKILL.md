---
name: maui-app-assets-lifecycle
description: >-
  Configure .NET MAUI app icons, splash screens, bundled assets, app lifecycle,
  window lifecycle, background/resume state preservation, and platform-specific
  asset/lifecycle settings. USE FOR: MauiIcon, MauiSplashScreen, MauiImage,
  MauiFont, MauiAsset, app icon and splash screen replacement, Resources/Images,
  Resources/Fonts, Resources/Raw seed files, bundled JSON, FileSystem app
  package access, Window events, ConfigureLifecycleEvents, resume/save state,
  Xamarin drawable/UIAppFonts asset migration, and platform asset overrides. DO
  NOT USE FOR: localization/theme resources (use maui-localization-theming),
  device permissions (use maui-device-capabilities), or general project
  structure edits (use maui-project-structure).
---

# MAUI App Assets and Lifecycle

Use this skill when a MAUI app needs app identity assets, packaged files, or
state handling across start, stop, resume, and multi-window lifecycle events.

## Workflow

1. Inspect the app project file and `Resources` folders before adding assets.
2. Use MAUI single-project item types for shared assets.
3. Put platform-only assets under the matching `Platforms/*` folder only when a
   shared MAUI asset cannot express the requirement.
4. Access bundled raw files through `FileSystem.OpenAppPackageFileAsync`.
5. Use `Window` lifecycle events for cross-platform app/window state.
6. Use `ConfigureLifecycleEvents` for platform-specific lifecycle hooks.
7. Persist user/session state before deactivation or stop; restore on resume or
   app startup.
8. Validate icon/splash/background behavior on each target platform.

## Asset Item Types

```xml
<MauiIcon Include="Resources/AppIcon/appicon.svg" ForegroundFile="Resources/AppIcon/appiconfg.svg" Color="#512BD4" />
<MauiSplashScreen Include="Resources/Splash/splash.svg" Color="#512BD4" BaseSize="128,128" />
<MauiImage Include="Resources/Images/*" />
<MauiFont Include="Resources/Fonts/*" />
<MauiAsset Include="Resources/Raw/**" LogicalName="%(RecursiveDir)%(Filename)%(Extension)" />
```

Use vector SVG sources when possible and let MAUI generate platform-specific
sizes. Avoid manually adding resized copies unless a platform requires a custom
override.

When migrating from Xamarin.iOS, remove old `UIAppFonts` entries from
`Platforms/iOS/Info.plist`; `MauiFont` generates those registrations. Keep font
aliases in MAUI's font pipeline, typically with `MauiFont` items and
`ConfigureFonts()` when explicit aliases are needed. Do not copy Xamarin Android
`drawable-*` folders or iOS `.xcassets` directly into `Platforms/*` for images
used by MAUI XAML/C#; put shared image assets in `Resources/Images` as
`MauiImage` items.

## Bundled Assets

Put read-only bundled files in `Resources/Raw` and open them with:

```csharp
await using var stream = await FileSystem.OpenAppPackageFileAsync("seed.json");
```

Copy bundled data into app storage before editing it. Files in the app package
are read-only.

## Lifecycle Guidance

Cross-platform `Window` lifecycle events are the default place to coordinate
state:

- `Created`
- `Activated`
- `Deactivated`
- `Stopped`
- `Resumed`
- `Destroying`

Use platform lifecycle hooks only for behavior that truly depends on native app
events, such as Android activity callbacks or iOS scene/app delegate events.

## State Preservation

- Save lightweight preferences in `Preferences`.
- Save larger state, drafts, and caches in app data files or SQLite.
- Persist in-progress work before `Stopped` or `Destroying`.
- Reconcile stale network data after `Resumed`.
- Treat each window as having separate UI/navigation state when multi-window is
  enabled.
- Keep background work platform-specific and explicit; do not assume arbitrary
  code continues running after the app is stopped.

## Platform Configuration

| Area | Shared default | Platform override examples |
| --- | --- | --- |
| Icon | `MauiIcon` | Android adaptive icon details, iOS alternate icon metadata |
| Splash | `MauiSplashScreen` | Platform launch storyboard/theme customization |
| Bundled files | `MauiAsset` in `Resources/Raw` | Native-only files under `Platforms/*` |
| Lifecycle | `Window` events | `ConfigureLifecycleEvents` for native callbacks |

## Validation Checklist

- Shared assets use MAUI item types and live under `Resources`.
- Bundled files are read through `FileSystem.OpenAppPackageFileAsync`.
- Editable data is copied out of the app package.
- State is saved before stop/destroy and restored on startup/resume.
- Platform lifecycle hooks are isolated to platform-specific behavior.
