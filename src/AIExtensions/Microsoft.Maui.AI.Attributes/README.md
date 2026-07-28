# Microsoft.Maui.AI.Attributes

Source-generated AI tool discovery for .NET 10. Decorate methods or property accessors with `[ExportAIFunction]`, then either group them into an explicit tool context with `[AIToolSource]` or use the auto-generated assembly-wide context — no DI registration ceremony, no runtime reflection on the hot path.

## How it works

### 1. Annotate your methods or property accessors

```csharp
using System.ComponentModel;
using Microsoft.Maui.AI.Attributes;

public class PlantCatalogService
{
    [Description("Searches the plant catalog by name or category.")]
    [ExportAIFunction("search_plants")]
    public List<PlantInfo> SearchPlants(
        [Description("Optional filter text")] string? query = null)
    {
        // ...
    }
}
```

- `[ExportAIFunction]` marks a method, property getter, or property setter as an AI-callable tool.
- `[ExportAIFunction("custom_name")]` overrides the tool name (defaults to method name).
- `[ExportAIFunction(ApprovalRequired = true)]` wraps the tool so it requires user approval before executing.
- `[Description]` on the method and parameters provides AI-visible documentation.

Methods may be **instance** (resolved from DI) or **static** (no DI required — see below). Property getters surface as zero-argument tools; property setters surface as a single `value` argument in the generated schema.

### 2. Define a tool context (optional)

```csharp
using Microsoft.Maui.AI.Attributes;

[AIToolSource(typeof(PlantCatalogService))]
[AIToolSource(typeof(GardenService))]
public partial class GardenTools : AIToolContext { }
```

The **source generator** scans each `[AIToolSource]` type for `[ExportAIFunction]` methods at compile time and emits a sealed `AIFunction` subclass per method, plus a `Default` singleton instance and a `Tools` method on the context. No reflection on the invocation path. AOT-friendly.

An explicit context is still the best choice when you want to **curate a subset of tools**, keep a **stable public name**, or separate tools by feature area.

### 3. Or use the assembly-wide auto context

If you skip the partial class entirely, the generator now also emits an **assembly-wide tool context** that collects every `[ExportAIFunction]` in the current project.

For an assembly named `MyApp`, the generated type is:

```csharp
IReadOnlyList<AITool> tools = MyAppToolContext.Default.Tools;
```

The exact class name is `<AssemblyName>ToolContext` in your root namespace, with dots removed from the assembly name. This is a great fit for small apps, prototypes, and quick demos where "all exported tools in this assembly" is the behavior you want.

### 4. Get the tools

```csharp
// Headline pattern — no DI registration needed.
IReadOnlyList<AITool> tools = GardenTools.Default.Tools;
```

That's the whole API. `Default` is a static singleton; `Tools` returns the same `AITool[]` every time. Pass it straight into any chat client.

### 5. Wire tools into an `IChatClient`

```csharp
var tools = GardenTools.Default.Tools;

var client = innerChatClient.AsBuilder()
    .UseFunctionInvocation()
    .ConfigureOptions(opts =>
    {
        opts.Tools ??= [];
        foreach (var tool in tools)
            opts.Tools.Add(tool);
    })
    .Build(serviceProvider);   // ← provider flows to AIFunctionArguments.Services

await foreach (var update in client.GetStreamingResponseAsync(messages))
    Console.Write(update.Text);
```

`UseFunctionInvocation().Build(sp)` populates `AIFunctionArguments.Services` on every tool call. Each generated tool reads that provider to resolve its host service and any `[FromServices]` parameters.

## Static tools — no DI needed

If your method is `static` and doesn't use `[FromServices]` / `[FromKeyedServices]`, the generator emits a tool that **never touches `IServiceProvider`**. You can call it without DI at all:

