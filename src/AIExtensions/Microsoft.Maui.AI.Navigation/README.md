# Microsoft.Maui.AI.Navigation

> **Experimental:** APIs may change between releases.

Model-independent Shell route discovery and template-aware navigation for .NET
MAUI apps. This package does not reference `Microsoft.Extensions.AI`.

## Register routes

Use MAUI Shell's normal route registration:

```csharp
Routing.RegisterRoute("product", typeof(ProductDetailPage));
```

`ShellNavigationService` walks the live Shell hierarchy and reflects MAUI's
registered route factories to recover destination page types. It discovers
query parameters from `[QueryProperty]` on destination pages and their injected
view models. This package is experimental and does not claim trimming or AOT
compatibility for that reflective discovery.

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
