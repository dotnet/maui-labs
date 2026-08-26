# Microsoft.Maui.AI.Wayfinding

> **Experimental:** APIs may change between releases.

Plug-in AI tools and chat middleware that compose the model-free
`Microsoft.Maui.AI.Indexer` and `Microsoft.Maui.AI.Navigation` packages.

## Install

Reference Wayfinding. It includes the Indexer analyzer and XAML build assets:

```xml
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
expressions are not exposed to the model. AutomationIds are exposed only as
selectors for automation and vision tools and are never intended for user-facing
prose.

## Optional rendered vision

Enable vision when the configured `IChatClient` accepts image input:

```csharp
builder.Services.AddMauiWayfinding(
    MyAppIndexedPageCatalog.Default,
    options => options.EnableVision = true);

builder.Services.AddKeyedSingleton<IChatClient>(
    MauiWayfindingOptions.VisionChatClientServiceKey,
    rawVisionClient);
```

This adds `describe_current_visual`. Give useful visual controls a semantic
description and `AutomationId`:

```xml
<GraphicsView
    AutomationId="SpendingChart"
    SemanticProperties.Description="Where the money went chart" />
```

The compile-time and runtime indexes expose both values. Wayfinding first reads
the current UI, selects one or more relevant AutomationIds, captures those
visible `IView` instances with `IView.CaptureAsync()`, and asks the configured
model to describe their pixel-only charts, drawings, maps, or diagrams. PNG
bytes remain in memory.
Register the raw vision client under the documented key; do not point it at the
already-composed Wayfinding pipeline, which would recurse.

There is no global default target and no whole-page fallback. If a selected
AutomationId is not present in the current semantic snapshot, capture fails
before reading pixels. Calls accept at most four visual targets and 8 MB of PNG
data by default; configure `MaximumVisualTargets` and
`MaximumVisualPayloadBytes` when the app requires different limits.

## Migration from pre-Wayfinding APIs

`ApplicationMapService`, `ApplicationDestination`, and destination resolution
types moved from `Microsoft.Maui.AI.Navigation` to
`Microsoft.Maui.AI.Wayfinding`. `ICurrentPageContextProvider` and
`RuntimePageContextProvider` moved from Navigation to
`Microsoft.Maui.AI.Indexer`.

The app still owns its chat UI, model and credentials, persona, domain tools,
approval UX, and conversation persistence.
