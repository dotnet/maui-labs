---
name: maui-platform-backend
description: >
  Guide .NET MAUI backend implementation for new platforms (GTK, AppKit, Avalonia, TUI, etc.)
  in the dotnet/maui-labs repository. USE FOR: creating a new MAUI backend, implementing
  a control handler, adding Essentials services, setting up build targets, scaffolding
  project structure, debugging handler rendering with DevFlow. DO NOT USE FOR: building
  or running existing MAUI apps (use maui-ai-debugging skill), DevFlow CLI/MCP tool
  development (see AGENTS.md). INVOKES: GitHub MCP tools for reference code, web_fetch
  for MAUI source and platform API docs.
---

# .NET MAUI Platform Backend Implementation

Guide for creating a complete .NET MAUI backend for a new target platform inside the `dotnet/maui-labs` repository.

## When to Use This Skill

Use this skill when:
- Creating a new .NET MAUI backend for a platform (e.g., Avalonia, WinUI3, Terminal/TUI)
- Implementing or fixing a specific control handler for an existing backend
- Adding MAUI Essentials service implementations for a platform
- Setting up build targets, NuGet packaging, or `dotnet new` templates for a backend
- Scaffolding the project structure for a new platform under `platforms/`
- Debugging handler rendering issues using MAUI DevFlow
- Auditing handler coverage or implementation parity

## Process

### Phase 0: Research & Architecture Planning

Before writing any code, research the target platform:

1. **Identify the native view base class** (equivalent of `UIView`/`NSView`/`Gtk.Widget`)
2. **Understand the layout system** — MAUI computes frames; how does the platform apply them?
3. **Understand main-thread dispatching** — what mechanism does the platform use?
4. **Map MAUI concepts to platform APIs**: gesture system, text rendering, image loading, font system, web engine
5. **Review known extensibility gaps** — see [references/extensibility-gaps.md](references/extensibility-gaps.md) for workarounds

