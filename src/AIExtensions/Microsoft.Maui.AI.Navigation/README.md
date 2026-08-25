# Microsoft.Maui.AI.Navigation

> **Experimental:** APIs may change between releases.

Model-independent Shell route discovery and template-aware navigation for .NET
MAUI apps. This package does not reference `Microsoft.Extensions.AI`.

## Register routes

Typed registration supplies destination identity, parent paths, and parameters
without relying on reflection:

```csharp
builder.Services.AddSingleton<ShellNavigationService>();

navigation.RegisterRoute<ProductDetailPage>(
    "product",
    "//main/products",
    [new QueryParameterInfo("sku", "Sku", "String")]);
```

## Navigate

```csharp
await navigation.NavigateAsync("//main/products/product/seed-tomato");
await navigation.NavigateAsync("//main/products/product/seed-tomato/review");
```

Nested paths resolve to one MAUI 10 Shell call with route-scoped intermediate
parameters, preserving the expected back stack.

## AI integration

Use `Microsoft.Maui.AI.Wayfinding` for curated
`Microsoft.Extensions.AI` tools and intent instructions that combine this package
with `Microsoft.Maui.AI.Indexer`.

## Requirements

- .NET 10
- `Microsoft.Maui.Controls` 10.0.100

> **This package is experimental.**
