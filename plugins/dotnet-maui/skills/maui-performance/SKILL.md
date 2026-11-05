---
name: maui-performance
description: >-
  Fix measurable MAUI performance issues. USE FOR: `maui profile startup`, Release/physical-device measurements, janky `CollectionView`, compiled bindings, oversized images, `MauiImage` `BaseSize`, thumbnail decoding, trim/NativeAOT regressions, IL2026, source-generated serializers, linker validation. DO NOT USE FOR: generic UI bugs, workload discovery, architecture-only work.
---

# MAUI Performance

Use this skill when the user reports a measurable performance symptom or asks
for a performance review. Measure first, then change the smallest thing that can
explain the symptom.

## Workflow

1. Identify the symptom: startup, first page render, scrolling, navigation,
   memory, image loading, or network-bound delays.
2. Inspect target frameworks and configuration. Prefer Release builds for
   meaningful startup and runtime measurements.
3. For startup, use the MAUI CLI profiling surface:

   ```bash
   maui profile startup --help
   ```

   Then run the target app with the appropriate platform/device options.
4. For UI runtime issues, inspect visual tree depth and logs when DevFlow is
   available.
5. Apply targeted fixes and re-measure.

## High-Value Fix Areas

| Symptom | Check |
| --- | --- |
| Slow startup | Startup profile, excessive work in `MauiProgram`, synchronous I/O, eager service construction, font/image count |
| Slow bindings | Missing `x:DataType`, reflection-heavy bindings, converters doing expensive work |
| Janky lists | `CollectionView` inside `ScrollView`, complex templates, missing item sizing strategy, image decode size |
| Layout cost | Deep nested layouts, unnecessary `Grid` nesting, repeated measure invalidations |
| Image memory | Oversized source images, missing MAUI image resizing, unbounded remote image caching |
| Release regression | Debug-only diagnostics leaking into Release, linker/trimming differences |

## Image Memory Guidance

A 4000×4000 RGBA PNG decodes to ~64 MB in memory regardless of how small it
appears on screen. A 200×200 display-sized image decodes to ~160 KB — 400×
less memory. Apply these changes when large images cause scrolling or memory
issues:

1. Resize images to display size before adding to the project.
2. Use the `MauiImage` build action in `.csproj` with `BaseSize` for automatic
   platform-specific resizing:
   ```xml
   <MauiImage Include="Resources/Images/*.png" BaseSize="400,400" />
   ```
3. Decode images to display size at runtime when loading from remote URLs.
   `BaseSize` only applies to packaged build-time image resources; it does not
   resize downloaded, camera, gallery, or user-imported images.
4. Use WebP or SVG for icons and logos that need to scale.
5. For list rows, generate or download thumbnails — never decode 4000 × 4000
   images inside a `CollectionView` item template.



- Do not wrap `CollectionView` in `ScrollView`.
- Keep item templates shallow and use compiled bindings.
- Prefer fixed or predictable item sizing when possible.
- Load thumbnails sized for display, not full-resolution images.
- Avoid expensive work in property getters used by item templates.

## Startup Guardrails

- Avoid network calls and file scans during app construction.
- Register services lazily when possible; do not instantiate heavy services just
  to register them.
- Defer non-critical initialization until after the first page is visible.
- Keep debug-only logging and DevFlow setup behind `#if DEBUG`.

## Trimming and NativeAOT Guardrails

- Treat trim and NativeAOT issues as publish-build issues, not Debug-build
  issues. Reproduce with the same publish properties used by the app.
- Do not silence trim warnings blindly. Warnings such as IL2026 indicate code
  that may not be safe when members are removed.
- Prefer source-generated serializers and explicit registrations over reflection
  discovery for app models and services used in trimmed builds.
- When reflection is required, keep the dependency explicit with attributes such
  as `DynamicDependency` or a linker descriptor, and document why it is needed.
- Test startup and the affected feature after publish; a successful Debug run is
  not evidence that trimming or NativeAOT is safe.

For System.Text.Json in trimmed or NativeAOT builds, move startup JSON paths to
a source-generated context:

```csharp
[JsonSerializable(typeof(List<Product>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}

var products = JsonSerializer.Deserialize(
    json,
    AppJsonContext.Default.ListProduct);
```

Keep the context near app models or serialization infrastructure and add every
serialized DTO shape used by the startup path. Do not hide IL2026 with broad
warning suppressions when a source-generated serializer can make the dependency
explicit.

## Validation Checklist

- There is a before/after measurement or a clear reason measurement is blocked.
- Startup investigations use `maui profile startup` when applicable.
- Binding-heavy pages use `x:DataType`.
- Lists avoid nested scrolling and oversized images.
- Trim/NativeAOT changes are validated with a publish build when applicable.
- Performance fixes do not remove accessibility metadata or automation hooks.
