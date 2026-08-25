# Microsoft.Maui.AI.Navigation

Semantic application mapping and template-aware Shell navigation for .NET MAUI apps.
The package composes the generated `IndexedPageCatalog`, live
`RuntimePageIndexer` context, and `ShellNavigationService` so an AI assistant can
find a feature, explain its verified page path, and open the resolved destination.

Install both packages directly so the UI indexer's analyzer and build assets are
available to the app:

```xml
<PackageReference Include="Microsoft.Maui.AI.Indexer" />
<PackageReference Include="Microsoft.Maui.AI.Navigation" />
```

## Application map

Register the generated catalog and the three wayfinding services:

```csharp
builder.Services.AddSingleton<IndexedPageCatalog>(
    MyAppIndexedPageCatalog.Default);
builder.Services.AddSingleton<ShellNavigationService>();
builder.Services.AddSingleton<ICurrentPageContextProvider, RuntimePageContextProvider>();
builder.Services.AddSingleton<ApplicationMapService>();
```

`ApplicationMapService` exposes one cohesive API:

```csharp
var matches = applicationMap.Search("write a review");
var destination = matches[0].Destination;

Console.WriteLine(destination.PageName);
Console.WriteLine(destination.RouteTemplate);
Console.WriteLine(string.Join(" -> ", destination.PagePath));

var currentRoute = navigationService.GetCurrentRoute();
var currentPage = await applicationMap.CaptureCurrentPageAsync();
Console.WriteLine(currentRoute);
Console.WriteLine(currentPage?.PageName);

var result = await applicationMap.NavigateAsync(
    destination.PageName,
    new Dictionary<string, string> { ["sku"] = "seed-basil" });
```

Only pages with a reliable Shell destination are returned. Generated
`ShellContent` routes map top-level pages without reflection. Registered deep routes
map to indexed pages by destination type identity. Resolution reports unknown,
ambiguous, and missing-parameter states without moving the user.

## Trimming-safe deep-route registration

Prefer typed registration for pushed routes. It supplies destination identity,
stable parent paths, and query parameters without discovering those facts through
reflection:

```csharp
navigation.RegisterRoute<ProductDetailPage>(
    "product",
    "//main/products",
    [new QueryParameterInfo("sku", "Sku", "String")]);

navigation.RegisterRoute<ProductReviewPage>(
    "review",
    "//main/products/product",
    [new QueryParameterInfo("sku", "Sku", "String")]);
```

The runtime discovery fallback remains available for existing
`Routing.RegisterRoute` calls, but explicit metadata produces the reliable
search-to-destination mapping used by `ApplicationMapService`.

## Template-aware navigation

AI agents can still use clean paths directly:

```csharp
await navigation.NavigateAsync("//main/products/product/seed-tomato");
await navigation.NavigateAsync("//main/products/product/seed-tomato/review");
```

The nested review path resolves to one MAUI call:

```text
//main/products/product/review?sku=seed-tomato&product.sku=seed-tomato
```

## Key features

- **Semantic destination search** over generated, accessibility-first page Markdown
- **Verified page paths** from the generated home page through intermediate screens
- **Live current-screen and Shell-route context** for from-here directions and
  "this", "here", and visible-control questions
- **Typed route identity** for trimming/AOT-conscious deep-page mapping
- **Ambiguity and required-parameter validation** before navigation
- **Single-call deep navigation** with shared parameters propagated to intermediate pages
- **Back-stack correctness** from one multi-page `GoToAsync` call

The package has no dependency on `Microsoft.Maui.AI.Attributes`. Apps choose the
small AI tool surface that fits their assistant. The Garden sample exposes
`search_app_ui`, `get_app_destination`, `get_current_navigation_uri`,
`get_current_page_ui`, and `navigate_to_app_destination` through one
`AppWayfindingTools` bridge.

## Requirements

- .NET 10
- `Microsoft.Maui.Controls` 10.0.100
- A direct `Microsoft.Maui.AI.Indexer` package reference for generated app metadata

The intermediate-page parameter fix shipped in
[dotnet/maui#35432](https://github.com/dotnet/maui/pull/35432) and is included in
the [MAUI 10.0.100 release](https://github.com/dotnet/maui/releases/tag/10.0.100).

> ⚠️ **This package is experimental.** APIs may change between releases.
