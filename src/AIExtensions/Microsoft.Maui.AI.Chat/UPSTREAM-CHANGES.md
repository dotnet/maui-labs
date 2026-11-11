# Differences from ASP.NET Core Components.AI

`Microsoft.Maui.AI.Chat` and `Microsoft.Maui.AI.Chat.Generators` are a port of the engine half of
`Microsoft.AspNetCore.Components.AI` (dotnet/aspnetcore `src/Components/AI`). The port stays close to
upstream so that fixes can be carried over cheaply. This file lists every intentional difference and
why it exists. Anything not listed here is meant to behave like upstream.

## Reference

| | |
|---|---|
| Compared against | dotnet/aspnetcore `main` at [`25e1cee7f`](https://github.com/dotnet/aspnetcore/tree/25e1cee7f159b7c2c3c0868fe5613518a6add482/src/Components/AI) (includes dotnet/aspnetcore#69445) |
| Also reviewed (unmerged) | dotnet/aspnetcore#68672 (multimodal input), dotnet/aspnetcore#70314 (MCP Apps) |
| Compared on | 2026-11-11 |

To update: diff `src/Components/AI/{src,gen,test}` between `25e1cee7f` and the new commit, port fixes
to the matching files below, port new tests from `test/` and `gen/test/`, then update this file and
the commit in `THIRD-PARTY-NOTICES.txt`.

## Where things live

| Upstream | Here |
|---|---|
| Namespace `Microsoft.AspNetCore.Components.AI` | `Microsoft.Maui.AI.Chat` |
| `src/Engine/*` | `Engine/*` |
| `src/Pipeline/*` (core) | `Pipeline/*` |
| `src/Pipeline/UIAgentOptions.cs` | `Engine/UIAgentOptions.cs` |
| `src/Pipeline/*Handler.cs` and `src/Blocks/*` | `Blocks/<Feature>/` (each block next to its handler) |
| `src/Attributes/*` | `Attributes/*` (same API) |
| `FunctionApprovalBlock`, `FunctionApprovalHandler` | `ToolApprovalBlock`, `ToolApprovalHandler` (named after M.E.AI `ToolApprovalRequestContent`) |
| `TextBlockHandler` emitting `RichContentBlock` | `TextBlockHandler` emitting `TextContentBlock : RichContentBlock` |
| `gen/` (`ToolBlockGenerator`, `ToolBlockParser`, `ToolBlockEmitter`, diagnostics `BAIC001`-`BAIC010`) | `Microsoft.Maui.AI.Chat.Generators` (`ToolBlockGenerator`, `Models`, diagnostics `MAUIAI101`-`MAUIAI111`) |
| `src/Components/*`, `wwwroot/` (Blazor) | Not ported. Native controls live in a separate MAUI package. |
| #68672 `MediaContentBlock` (internal, Blazor `RenderFragment`), `DataContentHandler`, `MediaContentFactory` | Public `MediaContentBlock` and internal `MediaContentHandler`; no rendering code |

## Kept identical

These match upstream so that upstream fixes apply directly:

- Block lifecycle, `BlockMappingPipeline` (two-phase mapping, handler entries, `BlockMappingResult<T>`),
  `BlockMappingPipelineLog`. The pipeline still reports a handler's update or completion of a block through
  the block's `NotifyChanged`, which raises `PropertyChanged` here (see Binding).
- The rich-text node vocabulary and `RichTextContent` (copies its node list), and
  `RichTextContentHandler` (last snapshot in an update wins, co-sent `TextContent` is claimed, a new
  message id starts a new block).
- `RichContentBlock` public surface: non-virtual `AppendText`, internal `Content` setter and
  `ReplaceContent`.
- Approvals arriving in separate updates each get their own block.
- Informational calls (`FunctionCallContent.InformationalOnly`) are rendered and never invoked.
- `BlockMappingContext.CreateInnerBlock` copies role, author, message id and creation time to the
  inner block and leaves it inactive.
- `StateMapperContext.UnhandledContents` is an `IEnumerable<AIContent>`; `UIAgentOptions<TState>.State`
  lets a state mapper read and merge the current state; predictive state is accepted, rejected or
  rolled back with the same rules.
- `ActivityContentBlock` and `ActivityHandler<TBlock>` (opt-in, not registered by default).
- Logging event ids 100 to 104, including 104 `ProcessingError` from #69445.
- `AgentContext` is sealed and guards its arguments. `UIAgentOptions.ChatOptions` is read for every
  request and passed through unchanged when the engine has nothing to add, so changing it between
  sends takes effect.
- `IConversationThread` has upstream's seven members (plus `Clear()`), and a failed request is never
  written to it.
- `[ToolBlock]`, `[ToolParameter]` and `[ToolResult]`; init-only setters are rejected; primitive
  parameters and results are converted without reflection.

## Differences

### Package and threading

- **Engine and UI are separate packages.** Upstream ships the engine together with its Blazor
  components. Here the engine is plain `net10.0` with no UI dependency, so it can be tested on its own
  and used by any MAUI controls package.
- **No `ConfigureAwait(false)`.** Upstream uses it throughout `UIAgent`. Here blocks change and
  notify on the caller's context, because MAUI bindings must be updated on the UI thread.
- **No locks.** Upstream locks inside `FunctionApprovalBlock` and `UIActionBlock`. The engine here is
  single-thread-affine and documents that callers serialize access. Approval and UI-action results
  still complete with `TaskCreationOptions.RunContinuationsAsynchronously` (as upstream), so
  approving never runs the next request inside the `Approve()` call.

### Binding

- **Standard .NET change notifications replace upstream's callbacks.** Upstream's Blazor components
  re-render from callbacks, which are removed here; a XAML list binds to the engine directly instead:

  | Upstream callback | Replaced by |
  |---|---|
  | `AgentContext.RegisterOnTurnAdded` | `Turns` raises `CollectionChanged`: an `Add` per sent turn, one `Reset` after a restore |
  | `AgentContext.RegisterOnBlockAdded` | each turn's `RequestBlocks` and `ResponseBlocks` raise an `Add` per block |
  | `AgentContext.RegisterOnStatusChanged` | `AgentContext` raises `PropertyChanged` for `Status` and `Error` |
  | `ContentBlock.OnChanged` (and `ContentBlockChangedSubscription`) | `ContentBlock` raises `PropertyChanged`; `NotifyChanged` raises it with an empty name |
  | `AgentState<T>.OnChanged` | `AgentState<T>` raises `PropertyChanged` for `Value` and `HasPendingPredictiveState` |

  A block's change is reported once per property that changed instead of once per handler update, and a
  status only once it differs from the status last raised (upstream calls back at every notification,
  for example `Streaming` again for each continuation round). In detail:
  - `ContentBlock`, `ConversationTurn` and `AgentContext` implement `INotifyPropertyChanged`. The
    engine's blocks raise the names of the properties that changed, and `InteractiveFunctionBlock`
    forwards its inner block's call and result properties. A block of any other type raises an empty
    name ("every property") from `NotifyChanged`, which runs when its handler updates or completes it and
    when the engine sets a call block's result or abandons it, because the engine cannot see which of its
    properties changed. Block authors get
    `OnPropertyChanged` and `SetProperty`.
  - `AgentState<T>` implements `INotifyPropertyChanged` for `Value` and `HasPendingPredictiveState`;
    assigning `Value` raises it even for the current instance, so assigning a value changed in place
    refreshes bindings to its properties.
  - `AgentContext.Turns`, `ConversationTurn.RequestBlocks` and `ResponseBlocks` are
    `ReadOnlyObservableCollection<T>` instead of `IReadOnlyList<T>`: an `Add` per turn or block, and one
    `Reset` for a restore, a clear, and a retry's removal of response blocks.
  - Events are raised synchronously on the thread that makes the change, once the state they report is
    in place; the engine has no dispatcher. `PropertyChanged` for `Status` reports the current value once
    it changed, as upstream passes each callback the current `Status`, so a handler that runs after one
    that sent the next message reads the new status. The turn that ended keeps `IsFailed`, `Error` and
    `IsStopped`, which say how it ended.
  - Handlers may re-enter. The attempt starts before a send reports its turn and before a retry removes
    its response blocks, so handlers of those changes cannot start another send or retry (upstream
    guards on the status, which changes later), and a send from a handler of the `Turns` reset is
    rejected, so every handler of a collection change sees it before the next change. A clear resets the
    typed state after it removed the turns, so a handler of that change can start the cleared
    conversation.

### Turns, history and the thread

- **One owner for a turn.** `UIAgent` keeps the committed history and a pending turn (request
  messages, response messages, raw updates and tool calls). Nothing reaches the history or the thread
  until the turn commits. Upstream adds the request to the history before streaming and does not
  remove it when the request fails, so a retry sends it twice.
- **One thread turn per conversation turn.** On commit the engine writes `AppendUserMessage`, then
  every update of every request round, then `CompleteTurn`. Messages sent in continuation rounds
  (approval responses, UI-action results) are stored as updates marked with
  `Microsoft.Maui.AI.Chat.Persistence.IsContinuation.v1`. Upstream writes each continuation round as
  a new user turn. Keeping one turn lets restore rebuild the same `ConversationTurn`s the user saw.
- **Smaller thread contract.** `IConversationThread` has upstream's seven members plus `Clear()`.
  Earlier previews also had `AbortTurn()` and `GetMessageHistory()`; they are gone because nothing
  pending is ever written and history is rebuilt from `GetUpdates()`, as upstream does.
- **Stopping keeps the partial answer.** `CancelAsync` (and the caller's token) stops the response and
  completes once it has unwound, as in #68672. The turn then commits with its request and partial
  output, minus tool calls and approvals that never got a result, and is marked
  `ConversationTurn.IsStopped`; the thread stores it with
  `Microsoft.Maui.AI.Chat.Persistence.IsStopped.v1`. A stopped turn does not advance a stateful
  provider's conversation id. Upstream main only requests cancellation, and the stopped round is never
  written.
- **What a stopped turn shows is what it keeps.** A round records and shows its request before it
  checks for a stop, so a turn stopped before its request was sent still shows and commits the
  request, and every update a round recorded is shown in full. Reasoning that the stop cut off
  (reasoning that ends the turn's last message) is still shown and stored in the thread, but left out
  of the history, live and restored, because providers can reject reasoning that no output follows.
- **Errors discard the turn, retry resends all of it.** A failed turn commits nothing and keeps its
  partial blocks on screen. `RetryAsync` clears the turn's response blocks (`ResponseBlocks` raises a
  `Reset`, which tells a list to drop them) and sends the whole turn again. #68672
  instead retries only the failed round and truncates that round's blocks; that would resend an
  approval and run its tool again without asking.
- **`ConversationTurn.IsFailed` and `ConversationTurn.Error`.** Not in upstream. A turn whose attempt
  failed is marked, and keeps the exception that failed it, until `RetryAsync` runs it again, so a view
  can tell the failed turn (and an earlier one that was never retried because another message was sent)
  and show why it failed, without catching the status change that reported it. `AgentContext.Error`
  only reports the last attempt, and the next send clears it. The turn raises `Error` before
  `IsFailed`, both before the context's `Error` and `Status`.
- **`ClearAsync`.** Not in upstream. It waits for the active response to stop, and for a restore in
  progress to finish, then clears the turns, the history, the thread and the typed state, so a response
  that was still running cannot change the cleared conversation.
- **Turn ids** are the request message id. Upstream leaves `ConversationTurn.Id` empty.
- **Per-turn metadata.** `ConversationTurn.ModelId` (the last model id reported by the provider) and
  `ConversationTurn.Usage` (token usage summed over the turn's requests) are not in upstream. Both are
  restored from the thread.

### Restore

- **Grouping.** Each user message starts a turn, except a user message that only answers approvals or
  tool calls, which continues the previous turn. Upstream main starts a turn at every user-role block,
  so a message with text and an image becomes two turns; #68672 groups by update.
- **Transactional.** Restore rebuilds history and typed state from the initial state and rolls both
  back if it fails. Predictive state found in the thread is rejected rather than revived. System
  messages are kept in the history but not shown.
- **Restore from messages.** `AgentContext.RestoreAsync(IEnumerable<ChatMessage>)` and
  `UIAgent.RestoreAsync(IEnumerable<ChatMessage>)` show a recorded or imported conversation and make it
  the history for the next send. Recorded tool calls show their recorded results and are never
  invoked; calls without a result and their approvals are shown but left out of the history. Upstream
  can only restore from a thread.
- **Announced.** Once all restored turns are loaded, `Turns` raises one `Reset`, with every turn's blocks in
  place, so a native view that already shows the context renders them. The turns' block collections and
  the status raise nothing. Upstream restores silently and relies on the Blazor component re-rendering
  after the call.
- **Exclusive.** Until the restored turns are loaded, a send fails and `ClearAsync` waits for the
  restore, so neither a send nor a clear interleaves with it, including one started by a handler of a
  typed-state change the restore replays. Upstream does not guard a restore.

### Tools, UI actions and approvals

- **Server tools need `FunctionInvokingChatClient`,** as upstream. Upstream silently never runs a tool
  when it is missing; here `SendMessageAsync` throws `InvalidOperationException` when `ChatOptions.Tools`
  contains an invocable `AIFunction`, or a registered UI action is an `ApprovalRequiredAIFunction`, and
  the client pipeline has no `FunctionInvokingChatClient` (which is what asks for approvals).
- **Calls left unanswered in a continuation round** get an error result. When one call of a batch
  targets a UI action, `FunctionInvokingChatClient` hands the whole batch back, and providers reject a
  continuation that leaves a call without a result. The calls are found from the round's calls and
  results (a call id reused after its result is a new call), not from the blocks that show them, so a
  call that a handler of the app's own folds into its block is answered too. The error result reaches
  the block through the next round's request, like any result.
- **Follow-up rounds drop a required tool mode, and a turn has a round limit.** Not in upstream, which
  sends every round of a turn with the same options and drives rounds without a limit. The rounds that
  send the results of UI actions or approvals send a `RequiredChatToolMode` as no tool mode, as
  `FunctionInvokingChatClient` does after its first iteration; otherwise every round must call a tool and
  an automatic UI action loops until stopped, one paid request per round.
  `UIAgentOptions.MaximumRoundsPerTurn` (default 40, as `FunctionInvokingChatClient.MaximumIterationsPerRequest`)
  fails the turn with an `InvalidOperationException` once it used all of its rounds, before the last
  round's UI actions run. `FunctionInvokingChatClient` instead makes one last request without function
  tools; a UI action needs another round to send its result, so the engine stops before running it.
- **One block per call id.** Each turn keeps a registry of its function calls, including calls that a
  handler of the app's own claims into a block of its own, whose result it then receives. When
  `FunctionInvokingChatClient` sends an approved call again, a rejected call comes back as
  informational, a replayed conversation repeats a call, or a complete response lists a call or its
  result more than once in one message, the existing block (for an approval, its inner block) is
  updated and receives the result. Upstream shows such repeats as extra blocks. A call
  id reused after its result is a new call with its own block; both blocks have the call id as their
  `Id`, so key views by block instance.
- **UI actions run in the engine.** An `Automatic` UI action is invoked by the engine on the caller's
  thread; a `Manual` one waits for the app to call `InvokeAsync`
  (`ConversationStatus.AwaitingInput`). Upstream always leaves invocation to the renderer. A UI action
  that throws returns an error result to the model instead of failing the turn, and
  `UIAgentOptions.Services` is passed to it as `AIFunctionArguments.Services`.
- **UI actions in an approval batch.** When one call of a response needs approval,
  `FunctionInvokingChatClient` turns every call of that response into an approval request, UI actions
  included, and it cannot invoke a UI action because UI actions are sent as declarations. A UI action
  that does not need approval is therefore shown and run as a `UIActionBlock` (automatic or manual as
  registered). A UI action that is an `ApprovalRequiredAIFunction` keeps its approval card and runs once
  approved; the approval is its UI step, so a manual one runs on approval too. The client answers such
  a call with the action's result and sends its approval request as the plain call it carries, without
  an approval response: `FunctionInvokingChatClient` removes approval requests and skips responses
  whose call already has a result, so the result would otherwise reach the model without its call. A
  rejected UI action is answered by the approval response alone, and `FunctionInvokingChatClient`
  returns the rejection. The rewrite happens for every request, so the history and the thread keep the
  approval as it was shown. Requests are matched with results in message order rather than by id:
  providers that number their calls per response reuse call ids, and `FunctionInvokingChatClient`
  derives the request id from the call id (`ficc_<callId>`), so a request counts as answered by the
  client when a result for its call id arrives before the call itself is sent again. Upstream nests the
  UI action's block in the approval and answers only with the approval response, which
  `FunctionInvokingChatClient` cannot carry out for a declaration.
- **Reused call ids and the approvals `FunctionInvokingChatClient` carries out** (its limitation, not a
  port difference). `FunctionInvokingChatClient` (10.7, unchanged on dotnet/extensions main) skips an
  approval response whose call id has a result anywhere in the request, and keys approval requests by
  `ficc_<callId>`. With a provider that reuses call ids, an approved server tool or a rejection (of a
  server tool or a UI action) whose id was answered earlier in the conversation is never carried out,
  and a later request that holds another approval for that id fails with `ArgumentException`. Approved
  UI actions are not affected because the client answers them. The engine cannot correct this from
  above the function invoking client; such providers need unique call ids when they use approvals.
- **Pending approvals and UI actions end with their turn.** Once a turn stops or fails, and for
  approvals and UI actions shown by a restore, `Approve()` and `Reject()` do nothing and
  `InvokeAsync()` and `GetResultAsync()` return canceled tasks, because nothing could send the
  decision or the result. A UI action that was already running and returns after its turn ended
  shows no result either (its invocation task is canceled), so the card matches what the model got.
  Upstream leaves them responsive and drops what they produce.
- **`InteractiveFunctionBlock.IsAwaitingInput`.** A public, bindable signal for whether a block can
  still be answered: true for a pending approval and for a manual UI action that has not run and is
  not running; false once answered and once its turn stopped, failed or was restored. The block raises
  `PropertyChanged` for it and its change notification when the value changes, after the turn's outcome
  is final. Upstream has no
  equivalent, so a restored pending approval looks answerable.
- **A call says whether it can still get a result, and which approval wraps it.**
  `FunctionInvocationContentBlock.IsAbandoned`, which `InteractiveFunctionBlock` forwards, turns true
  when the call's turn ends without a result for it: the turn completed, stopped or failed, or left the
  call's approval or UI action unanswered. A restore sets it for a call without a recorded result. Once
  a turn ended, each of its calls has a result or is abandoned, as in the history, which leaves out
  calls without a result. The change is raised once the turn's outcome is final, after the turn's and
  the context's changes, with one change notification per block. A rejected call is not abandoned: it
  keeps the rejection that `FunctionInvokingChatClient` returns as its result, unless the turn ended
  before that arrived. `FunctionInvocationContentBlock.Approval` is the `ToolApprovalBlock` that wraps
  the call, set when the approval is created. Upstream has neither, so a card bound to its call cannot
  tell a stopped call from a running one, or find its approval, without searching the conversation.
- **Canceling one invocation.** Canceling the token passed to `UIActionBlock.InvokeAsync` cancels only
  that invocation: its task is canceled, the action can be invoked again, and the turn keeps waiting
  for the result. Upstream cancels the block's result, which fails the turn.
- **Typed blocks for UI actions.** Registered UI actions claim their calls before the app's handlers.
  Upstream runs the app's handlers first, so a typed block or `[ToolBlock]` for a UI action's name
  takes the call and the action never runs. Here such a handler builds the action's inner block: it
  shows inside the `UIActionBlock` (or inside the approval of an action that needs approval) and
  receives the action's result through the handler. A handler whose block is not a
  `FunctionInvocationContentBlock` cannot show a result, so the action shows a plain inner block.
- **`InteractiveFunctionBlock`.** `ToolApprovalBlock` and `UIActionBlock` both wrap an inner
  `FunctionInvocationContentBlock` that carries the call and result; an approval for a UI action shares
  the action's inner block. Upstream's `FunctionApprovalBlock.InnerBlock` is a `ContentBlock` and its
  `UIActionBlock` has no inner block.
- **Same-update call and result.** Handlers here complete a block whose result arrives in the same
  update (`IsAlreadyComplete`); upstream re-invokes the handler after emitting a function block. The
  outcome is the same.

### Blocks

- **Content order.** The blocks an update creates follow the order of the contents they show (by the
  first content each block claims). Upstream emits them in handler order, so a complete response, which
  delivers each message as one update, shows a message's tool call before its text, and with #68672's
  media handler a user message with text and an image shows the image first.
- **Updates without output.** An update with no content other than usage, such as one that only
  reports a model id, does not complete the active text, rich-text or reasoning block. Upstream
  completes the block on any update without its content, so such an update in the middle of a message
  split it into two blocks with the same id, and the encrypted content that follows an OpenAI Responses
  reasoning summary after empty updates became a separate block.
- **`ContentBlock.Id` has a public setter** so blocks defined outside the engine can be given ids, and
  **`ContentBlock.CreatedAt`** records when a block was created. Upstream's `Id` setter is internal.
- **`TextContentBlock`** marks plain streamed text so a renderer can tell it from a provider's rich
  text. A new block starts when the update's message id changes; upstream appends consecutive
  messages' text to one block. Paragraphs are built only when `Content` is read, and finished
  paragraphs are kept, so a long reply is not re-split on every token as upstream does.
- **Reasoning.** `ReasoningContentBlock` and its handler show reasoning and protected reasoning (with
  cached text), one block per reasoning item: a new message id, or reasoning after the item's
  protected data (which providers send last), starts a new block, so separate reasoning summaries are
  not concatenated and each keeps its own protected data. Upstream has no reasoning blocks.
- **Media.** One `MediaContentBlock` per item, each with its own id, as in #68672, but the block is
  public and holds the item (`AIContent Content`: a `DataContent` or `UriContent`) instead of a Blazor
  render fragment. `UriContent` is rendered too, and each output of an image-generation tool result
  gets its own block; partial images replace the content of their block. The handlers for reasoning and
  media run before the text handlers.
- **`FunctionInvocationContentBlock.Call`** sets the block id from the call id only when the id is
  empty, and falls back to a new id when the call id is empty.

### State

- `AgentState<T>` and `StateMapperContext` are sealed. `AgentState<T>` can also checkpoint and reset
  itself, which restore and `ClearAsync` use.

### Logging

- Event ids 105 (a UI action failed), 106 (a tool call was not executed) and 107 (a UI action's block
  failed to show its result; the result is still sent) are added.

### Source generator

- Reports every declaration error at once (upstream stops at the first); all diagnostics are errors.
- `MAUIAI110` requires a public parameterless constructor (upstream's generated code would not
  compile). A property with a private or init-only setter is reported by `MAUIAI107` and no handler is
  generated; upstream warns and skips the property.
- Generated handlers are named `{Block}GeneratedHandler` to avoid clashing with user types, and a
  single `[ToolResult(Name = "...")]` property reads that property of the JSON result.
- Primitive values sent as strings, such as `"5"`, are parsed with the invariant culture. A value that
  cannot be converted leaves its property unchanged; upstream fails the turn. Complex types use
  reflection-based `System.Text.Json`, as upstream.
- A block whose properties are all primitive uses no reflection, so trimmed and NativeAOT apps get no
  IL2026 or IL3050 warnings. It reads several results, or a named result, from a `JsonElement` or a
  string that holds JSON, and leaves other result objects unread. Upstream serializes any result object
  with reflection-based `System.Text.Json`; here only a block with a complex-typed property does.

## Not ported

- Blazor components, CSS, the JavaScript input stack, `MediaContentFactory` and the
  `RenderFragment`-based media block (#68672), and the MCP Apps host (#70314).
- `ConfigureAwait(false)` and locks (see above).
- Per-round retry and `ConversationTurn.TruncateResponseBlocks` (#68672).
- Writing continuation rounds to the thread as separate user turns.
- The `FunctionApproval*` names, to avoid renaming public types.
- `UIAgent.Dispose(bool)` (#68672).