```csharp
public static class GreetingService
{
    [Description("Returns a greeting for the given name.")]
    [ExportAIFunction("say_hello")]
    public static string SayHello(string name) => $"Hello, {name}!";
}

[AIToolSource(typeof(GreetingService))]
public partial class GreetingTools : AIToolContext { }

var tool = (AIFunction)GreetingTools.Default.Tools.First(t => t.Name == "say_hello");
var result = await tool.InvokeAsync(
    new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "Ada" }));
// result == "Hello, Ada!"
```

A static method is free to resolve its own dependencies internally if it wants — but the *tool* surface stays DI-free.

## Property getters and setters

Accessor-level exports are useful when the AI should read or change app state without forcing you to add wrapper methods:

```csharp
public partial class CartViewModel
{
    public string CartMode
    {
        [ExportAIFunction("get_cart_mode")]
        [Description("Gets the current cart display mode.")]
        get;

        [ExportAIFunction("set_cart_mode")]
        [Description("Sets the current cart display mode to values like 'normal' or 'compact'.")]
        set;
    } = "normal";
}
```

The generated getter tool has no JSON inputs. The setter tool emits a required `value` parameter and assigns it directly to the property, which makes this pattern a good fit for toggles, preferences, and view-model state.

## Dependency injection & parameter binding

At compile time the generator classifies each parameter and emits the right binding code:

| Parameter shape | Binding | In schema? |
|---|---|---|
| `CancellationToken` | Flows from the function-invocation pipeline. | [ ] |
| `IServiceProvider` | The provider on `AIFunctionArguments.Services`. | [ ] |
| `AIFunctionArguments` | The raw argument bag. | [ ] |
| `[FromServices] IMyThing x` | `provider.GetService<IMyThing>()` with null check. | [ ] |
| `[FromKeyedServices("k")] IMyThing x` | `(IKeyedServiceProvider).GetKeyedService(typeof(IMyThing), "k")` with null check. | [ ] |
| Everything else (`string`, records, enums, …) | Bound from the JSON argument dictionary. | [x] |

For accessor-based setter tools, the generated `value` argument also falls into the last row above, so it is included in the schema and required unless the target property type or binding rules say otherwise.

For instance methods the host service is resolved via `provider.GetRequiredService<TService>()`. For static methods the call is emitted directly — no service lookup, no provider needed unless a `[FromServices]` parameter forces it.

If a tool needs services and `arguments.Services` is null, the generated code throws an `InvalidOperationException` naming the specific tool and source type, pointing you at `UseFunctionInvocation().Build(sp)` or making the method `static`.

## Service lifetimes & scopes

The library **never creates a DI scope**. It uses whatever `IServiceProvider` was threaded in via `AIFunctionArguments.Services`.

You control the scope purely by how you build your `IChatClient`:

| Build pattern | Resulting scope behavior |
|---|---|
| `Build(app.Services)` once at startup | Root scope. `AddSingleton` and `AddTransient` work. `AddScoped` services are root-resolved. |
| `Build(scope.ServiceProvider)` per chat session | Scoped services live for the chat session. Reset by disposing the scope and creating a new one. |

Example (scope-per-session, see `samples/AIExtensions.Sample.Garden`):

```csharp
_sessionScope?.Dispose();
_sessionScope = _rootProvider.CreateScope();

_sessionClient = new ChatClientBuilder(_innerChatClient)
    .UseFunctionInvocation(configure: fic =>
        fic.AdditionalTools = [.. GardenTools.Default.Tools])
    .Build(_sessionScope.ServiceProvider);
```

Each tool invocation receives the session scope's provider through `AIFunctionArguments.Services`, so `AddScoped<GardenService>()` gets a fresh instance per session but stays consistent across tool calls within the session.

## Key types

