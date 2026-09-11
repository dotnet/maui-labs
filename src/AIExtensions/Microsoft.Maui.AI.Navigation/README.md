# Microsoft.Maui.AI.Navigation

Runtime Shell route discovery and template-aware navigation for .NET MAUI apps, designed for AI agent integration.

## How it works

`ShellNavigationService` walks the Shell hierarchy and `Routing.RegisterRoute` entries at runtime to build a route table. AI agents use clean template-style URIs — the service matches path segments against known routes, extracts inline parameter values, and resolves them to one Shell URI with route-scoped query parameters.

### 1. Register the service

```csharp
builder.Services.AddSingleton<ShellNavigationService>();
```

### 2. Discover routes at runtime

```csharp
var routes = navigationService.GetRoutes();
// → RouteInfo("products", "//main/products", [])
// → RouteInfo("product", "product", [QueryParameterInfo("sku", "Sku", "String")])
```

### 3. Navigate with clean URIs

```csharp
// Template-style URI — parameter values are inline in the path
await navigationService.NavigateAsync("//main/products/product/seed-tomato");

// Nested navigation — resolves to one GoToAsync call
await navigationService.NavigateAsync("//main/products/product/seed-tomato/review");
// Resolved URI:
// //main/products/product/review?sku=seed-tomato&product.sku=seed-tomato

// Back navigation
await navigationService.NavigateAsync("..");
```

## Key features

- **Route discovery** — walks `Shell.Items` hierarchy and `Routing.RegisterRoute` entries
- **Query parameter discovery** — reflects `[QueryProperty]` on pages and view models
- **Template URI resolution** — `ResolveRoute` converts `//main/products/product/seed-tomato/review` into one valid Shell URI
- **Parameter propagation** — shared parameters (like `sku`) flow to all pages that accept them
- **Single-call navigation** — MAUI 10.0.90+ delivers route-prefixed parameters to intermediate pages, avoiding a visible intermediate navigation
- **Back-stack correctness** — one multi-page `GoToAsync` call pushes the complete stack, so `..` pops to the right parent
- **BuildRoute helper** — constructs multi-segment routes with Shell's route-prefix convention for intermediate page parameters

## AI integration

The library has no dependency on `Microsoft.Maui.AI.Attributes`. To expose routes as AI tools, create a thin wrapper:

```csharp
public sealed class AINavigationService
{
    private readonly ShellNavigationService _inner;

    public AINavigationService(ShellNavigationService inner) => _inner = inner;

    [ExportAIFunction("get_routes")]
    [Description("Lists all available navigation routes with parameters.")]
    public IReadOnlyList<RouteInfo> GetRoutes() => _inner.GetRoutes();

    [ExportAIFunction("navigate")]
    [Description("Navigate using a clean URI with inline parameter values.")]
    public Task<string> NavigateAsync(string route) => _inner.NavigateAsync(route);
}
```

## Requirements

- .NET 10
- `Microsoft.Maui.Controls` 10.0.90 or later (the package currently references 10.0.100)

The intermediate-page parameter fix shipped in [dotnet/maui#35432](https://github.com/dotnet/maui/pull/35432) and is included in the [MAUI 10.0.100 release](https://github.com/dotnet/maui/releases/tag/10.0.100).

> ⚠️ **This package is experimental.** APIs may change between releases.
