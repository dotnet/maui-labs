# AI Extensions

AI integration packages for .NET MAUI, built on [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/ai-extensions) abstractions.

## Packages

| Package | Description |
|---------|-------------|
| [`Microsoft.Maui.AI.Attributes`](Microsoft.Maui.AI.Attributes/) | Source-generated AI tool contexts — `[ExportAIFunction]`, DI binding, AOT-safe |
| [`Microsoft.Maui.AI.Chat`](Microsoft.Maui.AI.Chat/) | Headless chat engine — streaming blocks, tools/approval/UI actions, typed state, thread restore/retry, reasoning, media, and `[ToolBlock]` generation |
| [`Microsoft.Maui.AI.Chat.Controls`](Microsoft.Maui.AI.Chat.Controls/) | Native MAUI chat UI — zero-config `CopilotChatView`, virtualized `MessageListView`, XAML block templates, multimodal input, stop/retry, and theming |
| [`Microsoft.Maui.Chat.Controls`](Microsoft.Maui.Chat.Controls/) | Provider-neutral `ChatView` for human, group, and agent participants with files, audio capture, live speech, and no AI dependency |

`Microsoft.Maui.AI.Chat` supports caller-provided `IConversationThread` persistence, history
restore, retry, and coherent clear/reset behavior. No storage provider is built in:
applications own persistence and serialization. The engine and thread contracts are deliberately
single-thread-affine and not thread-safe; callers serialize access on their owning application
thread.

- [Attributes documentation](Microsoft.Maui.AI.Attributes/README.md) — API reference, samples, and equivalence rules
- [Chat engine documentation](Microsoft.Maui.AI.Chat/README.md) — engine, blocks, persistence, typed state, and ToolBlock generation
- [Chat controls documentation](Microsoft.Maui.AI.Chat.Controls/README.md) — drop-in control, templates, attachments, and customization
- [Neutral chat documentation](Microsoft.Maui.Chat.Controls/README.md) — reusable human/group chat model, controls, and templates
- [Chat upstream notes](Microsoft.Maui.AI.Chat/UPSTREAM-CHANGES.md) — how the chat engine relates to the ASP.NET AI Components it forked from

## Samples

| Sample | Demonstrates |
|--------|-------------|
| [`AIExtensions.Sample.Hello`](../../samples/AIExtensions.Sample.Hello/) | Minimal end-to-end usage |
| [`AIExtensions.Sample.DIParameters`](../../samples/AIExtensions.Sample.DIParameters/) | DI parameter binding with `[FromServices]` |
| [`ChatControls.Sample`](../../samples/ChatControls.Sample/) | Provider-neutral group chat: participants, delivery states, typing, media, attachments, custom XAML content, and theme overrides |
| [`AIExtensions.Sample.Garden`](../../samples/AIExtensions.Sample.Garden/) | Full MAUI AI chat app: Azure OpenAI, custom product/order blocks, generated `[ToolBlock]`, approvals, UI state, attachments, image generation, raw-block preview, and template switching |

## CI

- GitHub Actions: `ci-ai.yml`
- Solution filter: `AIExtensions.slnf`

## Requirements

- .NET 10

> ⚠️ **These packages are experimental.** APIs may change between releases.
