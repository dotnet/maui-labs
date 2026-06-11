---
name: maui-ai-tool-bindings
description: >-
  Use Microsoft.Maui.AI.Attributes / AIExtensions to source-generate
  Microsoft.Extensions.AI tools for MAUI apps. USE FOR: [ExportAIFunction],
  [AIToolSource], AIToolContext, Default.Tools, DI parameter binding,
  [FromServices], [FromKeyedServices], ApprovalRequired, AOT-friendly tools, and
  IChatClient wiring. DO NOT USE FOR: Essentials.AI chat/embedding setup,
  native platform bindings, or non-MAUI tool-calling theory.
---

# MAUI AI Tool Bindings

Use this skill when a MAUI app needs AI-callable tools backed by app services,
view models, or static utility methods. `Microsoft.Maui.AI.Attributes` generates
`Microsoft.Extensions.AI` tools at compile time to avoid reflection-heavy
invocation paths.

## Basic Workflow

1. Install the package:

   ```bash
   dotnet add package Microsoft.Maui.AI.Attributes --prerelease
   ```

2. Annotate methods or property accessors:

   ```csharp
   using System.ComponentModel;
   using Microsoft.Maui.AI.Attributes;

   public sealed class PlantCatalogService
   {
       [Description("Searches the plant catalog by name or category.")]
       [ExportAIFunction("search_plants")]
       public IReadOnlyList<PlantInfo> SearchPlants(
           [Description("Optional filter text")] string? query = null)
       {
           return [];
       }
   }
   ```

3. Create a curated tool context:

   ```csharp
   [AIToolSource(typeof(PlantCatalogService))]
   public partial class GardenTools : AIToolContext
   {
   }
   ```

4. Register services normally in `MauiProgram.cs`:

   ```csharp
   builder.Services.AddSingleton<PlantCatalogService>();
   ```

5. Pass generated tools into an `IChatClient`:

   ```csharp
   var client = innerClient.AsBuilder()
       .UseFunctionInvocation()
       .ConfigureOptions(options =>
       {
           options.Tools ??= [];
           foreach (var tool in GardenTools.Default.Tools)
               options.Tools.Add(tool);
       })
       .Build(serviceProvider);
   ```

## Tool Context Choices

| Need | Pattern |
| --- | --- |
| Curated feature-specific tools | Explicit partial `AIToolContext` with `[AIToolSource]` |
| Small prototype exposing all exported tools | Assembly-wide `<AssemblyName>ToolContext.Default.Tools` |
| Sensitive mutation | `[ExportAIFunction(ApprovalRequired = true)]` |
| Static utility tool | Static `[ExportAIFunction]` method |
| Service-backed tool | Instance method on a DI-registered service |

Prefer explicit contexts for production features so tool names and scope stay
stable.

## DI Parameter Binding

The generator classifies parameters at compile time:

| Parameter | Binding |
| --- | --- |
| Plain `string`, `int`, records, enums | JSON argument in the tool schema |
| `CancellationToken` | Function invocation cancellation token |
| `IServiceProvider` | `AIFunctionArguments.Services` |
| `[FromServices] IMyService service` | Resolved from the provider, excluded from schema |
| `[FromKeyedServices("key")] IMyService service` | Resolved from keyed DI, excluded from schema |

For instance methods, the host service itself is resolved from the provider. If
the provider is missing, generated tools throw a clear `InvalidOperationException`
instead of returning fake success.

## Scope and Lifetime

The library does not create DI scopes. Keep the scope alive for as long as the
chat session can invoke tools, then dispose it when the session ends:

`AdditionalTools` on the invocation middleware injects tools into every request
automatically, which is idiomatic for a session-scoped tool set.

```csharp
private IServiceScope? _sessionScope;
private IChatClient? _sessionClient;

if (_sessionClient is IAsyncDisposable asyncDisposable)
    await asyncDisposable.DisposeAsync();
else
    _sessionClient?.Dispose();

_sessionScope?.Dispose();
_sessionScope = serviceScopeFactory.CreateScope(); // Inject IServiceScopeFactory.

_sessionClient = innerClient.AsBuilder()
    .UseFunctionInvocation(configure: invocation =>
        invocation.AdditionalTools = [.. GardenTools.Default.Tools])
    .Build(_sessionScope.ServiceProvider);

// Also dispose _sessionClient and _sessionScope when the chat session or view model ends.
```

Use a per-chat-session scope when tools hold conversational state.

## AOT and Analyzer Guardrails

- Use declared methods on declared types. Lambdas, local functions, and dynamic
  methods are not source-generated tools.
- Add `[Description]` to tools and user-visible parameters.
- Avoid generic methods, `ref`/`out`/`in` parameters, delegates, pointers, and
  shapes that cannot round-trip through JSON.
- Materialize `IAsyncEnumerable<T>` results to arrays/lists if the consumer
  expects JSON array output.
- Watch diagnostics such as `MAUIAI002`, `MAUIAI003`, and `MAUIAI004`.

## Validation Checklist

- Tool names are stable and safe for the assistant to call.
- Sensitive tools require approval.
- DI services used by tools are registered with the intended lifetime.
- `UseFunctionInvocation().Build(serviceProvider)` flows a provider to tool
  invocations.
- The app builds so the generator emits the context and diagnostics.
- Tool output is deterministic enough for the app's AI UX.