| Type | Description |
|---|---|
| `ExportAIFunctionAttribute` | Marks a method as an AI tool. Set `ApprovalRequired = true` to require approval. |
| `AIToolSourceAttribute` | Declares which type contributes tools to a context. |
| `AIToolContext` | Base class for source-generated tool contexts. Exposes the abstract `Tools` method that the generator overrides. The static `Default` property is also generated. |
| `FromServicesAttribute` | Resolves a parameter from `IServiceProvider` (lives in `Microsoft.Extensions.DependencyInjection` for discoverability alongside `[FromKeyedServices]`). |

## Mixing in hand-crafted tools

Generated tools are plain `AITool` instances, so they compose with anything:

```csharp
var tools = new List<AITool>(GardenTools.Default.Tools)
{
    AIFunctionFactory.Create(
        () => DateTime.UtcNow.ToString("o"),
        name: "get_current_datetime",
        description: "Gets the current UTC time."),
};
```

Pass the merged list into `ConfigureOptions` (or `FunctionInvokingChatClient.AdditionalTools`) the same way.

## Samples

The repository ships three focused samples under `samples/`. Each tells exactly one story:

| Sample | Type | Demonstrates |
|---|---|---|
| [`AIExtensions.Sample.Hello`](../../samples/AIExtensions.Sample.Hello) | Console | Smallest end-to-end. One DI service + one static service, both surfaced through `Default.Tools`. |
| [`AIExtensions.Sample.DIParameters`](../../samples/AIExtensions.Sample.DIParameters) | Console | Every parameter binding shape: `[FromServices]`, `[FromKeyedServices]`, plain records, `CancellationToken`. |
| [`AIExtensions.Sample.Garden`](../../samples/AIExtensions.Sample.Garden) | MAUI | Static + instance + interface + transient view-model tool sources, approval-required tools, modal navigation, cart-mode property tools, and DevFlow integration. |

## AOT compatibility

The hot invocation path contains **no reflection and no dynamic code emission**. Each tool is a compile-time-emitted `AIFunction` subclass that resolves services from `IServiceProvider`, reads named arguments, and calls your method directly.

Schema generation still flows through `AIJsonUtilities.CreateFunctionJsonSchema` (reflective, but invoked once per tool at warmup and cached behind `Lazy<>`). String-literal schema emission is a planned follow-up.

## Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| `MAUIAI002` | Warning | A parameter's type is unlikely to round-trip through JSON (e.g. delegate or pointer). Annotate with `[FromServices]` or change the signature. |
| `MAUIAI003` | Warning | An `[AIToolSource(typeof(T))]` references a type that has no `[ExportAIFunction]` methods. |
| `MAUIAI004` | Error | The method has an unsupported signature (generic method, `ref`/`out` parameters, etc.). |

## Compatibility with `Microsoft.Extensions.AI.AIFunctionFactory`

