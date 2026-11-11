# Microsoft.Maui.AI.Chat

> **Experimental:** APIs may change between preview releases.

A headless conversation engine for `Microsoft.Extensions.AI`. It transforms streaming
`ChatResponseUpdate` content into observable, strongly typed blocks, shows the tool calls that
`FunctionInvokingChatClient` runs, and drives approvals, UI actions, retry, stop, typed state, and
optional conversation persistence. It has no dependency on MAUI Controls or Blazor.

It is a port of the engine in ASP.NET Core's `Microsoft.AspNetCore.Components.AI`; the differences are
listed in [UPSTREAM-CHANGES.md](https://github.com/dotnet/maui-labs/blob/main/src/AIExtensions/Microsoft.Maui.AI.Chat/UPSTREAM-CHANGES.md).

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

## Server tools and UI actions

The engine does not invoke tools itself. Tools in `ChatOptions.Tools` run inside
`FunctionInvokingChatClient`, so add it to the client pipeline:

```csharp
IChatClient client = providerClient.AsBuilder().UseFunctionInvocation().Build();
```

Sending throws `InvalidOperationException` when `ChatOptions.Tools` contains an invocable
`AIFunction` but the client has no `FunctionInvokingChatClient`. Tools wrapped in
`ApprovalRequiredAIFunction` show a `ToolApprovalBlock` and wait for `Approve()` or `Reject()`.

Actions that must run in the app, such as reading the current location or asking the user to pick
something, are registered with `options.RegisterUIAction(function, mode)`. `Automatic` actions are
invoked by the engine on the caller's thread; `Manual` actions wait until the app calls
`UIActionBlock.InvokeAsync()`. An action that throws sends an error result to the model instead of
failing the turn. Canceling the token passed to `InvokeAsync` cancels only that invocation; the
action can be invoked again. Once a turn stops or fails, its approvals and UI actions accept no
decision or invocation, and an action that was still running shows no result when it returns. `IsAwaitingInput` on both blocks says whether they can still be answered: true for
a pending approval and for a manual action that has not run, false once answered, while the action runs,
and after a stop, failure or restore. The block raises `PropertyChanged` for it, so templates can bind
their buttons to it. A UI action requested together with a tool that needs approval still runs as a UI
action. A UI action wrapped in `ApprovalRequiredAIFunction` needs `FunctionInvokingChatClient` too
(sending throws without it, because nothing would ask for the approval). It shows a
`ToolApprovalBlock` and runs once approved, whatever its mode; edit the approval's `Call.Arguments`
before approving to supply input.

Providers that number their calls per response reuse call ids. Blocks, UI actions and approved UI
actions handle that, but `FunctionInvokingChatClient` matches the approvals it carries out by call id
across the whole history: an approval whose call id already has a result earlier in the conversation
is skipped, and a later approval for the same id fails the request. Give such providers unique call ids (for example with a
middleware below `UseFunctionInvocation()` that rewrites them) when they use approvals.

A block handler or `[ToolBlock]` registered for a UI action's name shows the action's call instead of
replacing the action: its block becomes the `UIActionBlock.InnerBlock` (or the approval's inner block)
and receives the action's result, and the action still runs. The block must derive from
`FunctionInvocationContentBlock`; otherwise the action shows a plain call.

## Features

- Streaming text projected into `TextContentBlock : RichContentBlock`, one block per message.
- Blocks follow the order of the contents that produced them, also when a complete (non-streaming)
  response or a restore delivers a whole message at once.
- Minimal paragraph/text rich-content AST plus an extensible rich-node vocabulary.
- Function call/result correlation, including batched out-of-order results. Each call id is shown
  once per turn, even when `FunctionInvokingChatClient` replays an approved call; informational
  calls (`InformationalOnly`, for example handled by a server or restored) are shown without being
  invoked.
- `ToolApprovalBlock` human-in-the-loop flow with single-use decisions.
- Automatic or manually invoked `UIActionBlock` client actions, with no false human-input pause
  for automatic actions.
- Extensible `ActivityContentBlock`/`ActivityHandler<TBlock>` support for provider activity
  snapshots and deltas (handlers are opt-in).
- Reasoning and protected-reasoning blocks, one per reasoning item (for example each reasoning
  summary).
- One `MediaContentBlock` per media item (`DataContent` or `UriContent`), including hosted
  image-generation outputs; streamed partial images update their block in place.
- Custom streaming handlers and many-to-one aggregate blocks.
- `[ToolBlock]`, `[ToolParameter]`, and `[ToolResult]` source generation for simple 1:1 tool blocks.
- `UIAgent<TState>` and `StateMapperContext` for typed inbound state; a state mapper can read and
  merge the current state through `UIAgentOptions<TState>.State`.
- `IConversationThread` restore/retry/stateful-provider support.
- `ConversationTurn.ModelId` (the last model id the provider reported) and `ConversationTurn.Usage`
  (token usage summed over the turn's requests), live and restored.
- Turn transactions: `CancelAsync` stops a response and keeps its partial output
  (`ConversationTurn.IsStopped`), failed turns commit nothing, are marked `ConversationTurn.IsFailed`
  and can be retried, and `ClearAsync` discards the active response before clearing.
- A bindable model: the session, its turns, their blocks and the typed state implement
  `INotifyPropertyChanged`, and `Turns`, `RequestBlocks` and `ResponseBlocks` are observable collections.

## Binding from XAML

The session, its turns, their blocks and the typed state raise the standard .NET change events, so a XAML
list can bind to them directly: a `CollectionView` with a `DataTemplateSelector`, `BindableLayout`, or a
third-party list.

- `AgentContext` raises `PropertyChanged` for `Status` and `Error`. `Turns` raises `CollectionChanged`:
  an `Add` when a send adds a turn, and one `Reset` once a restore loaded every turn or `ClearAsync`
  removed them.
- `ConversationTurn` raises `PropertyChanged` for `IsStopped`, `IsFailed`, `ModelId` and `Usage`. Its
  `RequestBlocks` and `ResponseBlocks` raise an `Add` per block, and `ResponseBlocks` a `Reset` when
  `RetryAsync` removes the blocks of the failed attempt.
- Blocks raise `PropertyChanged` for the properties that changed, such as `RawText` and `Content` while
  text streams, a reasoning block's `Text`, a tool call's `Result` and `HasResult`, an approval's
  `Status`, a UI action's `IsComplete`, and `IsAwaitingInput`. A block of your own type, including a
  `[ToolBlock]`, raises it with an empty name (every property may have changed) whenever its handler
  updates it; raise specific names with `SetProperty` or `OnPropertyChanged`.
- `UIAgent<TState>.State` raises `PropertyChanged` for `Value` and `HasPendingPredictiveState`, so a view
  can bind to `State.Value` and show a pending prediction. Assigning `Value` raises it even for the current
  instance, so assign the value again after changing it in place.

`Turns`, `RequestBlocks` and `ResponseBlocks` are `ReadOnlyObservableCollection<T>`; cast one to
`INotifyCollectionChanged` to subscribe from code. A page whose `BindingContext` is the session can list
its turns, each showing its response blocks with a template per block type (`chat` maps
`clr-namespace:Microsoft.Maui.AI.Chat;assembly=Microsoft.Maui.AI.Chat`):

```xml
<CollectionView ItemsSource="{Binding Turns}">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="chat:ConversationTurn">
            <VerticalStackLayout BindableLayout.ItemsSource="{Binding ResponseBlocks}"
                                 BindableLayout.ItemTemplateSelector="{StaticResource BlockTemplates}" />
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

The selector, in the page's resources:

```xml
<local:BlockTemplateSelector x:Key="BlockTemplates">
    <local:BlockTemplateSelector.Text>
        <DataTemplate x:DataType="chat:RichContentBlock">
            <Label Text="{Binding RawText}" />
        </DataTemplate>
    </local:BlockTemplateSelector.Text>
    <local:BlockTemplateSelector.ToolCall>
        <DataTemplate x:DataType="chat:FunctionInvocationContentBlock">
            <Label Text="{Binding ToolName, StringFormat='Called {0}'}" />
        </DataTemplate>
    </local:BlockTemplateSelector.ToolCall>
    <local:BlockTemplateSelector.Other>
        <DataTemplate>
            <ContentView />
        </DataTemplate>
    </local:BlockTemplateSelector.Other>
</local:BlockTemplateSelector>
```

```csharp
public class BlockTemplateSelector : DataTemplateSelector
{
    public DataTemplate Text { get; set; } = null!;
    public DataTemplate ToolCall { get; set; } = null!;
    public DataTemplate Other { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
    {
        RichContentBlock => Text,
        FunctionInvocationContentBlock => ToolCall,
        _ => Other,
    };
}
```

Events are raised synchronously on the thread that makes the change, once the state they report is in
place; the engine has no dispatcher. Drive the session from the UI thread: send, retry, cancel and clear
there, and the response streams on the UI thread through its synchronization context, so bindings update
there too. Handlers may call back into the session, for example to send the next message when `Status`
becomes `Idle`. A handler that runs after such a send reads the new send's `Status`; use
`RegisterOnStatusChanged` when you need the status each change reported. A handler of a turn's addition,
of a retry's removal of blocks or of the `Turns` reset cannot start a send or retry (the returned task
fails with `InvalidOperationException`), and nothing can send while a restore runs, including a handler of
a state change the restore replays; send from a status change instead. `ClearAsync` resets the typed
state last, so a handler of that change can send the first message of the cleared conversation.

The `OnChanged` and `RegisterOn…` callbacks remain available.

## Changing options between sends

`UIAgentOptions.ChatOptions` is read for every request. Keep the instance and change it between
sends, for example `Tools`, `ToolMode`, `ResponseFormat` or `ModelId`, and the next send uses the
new values with the same history:

```csharp
var chatOptions = new ChatOptions { ModelId = "gpt-4.1" };
var session = new AgentContext(new UIAgent(client, chatOptions));

await session.SendMessageAsync("Hello");
chatOptions.ModelId = "gpt-4.1-mini";
chatOptions.Tools = null;
await session.SendMessageAsync("Answer briefly");   // same history, new model, no tools
```

To compute options for every request instead, add `ConfigureOptions` to the chat client pipeline
(before `UseFunctionInvocation` when it changes tools).

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

Mapped properties need a public or internal setter that is not `init`-only (MAUIAI107). Values of
type `string`, `int`, `long`, `double`, `float`, `decimal` and `bool` (and their nullable forms) are
read from JSON without reflection; values the model sends as strings, such as `"5"`, are parsed with
the invariant culture. A block whose properties all have these types uses no reflection, so it works
with trimming and NativeAOT without warnings. It reads several results, or a named result, from a JSON
result: a `JsonElement` (what `AIFunctionFactory` functions return) or a string that holds JSON. Other
result objects are left unread. Other property types are deserialized with reflection-based
`System.Text.Json`, and a block with such a property also reads other result objects that way. A value
that cannot be converted leaves its property unchanged.

Use handwritten `ContentBlockHandler<TState>` implementations for aggregation, custom events,
or projections spanning multiple calls.

## Conversation persistence

Set `UIAgentOptions.Thread` to an application-provided `IConversationThread`. The engine stores
raw updates, not rendered block snapshots. Restore replays those updates through the currently
registered handlers and state mapper.

No storage provider ships. Implementations own serialization and storage. Custom block
discriminators must survive serialization; `RawRepresentation` is not durable unless explicitly
persisted. The engine writes a thread only when a turn commits (`AppendUserMessage`, then
`AppendUpdate` for each update, then `CompleteTurn`), so failed or discarded turns never reach it.
A stopped turn is committed with its partial output, without tool calls that never got a result;
its history also leaves out reasoning that the stop cut off, which stays on screen.
Threads must round-trip `ChatResponseUpdate.AdditionalProperties`, which mark continuation rounds and
stopped turns. Call `RestoreAsync` to load a thread's turns and message history; until it has loaded
them, a send fails and `ClearAsync` waits for it. Restored approvals
and UI actions are display history, not resumable pending work: they accept no decision or
invocation, and their `IsAwaitingInput` is false.

To show and continue a conversation that no thread wrote, such as a recording or an import, pass its
messages to `AgentContext.RestoreAsync(messages)` (or `UIAgent.RestoreAsync(messages)`) on a context
without a thread:

```csharp
await session.RestoreAsync(recordedMessages);   // renders turns; invokes nothing
await session.SendMessageAsync("Follow-up");    // continues with the recorded history
```

Each user message starts a turn unless it only answers approvals or tool calls. Recorded tool calls
show their recorded results, system messages are sent with the history but not shown, and tool calls
without a recorded result are shown but left out of the history.

## State and provider metadata

`StateMapperContext` maps inbound updates to application state. It runs for every configured
update; call `MarkHandled` only for content that should not be rendered. `ChatOptions.RawRepresentationFactory`
is retained when the engine adds a stateful thread's conversation id or UI-action declarations to the
request options. It is transport-neutral request customization, not a provider API or dependency.

## Threading contract

`UIAgent`, `AgentContext`, blocks, handlers, callbacks, and `IConversationThread` are deliberately
single-thread-affine and **not thread-safe**. Serialize access and dispatch to your owning
application thread when entering from background work. The library does not add locks or
concurrent-caller guarantees. Change events and callbacks are raised synchronously on the thread that
makes the change, so a UI drives the session from its UI thread.

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
