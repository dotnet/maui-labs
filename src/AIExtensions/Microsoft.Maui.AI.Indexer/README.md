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

Build your project. The generator produces one `{PageName}_UiIndex.g.cs` per XAML page, each containing a `const string Markdown` with the page's semantic content.

## Generated Output

For every XAML page the generator emits a `{PageName}_UiIndex` class holding the
page's semantic Markdown:

```csharp
public static partial class ProductDetailPage_UiIndex
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

It also emits **one aggregate class per assembly**, named `{AssemblyName}UiIndex`,
that derives from `Microsoft.Maui.AI.Indexer.UiPageIndex` and exposes every page.
No reflection or module initializers are used — the page list is a plain static
array, so it is trimming- and AOT-safe:

```csharp
// Generated as, e.g., MyApp_UiIndex : UiPageIndex
public partial class MyApp_UiIndex : UiPageIndex
{
    public static MyApp_UiIndex Default { get; }
    public override IReadOnlyList<UiPageEntry> Pages { get; }
}
```

`UiPageIndex` / `UiPageEntry` are the runtime types you consume:

```csharp
public abstract class UiPageIndex
{
    public abstract IReadOnlyList<UiPageEntry> Pages { get; }
    public UiPageEntry? FindByName(string name);
    public UiPageEntry? FindByRoute(string route);
}

public sealed class UiPageEntry
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
sample ([`Services/UiDiscovery.cs`](../../../samples/AIExtensions.Sample.Garden/Services/UiDiscovery.cs))
does exactly this, backing three tools with `MyApp_UiIndex.Default`:

```csharp
// list_app_pages — enumerate every indexed page
foreach (var page in MyApp_UiIndex.Default.Pages)
    Console.WriteLine($"{page.Name} ({page.Route}) — {page.FilePath}");

// get_page_ui — return one page's full semantic Markdown
var md = MyApp_UiIndex.Default.FindByName("ProductDetailPage")?.Markdown;

// search_ui — a lightweight in-memory RAG over the Markdown corpus
var hits = MyApp_UiIndex.Default.Pages
    .Where(p => p.Markdown.Contains(query, StringComparison.OrdinalIgnoreCase));
```

If your app spans multiple assemblies, collect each assembly's
`{AssemblyName}UiIndex.Default.Pages` yourself and merge them — there is no
global registry, by design.

For a heavier setup you can feed each `UiPageEntry.Markdown` into a real
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
