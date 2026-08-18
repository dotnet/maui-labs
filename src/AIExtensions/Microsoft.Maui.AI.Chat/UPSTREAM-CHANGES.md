# ASP.NET Components.AI convergence

`Microsoft.Maui.AI.Chat` shares its block/pipeline ancestry with the experimental
`Microsoft.AspNetCore.Components.AI` work in dotnet/aspnetcore.

The old monolithic PR #67673 is closed. The current reference is the incremental
01-09 stack whose top is:

- PR: [dotnet/aspnetcore#68335](https://github.com/dotnet/aspnetcore/pull/68335)
- Branch: `javiercn-components-ai-09-predictive-state`
- Compared SHA: `142ec3289c57f0d2e0efa0856771f71d4ae6157a`
- ASP.NET main: `c349f7588f6619a62d370569acbb87234e8afd11`
- Compared on: 2026-08-18

The 01 streaming-chat and 02 rich-text layers are merged to ASP.NET `main`.
Layer 03 client tools is retargeted to `main`; layers 04 through 09 remain
stacked on it. All open layers still require review. Human inline reviews now
exist across the stack, but there are no approvals or changes-requested
decisions. Completed Components E2E runs at the stack merge ref report no
failures in the main CoreCLR/Mono legs; unrelated quarantine/Helix aggregation
keeps some top-level checks red.

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
| Media | MAUI superset; current ASP.NET stack removed media blocks/handlers |
| Reasoning/protected reasoning | MAUI preserved; current ASP.NET stack removed reasoning blocks/handlers |
| Approval | Converged; decisions are single-use and rejection reasons propagate |
| Automatic UI actions | MAUI improvement: actions auto-run without entering human `AwaitingInput` |
| Typed state/state mapper | Converged |
| Predictive state | Converged: provisional values can be accepted/rejected and otherwise roll back at turn completion, cancellation, error, clear, or dispose |
| Shared state/stateful provider | Converged through thread replay plus `ConversationId` forwarding |
| Conversation threads/restore | Converged plus explicit thread `Clear()` |
| Retry/cancellation | Converged with separate graceful cancel and observable caller cancellation |
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

### Minimal UI surface

The current ASP.NET stack removed media/reasoning components, drawer/bubble
shells, suggestions, attachment input, SSR form components, and component-based
block renderers. Those removals are not a specification for native MAUI.

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
- native file picking/media;
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
- Restore remains non-transactional upstream and can revive rejected predictive
  state.
- State-only mapper content remains in upstream local chat history.
- Upstream handlers still claim informational calls and mishandle batched
  call/result updates.
- `RawRepresentation` is not a durable serialized discriminator.
- No production conversation-thread provider ships.
- Pipeline handler cancellation remains reserved rather than propagated.
- Blazor `MessageList` is not virtualized.
- The rich-text library accepts provider ASTs but does not parse Markdown.
- Media/multimodal and reasoning handling are absent in the current stack.

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

## Updating the reference

The read-only comparison worktree used for this review tracks the stack top:

```bash
cd /Users/matthew/.copilot/repos/copilot-worktrees/aspnetcore/mattleibow-vigilant-potato
git pull --ff-only
```

Re-check the entire effective stack, not only PR #68335's predictive-state
delta: blocks, engine, pipeline, generator, tests, dojo scenarios, review
threads, and completed CI runs.
