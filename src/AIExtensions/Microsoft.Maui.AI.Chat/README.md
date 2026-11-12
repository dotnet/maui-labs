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
foreach (var text in turn.ResponseBlocks.OfType<RichContentBlock>())
    Console.WriteLine(text.RawText);
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
When one call of a response needs approval, `FunctionInvokingChatClient` turns every call of the
response into an approval request, and marks the requests of tools that need none with
`RequiresConfirmation` false. The engine shows those requests as the calls they carry and approves
them itself, so only the calls that need approval wait for the user. The others run with the batch
once those are decided, also when they are rejected.

Actions that must run in the app, such as reading the current location or asking the user to pick
something, are registered with `options.RegisterUIAction(function, mode)`. `Automatic` actions are
invoked by the engine on the caller's thread; `Manual` actions wait until the app calls
`UIActionBlock.InvokeAsync()`. An action that throws sends an error result to the model instead of
failing the turn. Canceling the token passed to `InvokeAsync` cancels only that invocation; the
action can be invoked again. Once a turn stops or fails, its approvals and UI actions accept no
decision or invocation, and an action that was still running shows no result when it returns. Once a
turn ends, whether it completed, stopped or failed, each of its calls has a result or is marked
`IsAbandoned`, and a call's `Approval` is the approval that wraps it (see
[Binding from XAML](#binding-from-xaml)). `IsAwaitingInput` on both blocks says whether they can still be answered: true for
a pending approval and for a manual action that has not run, false once answered, while the action runs,
and after a stop, failure or restore. The block raises `PropertyChanged` for it, so templates can bind
their buttons to it. A server call that `FunctionInvokingChatClient` hands back uninvoked, because a UI
action shared its batch, gets an error result in the continuation, so the provider accepts it; the block
that shows the call receives that result like any other. A UI action requested together with a tool that
needs approval still runs as a UI action. A UI action wrapped in `ApprovalRequiredAIFunction` needs `FunctionInvokingChatClient` too
(sending throws without it, because nothing would ask for the approval). It shows a
`ToolApprovalBlock` and runs once approved, whatever its mode; edit the approval's `Call.Arguments`
before approving to supply input.

Providers that number their calls per response reuse call ids. Blocks, UI actions, approved UI
actions and the history handle that (a call without a result is left out of the history, not an
earlier call with its id), but `FunctionInvokingChatClient` matches the approvals it carries out by call id
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
- `ToolApprovalBlock` human-in-the-loop flow with single-use decisions. Only calls that need
  approval ask for it, also when the model batches them with calls that need none.
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
  with the exception in `ConversationTurn.Error` and can be retried, and `ClearAsync` discards the
  active response before clearing.
- A bindable model: the session, its turns, their blocks and the typed state implement
  `INotifyPropertyChanged`, and `Turns`, `RequestBlocks` and `ResponseBlocks` are observable collections.

## Binding from XAML

The session, its turns, their blocks and the typed state raise the standard .NET change events, so a XAML
list can bind to them directly: a `CollectionView` with a `DataTemplateSelector`, `BindableLayout`, or a
third-party list.

- `AgentContext` raises `PropertyChanged` for `Status` and `Error`. `Turns` raises `CollectionChanged`:
  an `Add` when a send adds a turn, and one `Reset` once a restore loaded every turn or `ClearAsync`
  removed them.
- `ConversationTurn` raises `PropertyChanged` for `IsStopped`, `Error` and `IsFailed`, `ModelId` and
  `Usage`. A failed turn keeps its `Error` until a retry runs it again, while `AgentContext.Error` only
  reports the last attempt, so every failed turn can show why it failed. Its `RequestBlocks` and
  `ResponseBlocks` raise an `Add` per block, and `ResponseBlocks` a `Reset` when `RetryAsync` removes
  the blocks of the failed attempt.
- Blocks raise `PropertyChanged` for the properties that changed, such as `RawText` and `Content` while
  text streams, a reasoning block's `Text`, a tool call's `Result` and `HasResult`, an approval's
  `Status`, a UI action's `IsComplete`, and `IsAwaitingInput`. A block of your own type, including a
  `[ToolBlock]`, raises it with an empty name (every property may have changed) whenever its handler
  updates it, and after the engine sets its result or abandons it; raise specific names with
  `SetProperty` or `OnPropertyChanged`.
- A tool call's block tells its card everything, so the card never searches the session.
  `FunctionInvocationContentBlock.HasResult` turns true when the result arrives, and `IsAbandoned` when
  the turn ends without one: it completed, stopped or failed, or left the call's approval or UI action
  unanswered. A restored call without a recorded result is abandoned too. `Approval` is the
  `ToolApprovalBlock` that wraps the call, or null: bind `Approval.IsAwaitingInput` to show that the call
  waits for approval, and check `Approval.Status` for a rejection, whose result only says that the call
  was rejected. Approvals and UI actions forward `IsAbandoned` like their call's other properties.
- `UIAgent<TState>.State` raises `PropertyChanged` for `Value` and `HasPendingPredictiveState`, so a view
  can bind to `State.Value` and show a pending prediction. Assigning `Value` raises it even for the current
  instance, so after changing the value in place, assign it again to refresh bindings to its properties
  (`State.Value.Count`).

Bindings follow the standard .NET contract: a binding refreshes when the property it reads raises
`PropertyChanged`. Two kinds of binding keep their first value: a binding to a block itself (`{Binding .}`, or
`{Binding Converter=…}` over the whole block), which no property change refreshes, and a binding that hands a
view an object or list that a block changes in place (`ItemsSource="{Binding Rows}"`), because reading the
property again returns the same instance. Put logic over the whole block in a property of the block, or handle
the block's `PropertyChanged` in code, as `BlockView.OnBlockChanged` in `Microsoft.Maui.AI.Chat.Controls` does;
make a list that a block of your own changes in place an `ObservableCollection<T>`.

`Turns`, `RequestBlocks` and `ResponseBlocks` are `ReadOnlyObservableCollection<T>`; cast one to
`INotifyCollectionChanged` to subscribe from code. A page whose `BindingContext` is the session can list
its turns, each laying out its blocks with a template per kind of block. `Microsoft.Maui.AI.Chat.Controls`
provides the template selector, and its `chat` XAML namespace
(`http://schemas.microsoft.com/dotnet/2026/maui/ai/chat`) maps the types of both packages:

```xml
<CollectionView ItemsSource="{Binding Turns}" ItemsUpdatingScrollMode="KeepLastItemInView">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="chat:ConversationTurn">
            <VerticalStackLayout>
                <VerticalStackLayout BindableLayout.ItemsSource="{Binding RequestBlocks}"
                                     BindableLayout.ItemTemplateSelector="{StaticResource BlockTemplates}" />
                <VerticalStackLayout BindableLayout.ItemsSource="{Binding ResponseBlocks}"
                                     BindableLayout.ItemTemplateSelector="{StaticResource BlockTemplates}" />
                <Label Text="Stopped" IsVisible="{Binding IsStopped}" />
            </VerticalStackLayout>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

The selector, in the page's resources, uses the first template whose `When` matches a block; a block that no
template matches shows nothing:

```xml
<chat:BlockTemplateSelector x:Key="BlockTemplates">
    <chat:BlockTemplate When="{chat:Is chat:RichContentBlock}">
        <DataTemplate x:DataType="chat:RichContentBlock">
            <Label Text="{Binding RawText}" />
        </DataTemplate>
    </chat:BlockTemplate>
    <chat:BlockTemplate When="{chat:Is chat:FunctionInvocationContentBlock}">
        <DataTemplate x:DataType="chat:FunctionInvocationContentBlock">
            <VerticalStackLayout>
                <Label Text="{Binding ToolName, StringFormat='Called {0}'}" />
                <Label Text="{Binding Result.Result}" IsVisible="{Binding HasResult}" />
                <Label Text="No result" IsVisible="{Binding IsAbandoned}" />
            </VerticalStackLayout>
        </DataTemplate>
    </chat:BlockTemplate>
</chat:BlockTemplateSelector>
```

The ChatPlayground sample (`samples/AIExtensions.Sample.ChatPlayground`) builds its whole chat this way.

Events are raised synchronously on the thread that makes the change, once the state they report is in
place; the engine has no dispatcher. Drive the session from the UI thread: send, retry, cancel and clear
there, and the response streams on the UI thread through its synchronization context, so bindings update
there too. Handlers may call back into the session, for example to send the next message when `Status`
becomes `Idle`. A handler that runs after such a send reads the new send's `Status`, as `PropertyChanged`
reports the current value; the turn that ended keeps `IsFailed`, `Error` and `IsStopped`, which say how
it ended, and a status that changed back before it was raised (a retry started from the failed turn's
`IsFailed`) is not raised at all. A handler of a turn's addition, of a retry's removal of blocks or of the `Turns` reset
cannot start a send or retry (the returned task fails with `InvalidOperationException`), and nothing can
send while a restore runs, including a handler of a state change the restore replays; send from a status
change instead. `ClearAsync` resets the typed state last, so a handler of that change can send the first
message of the cleared conversation.

These events are the only change notifications: upstream's `OnChanged` and `RegisterOn…` callbacks are not
part of this package.

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

A required `ToolMode` (`ChatToolMode.RequireAny` or a required function) applies to the first request of a
turn only. The rounds that send the results of UI actions or approvals drop it, as
`FunctionInvokingChatClient` does after its first iteration, so the model can answer the results instead
of calling a tool again. A turn sends at most `UIAgentOptions.MaximumRoundsPerTurn` rounds (40, the default
of `FunctionInvokingChatClient.MaximumIterationsPerRequest`); when the model keeps asking for UI actions or
approvals, the turn fails with an `InvalidOperationException` before the last round's actions run, and
`RetryAsync` starts it again with all of its rounds.

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
or projections spanning multiple calls. A handler that claims function calls with `MarkHandled` shows
them, whatever its block: a later replay of such a call is routed to the same handler instead of being
shown again, and a result that arrives in a later request round, such as the error result for a server
call that `FunctionInvokingChatClient` handed back uninvoked, is handed to the handler with the block's
state, so a block with a row per call can fill in every row. A call is answered once a result for it
arrives, whether or not a handler shows the result; a later call that reuses its id, as from a provider
that numbers its calls per response, is a new call, which the handler that claims it receives with its
own result, even when the same block claimed the earlier call.

## Conversation persistence

Set `UIAgentOptions.Thread` to an application-provided `IConversationThread`. The engine stores
raw updates, not rendered block snapshots. Restore replays those updates through the currently
registered handlers and state mapper.

No storage provider ships. Implementations own serialization and storage. Custom block
discriminators must survive serialization; `RawRepresentation` is not durable unless explicitly
persisted. The engine writes a thread only when a turn commits (`AppendUserMessage`, then
`AppendUpdate` for each update, then `CompleteTurn`), so failed or discarded turns never reach it.
A stopped turn is committed with its partial output. The thread keeps every update of a turn,
including tool calls that never got a result and a stopped turn's unanswered approvals, so a restore
shows them as the turn ended them; the history sent to the model leaves them out, live and restored,
because a provider rejects them or `FunctionInvokingChatClient` would run them, as well as reasoning
that a stop cut off, which stays on screen. Threads must round-trip
`ChatResponseUpdate.AdditionalProperties`, which mark continuation rounds and stopped turns; string
values may come back as the `JsonElement`s that JSON deserialization gives. A thread that drops them
restores a stopped turn as completed, with its cut-off reasoning in the history, but still leaves its
unanswered approvals out. Call `RestoreAsync` to load a thread's turns and message history; until it has loaded
them, a send fails and `ClearAsync` waits for it. Restored approvals
and UI actions are display history, not resumable pending work: they accept no decision or
invocation, and their `IsAwaitingInput` is false. A restored call without a recorded result is
`IsAbandoned`.

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

`UIAgent`, `AgentContext`, blocks, handlers, and `IConversationThread` are deliberately
single-thread-affine and **not thread-safe**. Serialize access and dispatch to your owning
application thread when entering from background work. The library does not add locks or
concurrent-caller guarantees. Change events are raised synchronously on the thread that makes the change,
so a UI drives the session from its UI thread.

## Rich text scope

The built-in handler produces `ParagraphNode` and `TextNode` only. The additional rich node types
are an extension contract for custom parsers/renderers; this package does not claim to provide a
full Markdown parser.

## Related packages

| Package | Purpose |
|---|---|
| `Microsoft.Maui.AI.Chat.Controls` | XAML block templates that show the engine's blocks with your own views |
| `Microsoft.Maui.AI.Attributes` | AOT-friendly source-generated `AIFunction` tools |

## Requirements

- .NET 10
- `Microsoft.Extensions.AI` provider of your choice
