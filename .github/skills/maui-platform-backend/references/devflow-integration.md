# MAUI DevFlow Integration for Backend Development

MAUI DevFlow is a development toolkit in `dotnet/maui-labs` that gives AI agents (and humans) full autonomy over the MAUI development loop: **build → deploy → inspect → interact → diagnose → fix → repeat** — entirely from the terminal.

---

## Why DevFlow Matters for Backend Development

When building a new MAUI backend, you're operating blind — no Visual Studio XAML Hot Reload, no Live Visual Tree, no device previews. MAUI DevFlow fills this gap:

- **See what the app renders** — take screenshots from the terminal
- **Inspect the visual tree** — verify controls exist with correct types and properties
- **Interact with controls** — tap buttons, fill text, scroll
- **Check element properties** — bounds, visibility, native type, native properties

This creates a tight debugging loop:
1. Make a handler change
2. Build and run the app
3. `maui devflow ui tree` — verify the control rendered
4. `maui devflow ui screenshot` — visually confirm
5. `maui devflow ui element <id>` — inspect bounds and properties
6. Find the issue, fix it, repeat

**Without DevFlow**: you're `Console.WriteLine`-debugging handler property mappings.
**With DevFlow**: you have a complete diagnostic view of every element.

---

## Setting Up DevFlow for a New Backend

### Step 1: Create the Platform Agent

Your platform needs a `DevFlowAgentService` subclass. For backends in maui-labs, the DevFlow agent code lives in the existing DevFlow product (`src/DevFlow/`).

**What to implement:**

| Component | Base Class | What to Override |
|-----------|-----------|-----------------|
| Agent Service | `DevFlowAgentService` | `CreateTreeWalker()`, `TryNativeTap()`, `CaptureScreenshotAsync()`, `GetNativeWindowSize()` |
| Tree Walker | `VisualTreeWalker` | `PopulateNativeInfo(ElementInfo, VisualElement)` — extracts native widget properties |
| Registration | Extension method | `AddMauiDevFlowAgent()` |

**Core abstractions:**

```csharp
// Platform-specific agent service
public class MyPlatformAgentService : DevFlowAgentService
{
    protected override VisualTreeWalker CreateTreeWalker()
        => new MyPlatformVisualTreeWalker();

    protected override bool TryNativeTap(VisualElement ve)
    {
        // Use platform API to programmatically click/tap the native view
        return false;
    }

    protected override async Task<byte[]?> CaptureScreenshotAsync(VisualElement rootElement)
    {
        // Use platform screenshot API to capture the window as PNG
        return null;
    }

    protected override (double width, double height) GetNativeWindowSize(IWindow window)
    {
        // Return the current window dimensions from the native window
        return (0, 0);
    }
}

// Platform-specific tree walker
public class MyPlatformVisualTreeWalker : VisualTreeWalker
{
    protected override void PopulateNativeInfo(ElementInfo info, VisualElement element)
    {
        var nativeView = element.Handler?.PlatformView;
        if (nativeView == null) return;

        info.NativeType = nativeView.GetType().Name;
        info.NativeProperties["isEnabled"] = /* native enabled state */;
        info.NativeProperties["isFocused"] = /* native focus state */;
    }
}
```

### Step 2: Wire Up in Your Sample App

Enable DevFlow conditionally via the `EnableMauiDevFlow` MSBuild property:

```xml
<!-- In Directory.Build.props -->
<PropertyGroup>
  <EnableMauiDevFlow Condition="'$(EnableMauiDevFlow)' == ''">false</EnableMauiDevFlow>
</PropertyGroup>
<PropertyGroup Condition="'$(EnableMauiDevFlow)' == 'true'">
  <DefineConstants>$(DefineConstants);MAUIDEVFLOW</DefineConstants>
</PropertyGroup>
```

```csharp
// MauiProgram.cs
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiAppMyPlatform<App>();

    #if MAUIDEVFLOW
    builder.AddMauiDevFlowAgent();
    #endif

    return builder.Build();
}
```

### Step 3: Use the CLI

```bash
# Install CLI
dotnet tool install -g Microsoft.Maui.Cli --prerelease

# Start broker (auto-discovers agents)
maui devflow broker start

# Build and run with DevFlow enabled
dotnet run --project samples/[Platform.Name].Sample -p:EnableMauiDevFlow=true

# Inspect visual tree
maui devflow ui tree

# Take screenshot
maui devflow ui screenshot --output render-check.png

# Interact
maui devflow ui tap MyButton
maui devflow ui fill MyEntry "Hello from terminal"

# Check element properties
maui devflow ui query --type Label
maui devflow ui element <elementId>
```

---

## Debugging Handler Issues

### Control not rendering
```bash
maui devflow ui tree    # Is the element in the tree?
```
- **Missing from tree**: Check handler registration in `AppHostBuilderExtensions.cs`
- **In tree but invisible**: Check bounds — zero width/height means `GetDesiredSize` returned empty
- **In tree with correct bounds but invisible**: Check IsVisible, Opacity, native visibility

### Wrong size/position
```bash
maui devflow ui element <id>    # Check Bounds, WindowBounds
```
- Verify `GetDesiredSize` delegates to `CrossPlatformMeasure`
- Verify `PlatformArrange` applies the `Rect` to the native view's frame correctly
- Ensure coordinate system is top-left origin

### Property not applying
```bash
maui devflow ui property <id> Text       # Check MAUI-side value
maui devflow ui property <id> IsVisible  # Check visibility
```
- Verify the property mapper entry in your handler
- Check that the mapper method reads from `virtualView` (not cached state)
- Verify the native property setter is correct

---

## Co-Evolution Workflow

When building a new backend, you're simultaneously building:

1. **Backend Handlers** — the actual MAUI handlers and essentials
2. **Sample App** — exercises all handlers
3. **DevFlow Agent** — platform-specific tree walking, screenshots, interaction
4. **DevFlow Sample** — tests the agent on the new platform

These evolve together:
- Phase 1: Everything uses **project references** for rapid iteration
- Phase 2: Backend stabilizes → backend becomes NuGet, DevFlow stays project-ref
- Phase 3: Everything published → all NuGet

---

## DevFlow Platform Support Checklist

| Feature | Status | Notes |
|---------|--------|-------|
| [ ] Agent Service | | Subclass of `DevFlowAgentService` |
| [ ] Visual Tree Walker | | `PopulateNativeInfo()` extracts native properties |
| [ ] Native Tap | | `TryNativeTap()` invokes platform click/activate |
| [ ] Native Fill | | Text input for Entry/Editor/SearchBar |
| [ ] Screenshots | | `CaptureScreenshotAsync()` captures window as PNG |
| [ ] Window Size | | `GetNativeWindowSize()` returns dimensions |
| [ ] Registration | | `AddMauiDevFlowAgent()` for DI |
| [ ] Broker Integration | | Agent connects to broker for port assignment |
| [ ] CLI Compatibility | | `maui devflow ui tree/screenshot/tap` all work |
| [ ] Blazor CDP | | (Optional) Chrome DevTools Protocol for Blazor WebView |
