# AI Extensions

AI integration packages for .NET MAUI, built on [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions.

## Packages

| Package | Description |
|---------|-------------|
| [`Microsoft.Maui.AI.Attributes`](Microsoft.Maui.AI.Attributes/) | Source-generated AI tool contexts — `[ExportAIFunction]`, DI binding, AOT-safe |
| [`Microsoft.Maui.AI.Navigation`](Microsoft.Maui.AI.Navigation/) | Runtime Shell route discovery and AI-friendly navigation |

- [AI Attributes documentation](Microsoft.Maui.AI.Attributes/README.md) — API reference, samples, and equivalence rules
- [AI Navigation documentation](Microsoft.Maui.AI.Navigation/README.md) — route discovery, template URIs, and integration guide

## Samples

| Sample | Demonstrates |
|--------|-------------|
| [`AIExtensions.Sample.Hello`](../../samples/AIExtensions.Sample.Hello/) | Minimal end-to-end usage |
| [`AIExtensions.Sample.DIParameters`](../../samples/AIExtensions.Sample.DIParameters/) | DI parameter binding with `[FromServices]` |
| [`AIExtensions.Sample.Garden`](../../samples/AIExtensions.Sample.Garden/) | Full MAUI chat app with navigation, cart, approval flow |

## CI

- GitHub Actions: `ci-ai.yml`
- Solution filter: `AIExtensions.slnf`

## Requirements

- .NET 10

> ⚠️ **These packages are experimental.** APIs may change between releases.