This library aims to match the runtime behavior of `AIFunctionFactory.Create(MethodInfo, target)` as closely as possible. The `Microsoft.Maui.AI.Attributes.Tests/Equivalence/` suite mirrors the in-scope tests from [`dotnet/extensions`'s `AIFunctionFactoryTest.cs`](https://github.com/dotnet/extensions/blob/main/test/Libraries/Microsoft.Extensions.AI.Tests/Functions/AIFunctionFactoryTest.cs) and, where feasible, pairs each generated tool against an `AIFunctionFactory.Create`-backed oracle for side-by-side comparison.

### Behaviors that match exactly

- **Parameter mapping by name.** Plain parameters are bound from `AIFunctionArguments` by case-sensitive name.
- **C# default values.** Parameters with C# defaults (`= value`) are treated as optional and their defaults are reflected in the JSON schema.
- **Missing required parameters throw `ArgumentException`** whose message contains the parameter name.
- **JSON-encoded argument tolerance.** `JsonElement`, `JsonNode`, `JsonDocument`, and boxed numeric types are all accepted and deserialized to the target type. Raw JSON strings are parsed if they represent valid JSON for the target type, otherwise treated as literal strings where the target type allows.
- **Framework-provided parameters** (`CancellationToken`, `IServiceProvider`, `AIFunctionArguments`) are injected by type, excluded from the JSON schema, and do not require a name match.
- **Async return types.** `Task`, `ValueTask`, `Task<T>`, `ValueTask<T>` are awaited correctly; `void`/`Task`/`ValueTask` produce a null `ReturnJsonSchema`.
- **`[FromKeyedServices]`** with a string or `null` key resolves via `IKeyedServiceProvider`. The parameter is excluded from the JSON schema.
- **Nullable schema shape.** Both `int?`/`DateTime?` value types and `string?`/other nullable reference types produce JSON schemas with `"type": ["…", "null"]`; defaulted nullables produce `"default": null` and are omitted from the `required` list.
- **`AIContent`-typed returns** are returned as-is without JSON serialization, matching the default `MarshalResult` behavior in `AIFunctionFactory`.
- **Struct defaults** (`Guid`, `StructWithDefaultCtor`) are passed as CLR `default(T)`, matching the `= default` behavior observed by `AIFunctionFactory`.

### Known behavioral differences

| # | Area | `AIFunctionFactory.Create` | `Microsoft.Maui.AI.Attributes` | Why |
|---|------|----------------------------|--------------------------------|-----|
| 1 | **Source** | Delegates, lambdas, local functions, anonymous methods, and `DynamicMethod` all work. | Only `[ExportAIFunction]` methods on a declared type. | The generator runs at compile time against Roslyn symbols; it needs a declared method to emit code for. |
| 2 | **Instance acquisition** | Caller supplies `target`, a `createInstanceFunc`, or lets `ActivatorUtilities` construct the type. | Instance methods: resolved via `IServiceProvider.GetService<TService>()` (with null check) from `AIFunctionArguments.Services`. Static methods: called directly with no service lookup. | Encourages clean DI wiring without per-invocation reflection; static methods stay zero-DI. |
| 3 | **Static methods** | Fails at `AIFunctionFactory.Create` time with `ArgumentException`. | **Fully supported.** A static target with no `[FromServices]` parameters generates a DI-free tool that works without a service provider. | Lets you author small utility tools (math, formatting, etc.) without wiring DI at all. |
| 4 | **Instance disposal** | When `createInstanceFunc` is used, disposable instances are disposed after each invocation. | Not applicable — lifetimes are managed entirely by DI. | DI already manages `IDisposable`/`IAsyncDisposable` lifetimes. |
| 5 | **Automatic DI scope** | None (caller is responsible). | None (caller is responsible). | Matching behavior — neither library creates a scope automatically. **Your `IChatClient` pipeline must thread the appropriate `IServiceProvider` to `FunctionInvokingChatClient`**; see the Garden sample for a per-session-scope pattern. |
| 6 | **`[FromServices]` / arbitrary DI parameters** | Unsupported by default. | Supported via explicit `[FromServices]` and `[FromKeyedServices]` attributes. Marked parameters resolve from DI and are excluded from the JSON schema. | A deliberate ergonomic improvement — this is the main reason this library exists. There is **no implicit DI inference**: interface/abstract parameters without `[FromServices]` are still treated as JSON arguments, matching reflection behavior. |
| 7 | **`[FromKeyedServices]` with missing key and default value** | Falls back to the parameter's default value. | Throws `InvalidOperationException` with a contextual error message identifying the parameter, type, and key. Uses `as IKeyedServiceProvider` pattern to safely handle non-keyed providers like `EmptyServiceProvider`. | Simplifies the emitted code; the contract is "this key must exist". |
| 8 | **`IServiceProvider?` parameter, `arguments.Services == null`** | Passes `null` to the method. | If the tool needs a provider (instance method or `[FromServices]` parameter) and `arguments.Services` is null, throws `InvalidOperationException`. Static no-DI tools accept null `Services` without throwing. | Forces explicit wiring at the build-the-client layer; static tools get an escape hatch. |
| 9 | **`AIFunctionFactoryOptions`** (`Name`, `Description`, `AdditionalProperties`, `ExcludeResultSchema`, `ConfigureParameterBinding`, `MarshalResult`, `SerializerOptions`) | First-class — overrides every aspect of the produced tool. | Not exposed. Name/description come from `[ExportAIFunction]` and `[Description]`; parameter binding is decided at compile time; result marshaling uses default JSON behavior. | The design goal of this library is compile-time correctness: runtime options that rewrite binding/marshaling behavior undercut that. If you need them, call `AIFunctionFactory.Create` directly for that one tool and merge it into your `tools` list. |
| 10 | **`ConfigureParameterBinding`** | First-class runtime hook. | Not supported. Use `[FromServices]`/`[FromKeyedServices]` for DI binding; use `AIFunctionArguments` parameter for ad-hoc context. | Compile-time decision, same rationale as (9). |
| 11 | **`MarshalResult`** | First-class runtime hook for post-processing method results. | Not exposed. Methods return their result directly (`AIContent`-typed returns pass through; other types are serialized to JSON by the `AIFunction` infrastructure). | Same rationale as (9). |
| 12 | **`ExcludeResultSchema`** | Option to suppress `ReturnJsonSchema`. | Always emits `ReturnJsonSchema` for non-void returns. | No runtime option to set; file a request if needed. |
| 13 | **`[return: Description]`** | Propagated into `ReturnJsonSchema` as `"description"`. | Not currently propagated — `ReturnJsonSchema` reflects the CLR return type only. The method-level `[Description]` *is* propagated into `AIFunction.Description`. | Generator gap; tracked for a follow-up. |
| 14 | **`[DefaultValue]` attribute** | Read and used when no C# default is present; also overrides a C# default when both are specified. | Only C# defaults (`p = value`) are honored. | Generator gap; if you need `[DefaultValue]`, annotate with `= value` in the method signature instead. |
| 15 | **`[DisplayName]` attribute on methods** | Used as the tool name when no explicit name is provided. | Not consulted — use `[ExportAIFunction("explicit_name")]`. | Style choice: one attribute rather than two to look up. |
| 16 | **Name cleanup for local functions / lambdas** | Strips compiler-generated prefixes and `Async` suffixes. | Not applicable (see (1)). Tool names are exactly what you pass to `[ExportAIFunction(...)]` or the method name. | — |
| 17 | **`IAsyncEnumerable<T>` return type** | Buffered and JSON-serialized to an array. | Returned as-is. The consumer (e.g. `FunctionInvokingChatClient`) will JSON-serialize whatever type it receives. | Generator gap; if you need this shape, materialize to an array/list in the method body. |
| 18 | **Generic methods** | Supported if the generic arguments are bound. | Hard error `MAUIAI004` at compile time. | We'd need to pick concrete type arguments at generator time; declare the specialized overload instead. |
| 19 | **`ref` / `out` / `in` / `ref readonly` parameters** | Skipped with a runtime error. | Hard error `MAUIAI004` at compile time. | Not meaningful for JSON-serialized arguments. |
| 20 | **`AIFunctionFactory.CreateDeclaration(...)`** | Produces a tool that is advertised but never invocable. | Not provided. | Every generated tool is invocable. If you need a declaration-only tool, register one via `AIFunctionFactory.CreateDeclaration` and merge it into your `tools` list. |
| 21 | **`InvalidArguments_Throw`** tests (null `method`, null `target`, etc.) | Validates factory arguments at `Create` time. | Not applicable — there is no factory to pass bad arguments to. | — |
| 22 | **Invocation result shape** | Default `MarshalResult` serializes the result to `JsonElement`. | Returns the CLR object as-is (consumers typically accept both). | Tests normalize via `JsonSerializer.SerializeToElement` before comparing. |

If you encounter a behavior not covered here, please open an issue — we consider any *silent* divergence a bug.
