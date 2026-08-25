# AI Extensions

AI integration packages for .NET MAUI, built on [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions.

## Packages

| Package | Description |
|---------|-------------|
| [`Microsoft.Maui.AI.Attributes`](Microsoft.Maui.AI.Attributes/) | Source-generated AI tool contexts — `[ExportAIFunction]`, DI binding, AOT-safe |
| [`Microsoft.Maui.AI.Indexer`](Microsoft.Maui.AI.Indexer/) | Hybrid MAUI UI indexer — compile-time Markdown for the whole app plus runtime snapshots of the current page |
| [`Microsoft.Maui.AI.Navigation`](Microsoft.Maui.AI.Navigation/) | Model-independent Shell route discovery and template-aware navigation |
| [`Microsoft.Maui.AI.Wayfinding`](Microsoft.Maui.AI.Wayfinding/) | Curated AI tools, intent policy, semantic destination search, navigation, and optional rendered vision |

- [AI Attributes documentation](Microsoft.Maui.AI.Attributes/README.md) — API reference, samples, and equivalence rules
- [UI Indexer documentation](Microsoft.Maui.AI.Indexer/README.md) — accessibility-first UI indexing for AI agents
- [AI Navigation documentation](Microsoft.Maui.AI.Navigation/README.md) — route metadata and template URIs
- [AI Wayfinding documentation](Microsoft.Maui.AI.Wayfinding/README.md) — plug-in chat middleware and tools
- [UI Indexer specification](../../docs/AIExtensions/xaml-markdown-indexer-spec.md) — the authoritative, code-agnostic spec of the XAML → Markdown output

## Samples

| Sample | Demonstrates |
|--------|-------------|
| [`AIExtensions.Sample.Hello`](../../samples/AIExtensions.Sample.Hello/) | Minimal end-to-end usage |
| [`AIExtensions.Sample.DIParameters`](../../samples/AIExtensions.Sample.DIParameters/) | DI parameter binding with `[FromServices]` |
| [`AIExtensions.Sample.Garden`](../../samples/AIExtensions.Sample.Garden/) | Full MAUI app with persistent AI wayfinding, deep navigation, rendered chart understanding, cart, and approval flow |

## CI

- GitHub Actions: `ci-ai.yml`
- Solution filter: `AIExtensions.slnf`

## Requirements

- .NET 10

> ⚠️ **These packages are experimental.** APIs may change between releases.
