# ASP.NET Components.AI convergence

`Microsoft.Maui.AI.Chat` shares its block/pipeline ancestry with the experimental
`Microsoft.AspNetCore.Components.AI` work in
[dotnet/aspnetcore PR #67673](https://github.com/dotnet/aspnetcore/pull/67673)
(`javiercn/components-ai-full`).

This document records the current semantic relationship. It is not an old MVP "removed features"
list: the portable engine capabilities have been re-evaluated and brought back where appropriate.

Compared reference during the August 2026 convergence:

- ASP.NET head: `ee195cbda0b8e4fe831ec54bfa40e03795a93e1e`
- MAUI branch: `mattleibow/ai-chat-mvp`

The ASP.NET PR remains experimental, unapproved, and may change. Revalidate this map when updating
the reference.

## Portable engine status

| Capability | MAUI status |
|---|---|
| Block lifecycle/change notifications | Converged; `ContentBlock.Id` stays publicly settable for external custom blocks |
| Two-phase block mapping/custom handlers | Converged |
| Streaming rich text | Converged with compatibility `TextContentBlock : RichContentBlock` |
| Rich-text node vocabulary | Converged; built-in projection intentionally produces paragraphs/text only |
| Function call/result pairing | Converged with MAUI fix for batched out-of-order results |
| Media | Converged plus hosted-image result extraction |
| Approval | Converged as `ToolApprovalBlock`; decisions are single-use |
| Reasoning/protected reasoning | Converged |
| Automatic UI actions | Converged and improved: non-human actions auto-run without `AwaitingInput` |
| Typed state/state mapper | Converged (`UIAgent<TState>`, `AgentState<T>`, `StateMapperContext`) |
| Conversation threads/restore | Converged plus explicit thread `Clear()` |
| Retry/cancellation | Converged with separate graceful cancel and observable caller cancellation |
| `[ToolBlock]` generator | Converged in `Microsoft.Maui.AI.Chat.Generators` |
| Activity blocks | Not added: upstream activity support is opt-in scaffolding and not in its default pipeline |

## Deliberate MAUI differences

### Package layering

MAUI keeps the portable engine and native UI in separate shipping packages:

- `Microsoft.Maui.AI.Chat` — plain `net10.0`, no MAUI/Blazor dependency.
- `Microsoft.Maui.AI.Chat.Controls` — native MAUI controls/templates.

ASP.NET currently ships engine and Blazor components together.

### Native rendering

Blazor `MessageList`, `BlockRenderer<T>`, `ChatPage`, CSS, SSR forms, drawer, and bubble components
are not copied literally. MAUI uses:

- virtualized `MessageListView`;
- zero-config `CopilotChatView`;
- priority-based XAML `ContentTemplate`s;
- replaceable `ControlTemplate`;
- dynamic resource theming;
- native file picker attachments;
- 50 ms streaming refresh coalescing;
- explicit `Session` binding instead of cascading components.

A host can place the same control in a page, drawer, flyout, or floating layout without adding
web-specific shell APIs.

### Tool generation

Two complementary generators ship:

- `Microsoft.Maui.AI.Attributes` generates AOT-friendly `AIFunction`s with DI/keyed-DI binding.
- `Microsoft.Maui.AI.Chat.Generators` generates simple one-call/one-result block handlers.

Many-to-one aggregation and nontrivial custom event projection remain handwritten handlers.

### Threading

The engine and controls are deliberately single-thread-affine and not thread-safe. Callers
serialize access and dispatch background entry to the owning application thread. No locks,
semaphores, or arbitrary concurrent-caller contract is added.

## MAUI improvements intentionally preserved

- Public `ContentBlock.Id` supports external arbitrary custom blocks. ASP.NET's internal setter
  prevents its documented external custom-block pattern from compiling.
- Function results are matched by scanning for the correct `CallId`, including reverse-order
  batches. The compared ASP.NET handler takes the first result and can miss the match.
- `ImageGenerationToolResultContent.Outputs` are unwrapped into media blocks.
- `AgentContext.Clear()` resets local and persistent conversation state coherently.
- UI actions auto-run; ASP.NET currently stalls until app code manually invokes them.
- Default errors are generic; exceptions remain available through `AgentContext.Error`.
- Native `CollectionView` virtualization and update coalescing are retained.
- The full XAML/template/resource customization model remains MAUI-native.

## Upstream surfaces deliberately not copied

- Static SSR form posts, antiforgery, and render-mode plumbing.
- Blazor render-tree/cascading-value APIs and CSS class contracts.
- Dormant `RichContentBlock.MediaItems`.
- Unused `BlockLifecycleState.Pending` semantics.
- Activity handlers that are not default-wired.
- Internal block ID restrictions.
- UI-action orchestration that incorrectly enters human `AwaitingInput`.
- Raw exception rendering in turnkey shells.

## Persistence limitations

`IConversationThread` stores committed raw `ChatResponseUpdate`s, not rendered block snapshots.
Restore replays updates through the current handlers and state mapper.

- No storage provider ships.
- Custom handlers must remain registered.
- Custom discriminators must survive serialization; `RawRepresentation` is not durable unless an
  implementation explicitly persists it.
- Restored pending approvals/UI actions are display history, not resumable live work.
- The MAUI thread contract adds `Clear()` to preserve the control's explicit new-chat behavior.

## Updating the reference

Use a separate read-only checkout that tracks the reference branch:

```bash
git clone --branch javiercn/components-ai-full https://github.com/dotnet/aspnetcore.git
cd aspnetcore
git pull --ff-only
```

After refreshing, compare the portable `Blocks/`, `Engine/`, `Pipeline/`, attributes/generator,
tests, and PR discussion. Do not assume the open ASP.NET PR is a finalized specification.
