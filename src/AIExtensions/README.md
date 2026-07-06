# AI Extensions

AI integration packages for .NET MAUI, built on [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions.

## Packages

| Package | Description |
|---------|-------------|
| [`Microsoft.Maui.AI.Attributes`](Microsoft.Maui.AI.Attributes/) | Source-generated AI tool contexts — `[ExportAIFunction]`, DI binding, AOT-safe |
| [`Microsoft.Maui.AI.Chat`](Microsoft.Maui.AI.Chat/) | Chat engine — a block-mapping pipeline that turns `Microsoft.Extensions.AI` content into strongly-typed `ContentBlock`s, plus a stateful `AgentContext` |
| [`Microsoft.Maui.AI.Chat.Controls`](Microsoft.Maui.AI.Chat.Controls/) | MAUI chat UI — `CopilotChatView` / `MessageListView` with a XAML content-template system for rendering blocks |

- [Attributes documentation](Microsoft.Maui.AI.Attributes/README.md) — API reference, samples, and equivalence rules
- [Chat upstream notes](Microsoft.Maui.AI.Chat/UPSTREAM-CHANGES.md) — how the chat engine relates to the ASP.NET AI Components it forked from

## Samples

| Sample | Demonstrates |
|--------|-------------|
| [`AIExtensions.Sample.Hello`](../../samples/AIExtensions.Sample.Hello/) | Minimal end-to-end usage |
| [`AIExtensions.Sample.DIParameters`](../../samples/AIExtensions.Sample.DIParameters/) | DI parameter binding with `[FromServices]` |
| [`AIExtensions.Sample.Garden`](../../samples/AIExtensions.Sample.Garden/) | Full MAUI chat app (navigation, cart, approvals) using the `Microsoft.Maui.AI.Chat.Controls` `CopilotChatView` — custom product cards, markdown, image generation, and a fancy/plain rendering toggle |

## CI

- GitHub Actions: `ci-ai.yml`
- Solution filter: `AIExtensions.slnf`

## Requirements

- .NET 10

> ⚠️ **These packages are experimental.** APIs may change between releases.
