# MauiProgram.cs Registration

Add DevFlow registration in `CreateMauiApp()` before `return builder.Build();`.

Standard MAUI:

```csharp
using Microsoft.Maui.DevFlow.Agent;

#if DEBUG
builder.AddMauiDevFlowAgent();
#endif
```

MAUI + Blazor WebView:

```csharp
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;

#if DEBUG
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools();
#endif
```

GTK uses the `.Gtk` namespaces:

```csharp
using Microsoft.Maui.DevFlow.Agent.Gtk;
using Microsoft.Maui.DevFlow.Blazor.Gtk;
```

Keep the calls inside `#if DEBUG` unless the user explicitly wants DevFlow available in non-Debug builds.
