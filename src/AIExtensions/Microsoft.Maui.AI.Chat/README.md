# Microsoft.Maui.AI.Chat

> **Experimental:** APIs may change between preview releases.

A headless conversation engine for `Microsoft.Extensions.AI`. It transforms streaming
`ChatResponseUpdate` content into observable, strongly typed blocks and drives backend tools,
approvals, automatic UI actions, retry, cancellation, typed state, and optional conversation
persistence. It has no dependency on MAUI Controls or Blazor.

## Install

```xml
<PackageReference Include="Microsoft.Maui.AI.Chat" Version="0.1.0-preview.*" />
```

## Quick start

```csharp
IChatClient client = /* any Microsoft.Extensions.AI provider */;

var agent = new UIAgent(client, options =>
{
    options.ChatOptions = new ChatOptions
    {
        Instructions = "You are a helpful assistant."
    };
});

var session = new AgentContext(agent);
await session.SendMessageAsync("Hello!");

foreach (var turn in session.Turns)
foreach (var block in turn.ResponseBlocks)
    Console.WriteLine(block);
```

## Features

- Streaming text projected into `TextContentBlock : RichContentBlock`.
- Minimal paragraph/text rich-content AST plus an extensible rich-node vocabulary.
- Function call/result correlation, including batched out-of-order results.
- `ToolApprovalBlock` human-in-the-loop flow with single-use decisions.
- Automatic or manually invoked `UIActionBlock` client actions, with no false human-input pause
  for automatic actions.
- Extensible `ActivityContentBlock`/`ActivityHandler<TBlock>` support for provider activity
  snapshots and deltas (handlers are opt-in).
- Reasoning and protected-reasoning blocks.
- Direct media plus hosted image-generation result extraction.
- Custom streaming handlers and many-to-one aggregate blocks.
- `[ToolBlock]`, `[ToolParameter]`, and `[ToolResult]` source generation for simple 1:1 tool blocks.
- `UIAgent<TState>` and `StateMapperContext` for typed inbound state.
- `IConversationThread` restore/retry/stateful-provider support.
- Graceful `CancelAsync`, observable caller cancellation, retry, and explicit `Clear`.

## Custom tool blocks

For a simple one-call/one-result projection, annotate a partial function block:

```csharp
[ToolBlock("find_order")]
public sealed partial class OrderSummaryBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "orderId")]
    public string OrderId { get; set; } = "";

    [ToolResult]
    public Order? Order { get; set; }
}
```

Register all generated handlers in the consuming assembly:

```csharp
var agent = new UIAgent(client, options => options.AddGeneratedToolBlocks());
```

Use handwritten `ContentBlockHandler<TState>` implementations for aggregation, custom events,
or projections spanning multiple calls.

## Conversation persistence

Set `UIAgentOptions.Thread` to an application-provided `IConversationThread`. The engine stores
raw updates, not rendered block snapshots. Restore replays those updates through the currently
registered handlers and state mapper.

No storage provider ships. Implementations own serialization and storage. Custom block
discriminators must survive serialization; `RawRepresentation` is not durable unless explicitly
persisted. A thread keeps one pending turn until `CompleteTurn`; `AbortTurn` must discard that pending
turn after cancellation or failure without touching committed history. Restored approvals and UI
actions are display history, not resumable pending work.

## State and provider metadata

`StateMapperContext` maps inbound updates to application state. It runs for every configured
update; call `MarkHandled` only for content that should not be rendered. `ChatOptions.RawRepresentationFactory`
is retained when the engine adds AG-UI stateful-thread or UI-action metadata. It is
transport-neutral request customization, not a provider API or dependency.

## Threading contract

`UIAgent`, `AgentContext`, blocks, handlers, callbacks, and `IConversationThread` are deliberately
single-thread-affine and **not thread-safe**. Serialize access and dispatch to your owning
application thread when entering from background work. The library does not add locks or
concurrent-caller guarantees.

## Rich text scope

The built-in handler produces `ParagraphNode` and `TextNode` only. The additional rich node types
are an extension contract for custom parsers/renderers; this package does not claim to provide a
full Markdown parser.

## Related packages

| Package | Purpose |
|---|---|
| `Microsoft.Maui.AI.Chat.Controls` | Native MAUI chat controls and XAML templates |
| `Microsoft.Maui.AI.Attributes` | AOT-friendly source-generated `AIFunction` tools |

## Requirements

- .NET 10
- `Microsoft.Extensions.AI` provider of your choice