**Reference the canonical implementation**: Study the Linux.Gtk4 backend at `platforms/Linux.Gtk4/` on `main`. It includes three subprojects: core handlers (`Linux.Gtk4`), essentials (`Linux.Gtk4.Essentials`), and Blazor WebView (`Linux.Gtk4.BlazorWebView`). Also reference the MAUI source at [dotnet/maui](https://github.com/dotnet/maui).

### Phase 1: Scaffold Project Structure

Create the self-contained backend under `platforms/[Platform.Name]/` following the pattern in [references/project-structure.md](references/project-structure.md).

Key files to create first:
- `Directory.Build.props`, `Directory.Packages.props`, `[Platform.Name].slnx`
- `src/[Platform.Name]/[Platform.Name].csproj` with core handlers
- `samples/[Platform.Name].Sample/` with a minimal test app

**✅ Phase 1 is complete when:**
- Directory tree matches the layout in [references/project-structure.md](references/project-structure.md)
- `.csproj` files have correct namespaces (`Microsoft.Maui.Platforms.[Platform.Name]`), assembly names, and MAUI version references
- `dotnet build platforms/[Platform.Name]/[Platform.Name].slnx` succeeds (stub files are fine — handler logic comes in Phase 2)
- Don't implement handler logic yet — empty/stub classes are sufficient for this phase

### Phase 2: Core Infrastructure (Get "Hello World" on screen)

1. Base handler class (`[Prefix]ViewHandler<TVirtualView, TPlatformView>`)
2. Dispatcher (`[Prefix]Dispatcher : IDispatcher` + `[Prefix]DispatcherProvider`)
3. MauiContext (`[Prefix]MauiContext : IMauiContext`)
4. App host builder extension (`UseMauiApp[Platform]<TApp>()`)
5. Application + Window handlers
6. ContentPage + LayoutHandler
7. Label handler (first visible control)

### Phase 3: Basic Controls (Interactive app)

8. Button, Entry, Editor, Image handlers
9. Switch, CheckBox, Slider, ProgressBar, ActivityIndicator
10. ScrollView, Border handlers
11. Font management (IFontManager, IEmbeddedFontLoader)
12. Gesture recognizers (Tap, Pan)
13. Basic Essentials (AppInfo, DeviceInfo, FileSystem, Preferences)

### Phase 4: Navigation (Multi-page app)

14. NavigationPage, TabbedPage, FlyoutPage handlers
15. Alert/Dialog system (see extensibility gaps — requires DispatchProxy workaround)
16. Animations (ITicker on UI thread)

### Phase 5: Advanced Features

17. CollectionView/ListView (virtualization strategy depends on platform)
18. Shell handler, WebView, BlazorWebView
19. Remaining controls, gestures, essentials
20. Build targets / Resizetizer integration
21. `dotnet new` templates

Use the full checklist in [references/implementation-checklist.md](references/implementation-checklist.md) to track progress.

## Key Architectural Principles

- **MAUI computes layout** — your handler just applies frames via `GetDesiredSize` and `PlatformArrange`. Don't re-implement layout logic.
- **One LayoutHandler for all layouts** — VerticalStack, HorizontalStack, Grid, Flex, Absolute all share a single handler. MAUI's cross-platform layout managers do the work.
- **PropertyMapper drives updates** — map `IView` properties to native property setters. MAUI fires property changes; your mapper reacts.
- **Virtual methods in base handler** — common view properties (opacity, visibility, transforms, background, shadow, clip) belong in your base handler's mapper.

## ❌ Critical Anti-patterns

1. **Don't reimplement layout** — MAUI's `CrossPlatformMeasure`/`CrossPlatformArrange` handles all positioning. Your handler just sets native view frames/bounds.
2. **Don't skip the DispatchProxy workaround for alerts** — `AlertManager` and `IAlertManagerSubscription` are internal. You MUST use `DispatchProxy` + reflection. There is no alternative.
3. **Don't use `MainThread.BeginInvokeOnMainThread`** — it throws on custom backends. Use `Application.Current?.Dispatcher.Dispatch()` instead.
4. **Don't forget `SetDefault()` reflection for Essentials** — DI registration alone is NOT sufficient. Static `XXX.Default` properties still throw without the reflection workaround.
5. **Don't use the old naming convention** — it's `Microsoft.Maui.Platforms.[Platform.Name]`, NOT `Platform.Maui.[Platform]`. New backends live in `platforms/` in maui-labs, not standalone repos.
6. **Don't eagerly create Shell page content** — only create content for the currently visible page. Eager creation triggers crashes (null references, premature layout). Discovered empirically across macOS and GTK4 backends.
7. **Don't skip the image fallback chain** — `NSImage(fileName)` and similar APIs crash on missing files. Always implement a fallback chain (bundle resource → file path → named image → null).
8. **Don't fight native styling** — some visual differences (switch tint, slider track color, button borders) are native platform behavior. Document them as expected differences rather than forcing impossible workarounds.
9. **Don't throw from native layout/measure overrides** — AppKit (and likely other toolkits) retries layout infinitely when managed exceptions propagate out of `Layout()`, `IntrinsicContentSize`, `GetDesiredSize`, or `PlatformArrange`. This causes hangs or SIGSEGV, not managed exceptions. Wrap ALL native layout overrides in try-catch. Also guard against NaN values — `base.IntrinsicContentSize` can return NaN when padding is NaN, causing cascading failures.
10. **Don't call Shell navigation from a background thread** — `Shell.Current.GoToAsync()` on a non-UI thread causes SIGSEGV crashes (not managed exceptions). Always marshal navigation: check `MainThread.IsMainThread` (or platform equivalent like `NSThread.IsMain`) and wrap in `Dispatcher.Dispatch()`. This applies to any handler that triggers navigation in response to async events.
11. **Don't mix separate text property setters on labels** — Setting `StringValue`, `Font`, and attributed text independently creates ordering bugs and visual glitches. Use a single `UpdateAttributedText()` method that owns the entire text rendering path (font, color, decorations, alignment, line-break mode, HTML spans). Every label property change should flow through this one method.

## Debugging & Audit Techniques (from real sessions)

- **Kill stale app processes before retesting**: This is the #1 source of false negatives. Old app binaries mask fixes and waste debugging time. Always kill and restart the app after rebuilding. On macOS: `pkill -f YourApp.Sample` or use Activity Monitor.
- **Side-by-side screenshot comparison**: Run your backend and a reference platform (Mac Catalyst, iOS Simulator) on separate DevFlow ports. Compare screenshots page-by-page. This is the most effective audit technique.
- **Use `maui devflow wait` before inspecting**: Don't use arbitrary sleeps. `maui devflow wait --project ... --timeout 30` gates on actual agent connection, preventing false "element not found" errors.
- **Verify .NET binding existence for native APIs**: Platform APIs documented in Apple/GTK docs may not have .NET bindings, or may have different names (e.g., `NSColor.LabelColor` doesn't exist — use `NSColor.ControlText`; `NSAccessibilityNotifications` doesn't exist — use string literal `"AXAnnouncementRequested"`). Always verify bindings compile before building complex logic around them.
- **Use file logging, not Console.WriteLine, for native debugging**: Console output from native toolkit callbacks may not reach your terminal. File logging exposes view hierarchy issues, scroll timing, and lifecycle ordering that console output misses.
- **Audit-first workflow**: Create a checklist of every handler and feature, audit what exists, identify gaps. The checklist becomes your source of truth — don't start implementing randomly.
- **Track audit progress with SQL**: Large audits (60+ pages, 40+ handlers) need structured tracking. Use the SQL tool's `todos` table to track per-handler completion.
- **Read MAUI source for controller APIs**: When gestures or events don't work, read the MAUI source to find the real controller interfaces (e.g., `ISwipeGestureController`). Don't guess.
- **Custom native subclasses are often needed**: Wrapping isn't always enough. Button padding, editor placeholders, attributed labels — plan for native subclasses.
- **Prepare for visual audit scale**: The visual comparison audit (side-by-side with reference platform) is the single largest time investment — it consumed 22 of 68 checkpoints in the macOS session. Plan for multiple passes: first pass finds obvious issues, second pass catches regressions from fixes, third pass verifies edge cases.

## Stop Signals

- **Research phase**: Stop researching when you've identified the native view base class, layout application method, dispatcher mechanism, and gesture system. Don't exhaustively document every platform API.
- **Handler implementation**: Implement all properties from the MAUI interface. For properties with no native equivalent, add a no-op with a `// No native equivalent on [Platform]` comment. Don't invent complex workarounds for optional properties.
- **Essentials**: Implement real services for capabilities the platform has. For capabilities it doesn't have (e.g., Vibration on desktop), register a no-op stub that returns `IsSupported = false`. Don't spend time on impossible services.
- **Debugging a handler**: Use MAUI DevFlow to inspect → identify the issue → fix it. If an element is missing from the tree, check handler registration. If bounds are zero, check `GetDesiredSize`. Don't trace through the entire MAUI layout pipeline.

## References

- **Implementation checklist**: [references/implementation-checklist.md](references/implementation-checklist.md) — full handler/service/feature checklist
- **Extensibility gaps**: [references/extensibility-gaps.md](references/extensibility-gaps.md) — known MAUI gaps and workarounds
- **Project structure**: [references/project-structure.md](references/project-structure.md) — canonical layout, naming, NuGet packaging
- **DevFlow integration**: [references/devflow-integration.md](references/devflow-integration.md) — debugging backends with MAUI DevFlow
- **Empirical patterns**: [references/empirical-patterns.md](references/empirical-patterns.md) — lessons, pitfalls, and workarounds from building 7 backends
- **Full reference doc**: `platforms/references/PLATFORM_BACKEND_IMPLEMENTATION.md` in this repo — comprehensive 1500-line guide with starter prompts
