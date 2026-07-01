# Microsoft.Maui.AI.Navigation

Runtime Shell route discovery and template-aware navigation for .NET MAUI apps, designed for AI agent integration.

## How it works

`ShellNavigationService` walks the Shell hierarchy and `Routing.RegisterRoute` entries at runtime to build a route table. AI agents use clean template-style URIs — the service matches path segments against known routes, extracts inline parameter values, and issues the correct sequence of `Shell.GoToAsync` calls so each page receives its parameters.

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

// Nested navigation — issues two GoToAsync calls so the back stack is correct
await navigationService.NavigateAsync("//main/products/product/seed-tomato/review");
// Step 1: //main/products/product?sku=seed-tomato
// Step 2: review?sku=seed-tomato

// Back navigation
await navigationService.NavigateAsync("..");
```

## Key features

- **Route discovery** — walks `Shell.Items` hierarchy and `Routing.RegisterRoute` entries
- **Query parameter discovery** — reflects `[QueryProperty]` on pages and view models
- **Template URI parsing** — `ParseRoute` converts `//main/products/product/seed-tomato/review` into sequential navigation steps with extracted parameters
- **Parameter propagation** — shared parameters (like `sku`) flow to all pages that accept them
- **Back-stack correctness** — first step is absolute, subsequent steps are relative, so `..` pops to the right parent
- **BuildRoute helper** — constructs multi-segment routes with Shell's dot-prefix convention for intermediate page parameters

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
- `Microsoft.Maui.Controls`

> ⚠️ **This package is experimental.** APIs may change between releases.
