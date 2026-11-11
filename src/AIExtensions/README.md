# AI Extensions

AI integration packages for .NET MAUI, built on [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions.

## Packages

| Package | Description |
|---------|-------------|
| [`Microsoft.Maui.AI.Attributes`](Microsoft.Maui.AI.Attributes/) | Source-generated AI tool contexts — `[ExportAIFunction]`, DI binding, AOT-safe |
| [`Microsoft.Maui.AI.Chat`](Microsoft.Maui.AI.Chat/) | Headless chat engine — streaming blocks, tools/approval/UI actions, activities, typed shared and predictive state, thread restore/retry, reasoning, media, and `[ToolBlock]` generation |
| [`Microsoft.Maui.AI.Chat.Controls`](Microsoft.Maui.AI.Chat.Controls/) | XAML block templates — `BlockTemplateSelector` picks a template for each engine block by its type, tool or role, and `BlockView` gives a template's view its block |

`Microsoft.Maui.AI.Chat` supports caller-provided `IConversationThread` persistence, history
restore, retry, and coherent clear/reset behavior. No storage provider is built in:
applications own persistence and serialization. The engine and thread contracts are deliberately
single-thread-affine and not thread-safe; callers serialize access on their owning application
thread.

- [Attributes documentation](Microsoft.Maui.AI.Attributes/README.md) — API reference, samples, and equivalence rules
- [Chat engine documentation](Microsoft.Maui.AI.Chat/README.md) — engine, blocks, persistence, typed state, and ToolBlock generation
- [Chat controls documentation](Microsoft.Maui.AI.Chat.Controls/README.md) — block templates, matches, markup extensions, and block views
- [Chat upstream notes](Microsoft.Maui.AI.Chat/UPSTREAM-CHANGES.md) — how the chat engine relates to the ASP.NET AI Components it forked from

## Samples

| Sample | Demonstrates |
|--------|-------------|
| [`AIExtensions.Sample.Hello`](../../samples/AIExtensions.Sample.Hello/) | Minimal end-to-end usage |
| [`AIExtensions.Sample.DIParameters`](../../samples/AIExtensions.Sample.DIParameters/) | DI parameter binding with `[FromServices]` |
| [`AIExtensions.Sample.Garden`](../../samples/AIExtensions.Sample.Garden/) | Full MAUI chat app with navigation, cart, approval flow |
| [`AIExtensions.Sample.ChatPlayground`](../../samples/AIExtensions.Sample.ChatPlayground/) | One chat on the engine and the block templates over Azure, Apple, Gemini and Windows `IChatClient`s: tools and approvals, a calculator worksheet that folds calls into one card, a `[ToolBlock]` card, a UI action, reasoning, images, structured output, notes shared with the model as shared and predictive state, and a saved conversation; portable tests |

## CI

- GitHub Actions: `ci-ai.yml`
- Solution filters: `AIExtensions.Packages.slnf` holds the shipping packages (including the chat controls)
  and their tests, with the ChatPlayground tests. It needs no MAUI workloads, because the controls are a plain
  `net10.0` library that references the `Microsoft.Maui.Controls` package. Both the CI build job and the official
  packaging job use it. `AIExtensions.slnf` adds the ChatPlayground sample, which separate CI jobs build on
  Xcode 27 (macOS) and Windows with the workloads and native dependencies it needs.

## Requirements

- .NET 10

> ⚠️ **These packages are experimental.** APIs may change between releases.
