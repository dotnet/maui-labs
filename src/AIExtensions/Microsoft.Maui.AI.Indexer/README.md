# Microsoft.Maui.AI.Indexer

Hybrid UI indexer for .NET MAUI — generates AI-friendly semantic Markdown for the
whole XAML app at compile time, then optionally augments it with the visible state
of the current page at runtime.

## What It Does

The indexer has two complementary views:

- **Compile-time catalog** — analyzes every XAML file and generates structured
  accessibility-first Markdown for the whole app. This makes every screen
  discoverable without running the app.
- **Runtime current-page snapshot** — reads the currently presented, materialized
  MAUI page and adds resolved labels, visible dynamic branches, focus, and live
  control state. This answers questions such as "what is this text box for?"

The runtime snapshot is additive. It never replaces or changes the deterministic
compile-time catalog.

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
    public string? EntryPageName { get; }
    public string? EntryPageTypeName { get; }
    public IndexedPage? Home { get; }
    public IndexedPage? FindByName(string name);
}

public sealed class IndexedPage
{
    public string Name { get; }
    public string TypeName { get; }
    public string? FilePath { get; }
    public string Markdown { get; }
    public IReadOnlyList<string> Routes { get; }
    public string? Route { get; }
}
```

## Consuming the Index

The package produces the deterministic indexes. Apps can consume them directly:

```csharp
foreach (var page in MyAppIndexedPageCatalog.Default.Pages)
    Console.WriteLine($"{page.Name} — {page.Route ?? "not directly routable"}");

var md = MyAppIndexedPageCatalog.Default.FindByName("ProductDetailPage")?.Markdown;
```

For a unified wayfinding service, use `Microsoft.Maui.AI.Navigation`.
`ApplicationMapService` composes this catalog with runtime current-page context and
Shell navigation, then searches only pages with reliable destinations. The Garden
sample's
[`AppWayfindingTools`](../../../samples/AIExtensions.Sample.Garden/Services/AppWayfindingTools.cs)
exposes that service as four focused AI tools.

If your app spans multiple assemblies, collect each assembly's
`{AssemblyName}IndexedPageCatalog.Default.Pages` yourself and merge them — there is no
global registry, by design.

For a heavier setup you can feed each `IndexedPage.Markdown` into a real
embedding/RAG pipeline; the Markdown is stable and deterministic, so it makes a
good corpus.

## Augmenting with the Current Page

Use `RuntimePageIndexer` when the user asks about what is visible now:

```csharp
CurrentPageSnapshot? current = await RuntimePageIndexer.CaptureCurrentAsync();

if (current is not null)
{
    Console.WriteLine(current.PageName);
    Console.WriteLine(current.Markdown);
}
```

For example, a materialized review form can produce:

```markdown
# Current UI: ProductReviewPage

Runtime snapshot: currently visible, materialized controls and live state.

- Heading (level 1): "Write Review"
- Label: "Heirloom Tomato Seeds"
- Slider: "Rating" [hint: Slide to select 1 to 5 stars, value: 5, range: 1–5]
- Heading (level 2): "Comment (optional)"
- Editor: [placeholder: "Share your experience..."]
- Button: "Submit Review" [hint: Submits your review for this product]
```

The runtime index:

- resolves the top modal and the current Shell, navigation, tab, or flyout page;
- includes only materialized controls whose runtime visibility and opacity make
  them visible;
- reads resolved control text and semantic accessibility metadata, without
  reflection or platform handlers;
- includes useful live state such as slider values, selections, toggle state,
  focus, and disabled state;
- omits text entered into `Entry`, `Editor`, and `SearchBar` by default;
- reports inputs as `empty` or `has text; value omitted`, and redacts the current
  input text if it also appears in a description, hint, or placeholder;
- never includes password text, even when ordinary input text is explicitly
  enabled with `CurrentPageSnapshotOptions.IncludeInputText`.

When a Shell flyout is open, the snapshot uses Shell's effective flyout collection,
including `AsMultipleItems` children and current-content menu commands, plus the
current page only. Materialized custom flyout content, headers, and footers replace
or supplement the default items as MAUI presents them. The snapshot does not walk
other materialized Shell pages.

Media locations are privacy-safe by default: runtime snapshots report generic
source kinds such as `remote image`, `local image`, and `web content`, never file
paths or URI payloads that could contain signed tokens or credentials.

Use the two indexes together:

| Question | Source |
|---|---|
| "How do I get to reviews?" | Search the compile-time catalog through `ApplicationMapService` and inspect its page path |
| "What is this text box for?" | Capture the runtime `CurrentPageSnapshot` |
| "What can ever appear on this page?" | Read the compile-time `IndexedPage` |
| "What is visible on this page right now?" | Read the runtime snapshot |

`CaptureCurrentAsync` runs on the MAUI dispatcher. Apps with multiple windows can
pass a specific `Window` to `RuntimePageIndexer.Capture`; callers already on the UI
thread can also capture a specific `Page`.

## Excluding Auxiliary Chrome

An app can keep an assistant, debugger, or inspector visible beside its domain UI
without feeding that auxiliary subtree back into the AI:

```xml
<views:AssistantSidebar
    xmlns:indexer="clr-namespace:Microsoft.Maui.AI.Indexer;assembly=Microsoft.Maui.AI.Indexer"
    indexer:IndexingProperties.ExcludeWithChildren="True" />
```

`ExcludeWithChildren` removes that element and every descendant from both the
compile-time catalog and runtime snapshots. If it is set on a XAML document's root,
that document does not become an indexed page. Mark each reference/use site as well;
an unmarked reference to an excluded document is otherwise retained as an unresolved
custom-control placeholder.

This is intended only for out-of-band assistant/debug chrome that would otherwise
make the AI describe its own interface recursively. Do not use it to hide ordinary
app controls from accessibility or AI help.

## SemanticProperties

The indexer prioritizes `SemanticProperties` — the .NET 10+ recommended accessibility API:

- `SemanticProperties.Description` → overrides control text in output
- `SemanticProperties.Hint` → shown as `[hint: ...]`
- `SemanticProperties.HeadingLevel` → controls heading depth

## Requirements

- .NET 10
- MAUI workload

> ⚠️ **This package is experimental.** APIs may change between releases.
