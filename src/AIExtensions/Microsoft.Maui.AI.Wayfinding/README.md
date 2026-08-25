# Microsoft.Maui.AI.Wayfinding

> **Experimental:** APIs may change between releases.

Plug-in AI tools and chat middleware that compose the model-free
`Microsoft.Maui.AI.Indexer` and `Microsoft.Maui.AI.Navigation` packages.

## Install

Reference Indexer directly so its XAML analyzer/build assets are available, then
add Wayfinding:

```xml
<PackageReference Include="Microsoft.Maui.AI.Indexer" />
<PackageReference Include="Microsoft.Maui.AI.Wayfinding" />
```

## Configure

```csharp
builder.Services.AddMauiWayfinding(MyAppIndexedPageCatalog.Default);
```

```csharp
var chat = new ChatClientBuilder(inner)
    .UseMauiWayfinding()
    .UseFunctionInvocation()
    .Build(services);
```

`UseMauiWayfinding()` must appear before `UseFunctionInvocation()` so the
function-invocation stage receives the Wayfinding tools.

The middleware adds four curated tools:

| Tool | Purpose |
|---|---|
| `search_app_ui` | Search navigable indexed screens and controls. |
| `get_app_destination` | Read a user-visible destination path, required inputs, and indexed UI. |
| `get_current_app_state` | Read the current user-visible screen and optional runtime UI. |
| `navigate_to_app_destination` | Navigate to a resolved semantic destination. |

It also adds an ephemeral wayfinding policy to each request. “Where/how” requests
remain read-only; “open/show/go/take me” requests may navigate. The policy is not
stored in conversation history. AI-facing results use user-visible titles and
controls; route URIs, CLR type names, file paths, command names, and binding
expressions are not exposed to the model.

## Optional rendered vision

Enable vision when the configured `IChatClient` accepts image input:

```csharp
builder.Services.AddMauiWayfinding(
    MyAppIndexedPageCatalog.Default,
    options =>
    {
        options.EnableVision = true;
        options.DefaultVisualTargetAutomationId = "OrderInsightsCharts";
    });

builder.Services.AddKeyedSingleton<IChatClient>(
    MauiWayfindingOptions.VisionChatClientServiceKey,
    rawVisionClient);
```

This adds `describe_current_visual`. It captures a visible `IView` by
`AutomationId` with `IView.CaptureAsync()`, keeps PNG bytes in memory, and asks
the configured model to describe pixel-only charts, drawings, maps, or diagrams.
Register the raw vision client under the documented key; do not point it at the
already-composed Wayfinding pipeline, which would recurse.

When a default target is configured but is not visible, capture fails rather
than silently broadening to the whole page. Leave the default unset only when
whole-current-page capture is the intended privacy boundary.

## Migration from pre-Wayfinding APIs

`ApplicationMapService`, `ApplicationDestination`, and destination resolution
types moved from `Microsoft.Maui.AI.Navigation` to
`Microsoft.Maui.AI.Wayfinding`. `ICurrentPageContextProvider` and
`RuntimePageContextProvider` moved from Navigation to
`Microsoft.Maui.AI.Indexer`.

The app still owns its chat UI, model and credentials, persona, domain tools,
approval UX, and conversation persistence.
