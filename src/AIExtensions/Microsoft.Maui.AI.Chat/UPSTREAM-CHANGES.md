# ASP.NET Components.AI convergence

`Microsoft.Maui.AI.Chat` shares its block/pipeline ancestry with the experimental
`Microsoft.AspNetCore.Components.AI` work in dotnet/aspnetcore.

The old monolithic PR #67673 is closed. The current reference is the incremental
05-10 stack whose top is:

- PR: [dotnet/aspnetcore#68672](https://github.com/dotnet/aspnetcore/pull/68672)
- Branch: `milosk/components-ai-10-production-app`
- Compared SHA: `b67462b1ab1111bc24a24cbe46edf7352d42dc1f`
- ASP.NET main: `e072299dd7ec1733e9fb20c60993c5a48208c625`
- Compared on: 2026-08-22

Client tools (#68325) and server tools (#68327) are now merged to ASP.NET
`main`. Human approval through predictive state (#68329-#68335) remains stacked
and has incorporated a full human-review pass. Multimodal input (#68672) is a
new tenth layer and still needs rebase/review. The stack-top Components E2E run
is green; the next-to-merge approval layer has unrelated QuickGrid flakes.

## Portable engine status

| Capability | MAUI status |
|---|---|
| Block lifecycle/change notifications | Converged; `ContentBlock.Id` remains publicly settable for external custom blocks |
| Two-phase block mapping/custom handlers | Converged |
| Streaming plain text | Converged |
| Provider-supplied rich AST | Converged with `RichTextContent` + `RichTextContentHandler` |
| Rich-text node vocabulary | Converged; provider supplies a parsed tree and the library does not claim a Markdown parser |
| Native rich rendering | MAUI equivalent covers headings, inline styles, code, quotes, lists, safe links/images, tables, and footnotes |
| Function call/result pairing | Converged with order-independent `CallId` matching |
| Media | Converged; MAUI maps provider data into neutral `MediaMessageContent` and adds native audio playback |
| Multimodal input | Converged with native file/audio/speech services and one reusable `ChatInputContext` |
| Reasoning/protected reasoning | MAUI preserved; current ASP.NET stack removed reasoning blocks/handlers |
| Approval | Converged; decisions are single-use and rejection reasons propagate |
| Automatic UI actions | MAUI improvement: actions auto-run without entering human `AwaitingInput` |
| Typed state/state mapper | Converged |
| Predictive state | Converged: provisional values can be accepted/rejected and otherwise roll back at turn completion, cancellation, error, clear, or dispose |
| Shared state/stateful provider | Converged through thread replay plus `ConversationId` forwarding |
| Conversation threads/restore | Converged plus explicit thread `Clear()` |
| Retry/cancellation | Converged with separate graceful cancel, observable caller cancellation, transactional direct history, and pending-thread abort |
| `[ToolBlock]` generator | Converged in `Microsoft.Maui.AI.Chat.Generators` |
| Activity blocks | Not added to defaults; ASP.NET still leaves activity handlers unregistered |

## 2026-08-18 human-review hardening

The current ASP.NET stack's first substantive human reviews exposed several
correctness issues in the shared engine shape. ASP.NET still has them; MAUI
audited and hardened the corresponding paths:

- informational `FunctionCallContent` no longer creates duplicate UI-action,
  function, or generated tool blocks;
- call and result content arriving in one update produces one completed block;
- later UI-action calls cannot be consumed by an already-active handler state;
- a second send is rejected while the context awaits human input;
- state-only content is filtered from local chat history while raw thread
  updates remain available for durable replay;
- a state mapper supplying the wrong state type fails explicitly instead of
  silently hiding content;
- restore is transactional for local history and typed state, starts from the
  initial state, and rejects replayed predictive snapshots rather than reviving
  stale predictions;
- generated tool handlers cover informational calls and same-update results.

## 2026-08-22 multimodal convergence

ASP.NET PR #68672 adds a browser `MessageInputContext`, attachment/send/stop
components, recorded audio, continuous speech recognition, and a `DataContent`
block handler. MAUI ports the portable state machine rather than the Blazor/JS
surface:

- `ChatInputContext` is the single bindable composer boundary for text,
  attachments, status/error, composing state, send/stop, audio, and live speech;
- audio and speech are injectable, with real Android/iOS/Mac Catalyst/Windows
  defaults and deterministic sample services;
- `ChatView` owns the native control-template parts and platform permission UX;
- failed/cancelled `UIAgent` attempts restore local history and call
  `IConversationThread.AbortTurn`, so persistent pending updates cannot leak;
- audio transcription and speech passes use operation identity checks so stale
  callbacks cannot overwrite newer input.

## Current ASP.NET changes deliberately interpreted, not copied

### Rich text

ASP.NET now accepts a `RichTextContent` `AIContent` carrying both source text and
an already-parsed node tree. It does not parse Markdown in the library. MAUI
uses the same contract and renders the shared node vocabulary with native
controls rather than HTML.

### Predictive state

`StateMapperContext.SetPredictiveState` applies a typed state value
provisionally. `AgentState<T>.AcceptPredictiveState()` commits it;
`RejectPredictiveState()` restores the value that existed before the first
pending prediction. An unaccepted prediction is rejected when the turn ends,
is cancelled, fails, is cleared, or the context is disposed.

This remains inbound server-to-client state mapping. Neither implementation
ships a symmetric outbound state channel.

### UI surface

Earlier ASP.NET layers removed the old attachment input; #68672 replaces it
with production-oriented browser components and JavaScript media services.
Those renderers and browser APIs are not copied directly. MAUI keeps its native
shell, virtualization, XAML templates, file picker, recorder, and speech APIs.

## Deliberate MAUI differences

### Package layering

- `Microsoft.Maui.AI.Chat` - plain `net10.0`, no MAUI/Blazor dependency.
- `Microsoft.Maui.AI.Chat.Controls` - native AI controls/templates.
- `Microsoft.Maui.Chat.Controls` - provider-neutral human/group chat surface.

ASP.NET currently ships its engine and Blazor components together.

### Native rendering

MAUI uses:

- virtualized `CollectionView` message rendering;
- zero-configuration `CopilotChatView`;
- a provider-neutral `ChatView`;
- priority-based XAML content templates;
- replaceable control templates;
- dynamic resource theming;
- native file picking, audio capture/playback, and speech recognition;
- coalesced in-place streaming refresh;
- explicit session/conversation binding.

### Tool generation

- `Microsoft.Maui.AI.Attributes` generates AOT-friendly `AIFunction`s with
  DI/keyed-DI binding.
- `Microsoft.Maui.AI.Chat.Generators` generates simple one-call/one-result block
  handlers.

Many-to-one aggregation and custom event projection remain handwritten
handlers.

### Threading

The engine and controls are deliberately single-thread-affine and not
thread-safe. Callers serialize access and enter the owning application thread.
No locks, semaphores, operation gates, or arbitrary concurrent-caller contract
is added.

## MAUI improvements intentionally preserved

- Public `ContentBlock.Id` keeps arbitrary external custom blocks viable.
- Function results match by `CallId`, including reverse-order batches.
- `ImageGenerationToolResultContent.Outputs` are unwrapped into media blocks.
- Media and reasoning remain supported despite their removal upstream.
- `AgentContext.Clear()` resets local and persistent history coherently.
- UI actions auto-run and do not stall in `AwaitingInput`.
- Mixed approval and tool results continue as separate user/tool messages.
- Failed or canceled sends cannot grow local or persistent pending history.
- Informational calls and same-update call/results are handled without
  duplicate execution.
- Restore rolls back typed state/history on failure and cannot revive a rejected
  predictive snapshot.
- Default errors are generic; diagnostics remain on `AgentContext.Error`.
- Native virtualization and streaming coalescing are retained.
- Response-block removals are notified precisely rather than rediscovered with
  an O(n) projection diff.
- The full XAML/template/resource customization model remains MAUI-native.

## Upstream limitations not copied

- External non-function custom blocks still cannot assign ASP.NET's required
  internal-set `ContentBlock.Id`.
- Client/UI actions still require renderer code to call `InvokeAsync`; the
  engine otherwise waits indefinitely.
- The activity handler remains unregistered and `Pending` lifecycle remains
  unused.
- Restore still rebuilds display history rather than resumable interactions.
- Server-tool handlers still render informational calls; MAUI filters them.
- The ASP.NET direct-history fix does not add an explicit persistent-thread
  abort contract; MAUI requires one.
- `RawRepresentation` is not a durable serialized discriminator.
- No production conversation-thread provider ships.
- Pipeline handler cancellation remains reserved rather than propagated.
- Blazor `MessageList` is not virtualized.
- The rich-text library accepts provider ASTs but does not parse Markdown.
- Reasoning handling remains absent from the current ASP.NET stack.

## Persistence limitations

`IConversationThread` stores committed raw `ChatResponseUpdate`s, not rendered
block snapshots. Restore replays updates through current handlers and the state
mapper.

- Applications own persistence and serialization.
- Custom handlers must remain registered.
- Custom discriminators must survive serialization.
- Restored pending approvals/UI actions are display history, not resumable work.
- The thread must round-trip roles, IDs, contents, additional properties, and
  provider conversation IDs.
- `AbortTurn` must discard pending updates after failure/cancellation while
  preserving committed turns.

## Updating the reference

Fetch the latest #68672 head plus every parent in the effective stack. Re-check
blocks, engine, pipeline, generator, tests, dojo scenarios, review threads, and
completed CI runs rather than reviewing only the newest layer's diff.
