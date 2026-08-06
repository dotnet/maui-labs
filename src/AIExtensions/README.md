# AI Extensions

AI integration packages for .NET MAUI, built on [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions.

## Packages

| Package | Description |
|---------|-------------|
| [`Microsoft.Maui.AI.Attributes`](Microsoft.Maui.AI.Attributes/) | Source-generated AI tool contexts — `[ExportAIFunction]`, DI binding, AOT-safe |
| [`Microsoft.Maui.AI.Chat`](Microsoft.Maui.AI.Chat/) | Headless chat engine — streaming blocks, tools/approval/UI actions, typed state, thread restore/retry, reasoning, and media |
| [`Microsoft.Maui.AI.Chat.Controls`](Microsoft.Maui.AI.Chat.Controls/) | Native MAUI chat UI — zero-config `CopilotChatView`, virtualized `MessageListView`, XAML block templates, multimodal input, stop/retry, and theming |

`Microsoft.Maui.AI.Chat` supports caller-provided `IConversationThread` persistence, history
restore, retry, and coherent clear/reset behavior. No storage provider is built in:
applications own persistence and serialization. The engine and thread contracts are deliberately
single-thread-affine and not thread-safe; callers serialize access on their owning application
thread.

- [Attributes documentation](Microsoft.Maui.AI.Attributes/README.md) — API reference, samples, and equivalence rules
- [Chat upstream notes](Microsoft.Maui.AI.Chat/UPSTREAM-CHANGES.md) — how the chat engine relates to the ASP.NET AI Components it forked from

## Samples

| Sample | Demonstrates |
|--------|-------------|
| [`AIExtensions.Sample.Hello`](../../samples/AIExtensions.Sample.Hello/) | Minimal end-to-end usage |
| [`AIExtensions.Sample.DIParameters`](../../samples/AIExtensions.Sample.DIParameters/) | DI parameter binding with `[FromServices]` |
| [`AIExtensions.Sample.Garden`](../../samples/AIExtensions.Sample.Garden/) | Full MAUI chat app using `CopilotChatView`, with navigation, cart, approvals, custom product cards, rich text, image generation, and template switching |
| [`AIExtensions.Sample.ChatPlayground`](../../samples/AIExtensions.Sample.ChatPlayground/) | Azure/Apple `IChatClient` requests, images and tools, automatic recording, and offline replay with portable tests |

## CI

- GitHub Actions: `ci-ai.yml`
- Solution filter: `AIExtensions.slnf` includes the MAUI playground. The portable
  `AIExtensions.Packages.slnf` includes the recording/replay tests and is used by
  the Windows official packaging job and package/test CI job. A separate macOS
  CI job builds the MAUI sample with the required workloads and native Apple
  dependencies.

## Requirements

- .NET 10

> ⚠️ **These packages are experimental.** APIs may change between releases.
