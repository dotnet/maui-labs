# Microsoft.Maui.AI.Indexer

Compile-time XAML UI indexer for .NET MAUI — generates AI-friendly semantic Markdown from your XAML pages.

## What It Does

The indexer analyzes your XAML files at build time and generates structured Markdown that describes your UI from an accessibility perspective — what a screen reader would announce. This makes your entire UI discoverable by AI agents without running the app.

## Quick Start

```xml
<PackageReference Include="Microsoft.Maui.AI.Indexer" />
```

Build your project. The generator produces one `{PageName}_UiIndex.g.cs` per XAML page, each containing a `const string Markdown` with the page's semantic content.

## Generated Output

Each page produces a class like:

```csharp
[UiPageIndex("ProductDetailPage", Route = "product")]
public static partial class ProductDetailPage_UiIndex
{
    public const string Markdown = """
        # ProductDetailPage
        Route: product
        
        - Button: "Back" [hint: Returns to catalog]
        - Heading (level 1): "{Name}"
        - Label: "{PriceLabel}"
        - Button: "Add to Cart" → AddToCartCommand
        """;
}
```

An aggregate `UiIndex` class provides search across all pages.

## SemanticProperties

The indexer prioritizes `SemanticProperties` — the .NET 10+ recommended accessibility API:

- `SemanticProperties.Description` → overrides control text in output
- `SemanticProperties.Hint` → shown as `[hint: ...]`
- `SemanticProperties.HeadingLevel` → controls heading depth

## Requirements

- .NET 10
- MAUI workload

> ⚠️ **This package is experimental.** APIs may change between releases.
