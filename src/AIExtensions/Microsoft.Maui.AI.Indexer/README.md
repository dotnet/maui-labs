# Microsoft.Maui.AI.Indexer

Compile-time XAML UI indexer for .NET MAUI — generates AI-friendly semantic Markdown from your XAML pages.

## What It Does

The indexer analyzes your XAML files at build time and generates structured Markdown that describes your UI from an accessibility perspective — what a screen reader would announce. This makes your entire UI discoverable by AI agents without running the app.

> 📄 **Specification.** For the complete, implementation-independent description of every rule and
> the exact Markdown produced for any XAML page, see the
> [XAML → Markdown UI Indexer specification](../../../docs/AIExtensions/xaml-markdown-indexer-spec.md).

## Quick Start

```xml
<PackageReference Include="Microsoft.Maui.AI.Indexer" />
```

Build your project. The generator produces one `{PageName}_Indexed.g.cs` per XAML page, each containing a `const string Markdown` with the page's semantic content.

## Generated Output

For every XAML page the generator emits a `{PageName}_Indexed` class holding the
page's semantic Markdown:

```csharp
public static partial class ProductDetailPage_Indexed
{
    public const string Markdown = """
        # ProductDetailPage

        - Button: "Back" [hint: Returns to catalog]
        - Heading (level 1): "{Name}"
        - Label: "{PriceLabel}"
        - Button: "Add to Cart" → AddToCartCommand
        """;
}
```

It also emits **one aggregate class per assembly**, named `{AssemblyName}IndexedPageCatalog`,
that derives from `Microsoft.Maui.AI.Indexer.IndexedPageCatalog` and exposes every page.
No reflection or module initializers are used — the page list is a plain static
array, so it is trimming- and AOT-safe:

```csharp
// Generated as, e.g., MyAppIndexedPageCatalog : IndexedPageCatalog
public partial class MyAppIndexedPageCatalog : IndexedPageCatalog
{
    public static MyAppIndexedPageCatalog Default { get; }
    public override IReadOnlyList<IndexedPage> Pages { get; }
}
```

`IndexedPageCatalog` / `IndexedPage` are the runtime types you consume:

```csharp
public abstract class IndexedPageCatalog
{
    public abstract IReadOnlyList<IndexedPage> Pages { get; }
    public IndexedPage? FindByName(string name);
    public IndexedPage? FindByRoute(string route);
}

public sealed class IndexedPage
{
    public string Name { get; }
    public string? Route { get; }
    public string? FilePath { get; }
    public string Markdown { get; }
}
```

## Consuming the Index

The package only produces the index — **searching is the app's job**. A typical
integration exposes the index to an AI agent as a few small tools. The Garden
sample ([`Services/PageDiscovery.cs`](../../../samples/AIExtensions.Sample.Garden/Services/PageDiscovery.cs))
does exactly this, backing three tools with `MyAppIndexedPageCatalog.Default`:

```csharp
// list_app_pages — enumerate every indexed page
foreach (var page in MyAppIndexedPageCatalog.Default.Pages)
    Console.WriteLine($"{page.Name} ({page.Route}) — {page.FilePath}");

// get_page_ui — return one page's full semantic Markdown
var md = MyAppIndexedPageCatalog.Default.FindByName("ProductDetailPage")?.Markdown;

// search_ui — a lightweight in-memory RAG over the Markdown corpus
var hits = MyAppIndexedPageCatalog.Default.Pages
    .Where(p => p.Markdown.Contains(query, StringComparison.OrdinalIgnoreCase));
```

If your app spans multiple assemblies, collect each assembly's
`{AssemblyName}IndexedPageCatalog.Default.Pages` yourself and merge them — there is no
global registry, by design.

For a heavier setup you can feed each `IndexedPage.Markdown` into a real
embedding/RAG pipeline; the Markdown is stable and deterministic, so it makes a
good corpus.

## SemanticProperties

The indexer prioritizes `SemanticProperties` — the .NET 10+ recommended accessibility API:

- `SemanticProperties.Description` → overrides control text in output
- `SemanticProperties.Hint` → shown as `[hint: ...]`
- `SemanticProperties.HeadingLevel` → controls heading depth

## Requirements

- .NET 10
- MAUI workload

> ⚠️ **This package is experimental.** APIs may change between releases.
