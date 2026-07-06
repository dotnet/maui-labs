# Upstream Changes

This project (`Microsoft.Maui.AI.Chat`, the engine) started as a copy of the ASP.NET AI
Components engine from [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore),
branch `javiercn/ai-components-e2e-tests`, path `src/Components/AI/src/`.

**Goal:** track everything we changed, removed, added, or renamed relative to that upstream
so we can (a) feed improvements back to the ASP.NET team and (b) add the removed features back
as the MVP grows. When the upstream engine ships on NuGet, we want to be able to re-converge.

> **This is an intentionally simplified MVP.** We kept the *block pipeline* + *stateful
> conversation context* and replaced the *Blazor UI* with a native **MAUI** UI
> (`Microsoft.Maui.AI.Chat.Controls`, a separate project). Many upstream features were removed
> to reach a minimal, understandable core. The **"Removed features"** section below is the
> shopping list for adding them back.

---

## 1. Strategy at a glance

| Upstream area (`src/Components/AI/src/`) | What we did |
|---|---|
| `Blocks/`, `Engine/`, `Pipeline/` | **Kept** as the engine (`Microsoft.Maui.AI.Chat`), simplified — see below. |
| `Components/` (Blazor UI), `wwwroot/` (CSS/JS) | **Removed entirely**, replaced by a native MAUI UI layer (`Microsoft.Maui.AI.Chat.Controls`). |
| `Attributes/` (`[ToolBlock]` source-generator attrs) | **Removed** — the sample shows the hand-written `ContentBlockHandler` pattern instead. |
| Reasoning, Activity, UI Actions, State, Persistence (Thread), Retry, Rich text | **Removed** from the engine (see section 2). |

The engine has **no dependency** on the UI layer, Blazor, or ASP.NET.

---

## 2. Removed features (the "add back" list)

Each of these existed upstream and was cut for the MVP. Grouped by capability, with the exact
upstream types so we know what to restore.

### 2a. Conversation persistence / threads  ⭐ highest priority to add back
The upstream engine treats an `IConversationThread` as the persistable source of truth, and
`AgentContext` is a projection that can be re-hydrated from it. The Blazor `AgentBoundary`
calls `AgentContext.RestoreAsync()` when a thread has stored updates.

| Upstream type | Status |
|---|---|
| `Engine/IConversationThread.cs` | **Removed** |
| `Engine/AgentContext.RestoreAsync(...)` | **Removed** |
| `Engine/UIAgent.RestoreAsync(...)` | **Removed** |
| `Pipeline/UIAgentOptions.Thread` (`IConversationThread?`) | **Removed** |

> **Note for adding back:** our recent refactors were done *specifically* to keep this clean —
> the engine's `Turns` and `UIAgent`'s `ChatMessage` history contain **only real content**, no
> fabricated status blocks (see 2f). That preserves the "turns == projection of the thread"
> invariant persistence depends on.

### 2b. Agent state / shared state / predictive state
| Upstream type | Status |
|---|---|
| `Engine/AgentState.cs` | **Removed** |
| `Engine/UIAgentOfT.cs` (`UIAgent<TState>`) | **Removed** — we only have the non-generic `UIAgent`. |
| `Pipeline/StateMapperContext.cs` | **Removed** |
| `Pipeline/UIAgentOptions.StateMapper` (`Func<StateMapperContext,bool>?`) | **Removed** |

### 2c. UI Actions (frontend tools)
Client-invoked tools rendered as interactive UI. We dropped these because the MVP is
client-side and can call device APIs (GPS, etc.) directly via normal backend tools.
| Upstream type | Status |
|---|---|
| `Blocks/UIActionBlock.cs` | **Removed** |
| `Pipeline/UIActionHandler.cs` | **Removed** |
| `Pipeline/UIAgentOptions.RegisterUIAction(AIFunction)` | **Removed** |

### 2d. Reasoning
| Upstream type | Status |
|---|---|
| `Blocks/ReasoningContentBlock.cs` | **Removed** |
| `Pipeline/ReasoningHandler.cs` | **Removed** |

### 2e. Activity
| Upstream type | Status |
|---|---|
| `Blocks/ActivityContentBlock.cs` | **Removed** |
| `Pipeline/ActivityHandler.cs` | **Removed** |

### 2f. Retry
| Upstream type | Status |
|---|---|
| `Engine/AgentContext.RetryAsync(...)` | **Removed** |

### 2g. Rich text (markdown node hierarchy)
Upstream had a structured rich-text/markdown block and a `RichText/` node tree. We simplified
to plain text; the sample demonstrates a "fancy plain text" formatter as a *pattern* to build on.
| Upstream type | Status |
|---|---|
| `Blocks/RichContentBlock.cs` | **Renamed + simplified** → `Blocks/TextContentBlock.cs` (plain text, no markdown tree). |
| `Blocks/RichText/` (node hierarchy) | **Removed** |

### 2h. `[ToolBlock]` source generator
Upstream generates strongly-typed tool blocks from attributes. We removed the generator and its
attributes; the sample's `WeatherToolBlock` + `WeatherToolBlockHandler` show the hand-written
equivalent.
| Upstream type | Status |
|---|---|
| `Attributes/ToolBlockAttribute.cs` | **Removed** |
| `Attributes/ToolParameterAttribute.cs` | **Removed** |
| `Attributes/ToolResultAttribute.cs` | **Removed** |
| ToolBlock source generator project | **Removed** |

### 2i. Blazor UI layer (entire `Components/` + `wwwroot/`)
Replaced wholesale by the MAUI-native `Microsoft.Maui.AI.Chat.Controls` project (section 5).
Removed upstream Blazor components: `AgentBoundary`, `AgentFormBoundary`, `BlockContainer`,
`BlockRenderer`, `BlockRendererRegistration`, `BlockRendererWithComponent`, `BubblePosition`,
`ChatBubble`, `ChatDrawer`, `ChatPage`, `ConversationTurnRenderer`, `DrawerPosition`,
`FormMessageInput`, `MessageInput`, `MessageList`, `MessageListContext`, `Suggestion`,
`SuggestionList`, plus `wwwroot/` CSS/JS.

---

## 3. Renamed / changed engine types

| Upstream | Ours | Reason |
|---|---|---|
| `Blocks/FunctionApprovalBlock.cs` | `Blocks/ToolApprovalBlock.cs` | Consolidate on "ToolApproval" naming (matches M.E.AI's `ToolApprovalRequestContent`). |
| `Pipeline/FunctionApprovalHandler.cs` | `Pipeline/Handlers/ToolApprovalHandler.cs` | Rename to match the block; moved into a `Handlers/` subfolder. |
| `Blocks/RichContentBlock.cs` | `Blocks/TextContentBlock.cs` | Rich markdown block reduced to plain streaming text (see 2g). |
| `Pipeline/*Handler.cs` (flat) | `Pipeline/Handlers/*Handler.cs` | Grouped all handlers into a `Handlers/` subfolder (namespace unchanged). |

---

## 4. Engine code modifications (line-level, on kept files)

| File | Change | Reason |
|---|---|---|
| `Blocks/ContentBlock.cs` | `Id` setter changed from `internal` to `public`. | Custom block handlers in consumer assemblies (e.g. the sample) set `Id` from `FunctionCallContent.CallId`. |
| `Blocks/TextContentBlock.cs` | `AppendText(...)` made `virtual`. | Lets the sample's `FormattedTextBlock : TextContentBlock` re-parse on append while feeding the base text. |
| `Engine/AgentContext.cs` | Added `uninvokedToolBlocks.RemoveAll(b => b.Result is not null)` after the streaming loop. | Prevents double tool invocation when `FunctionInvokingChatClient` middleware already ran the call during streaming. |
| `Engine/AgentContext.cs` | On a failed turn, sets `Status = Error` + `Error = ex` **only** — no block is added to the turn. | See 2f/6: failures are surfaced via status/`Error`; the UI renders them, keeping the thread clean for persistence. |
| `Engine/AgentContext.cs` | **Added `Clear()`** (reset turns + history). | Was on the old "wanted upstream" list; needed by the sample's clear button. Consider upstreaming. |

---

## 5. What replaced the Blazor UI: `Microsoft.Maui.AI.Chat.Controls` (net-new)

The upstream `Components/` (Blazor) has **no** counterpart in the engine repo copy — it is a
separate MAUI project. It is not "upstream code," so it is documented here only for the team's
awareness of the equivalent surface.

- **`CopilotChatView`** — drop-in `TemplatedView`: header, welcome, suggestion chips, busy state,
  input box, and a nested message list. Fully theme-/template-able. (Blazor equiv: `ChatPage` +
  `MessageInput` + `SuggestionList`.)
- **`MessageListView`** — a messages-only `TemplatedView` (just the `CollectionView` of blocks),
  hosted inside `CopilotChatView` and usable standalone. (Blazor equiv: `MessageList` /
  `MessageListContext`.)
- **`ContentTemplate` + `ContentTemplateSelector`** — per-block-type view selection by a
  `When(context)` predicate + priority. (Blazor equiv: `BlockRenderer<TBlock>`.) **Templates are
  an allow-list:** a block with no matching template renders nothing (no diagnostic fallback),
  so omitting `FunctionInvocationTemplate` is how you hide tool calls.
- **Per-block views/templates:** `TextContentTemplate`, `FunctionInvocationTemplate` (single
  template that renders the pending call and updates in place to the result — replaces upstream's
  separate call/result rendering), `ToolApprovalTemplate`, `MediaContentTemplate`,
  `DefaultContentTemplate` (opt-in catch-all), plus `GenericContentTemplate`.
- **UI-only status blocks (do not exist in the engine):** `ThinkingContentBlock` and
  `ErrorContentBlock` live in the Controls layer and are injected into `MessageListView`'s item
  list from the session `Status`/`Error`. See section 6.
- **Theme:** `ChatTheme.xaml` merges `ChatColors.xaml` (tokens), `MessageListTheme.xaml`
  (message/block templates), and `CopilotChatTheme.xaml` (the full surface). All resource keys
  use the **`MauiAIChat.`** prefix (renamed from the upstream-derived `ExtensionsAI.` prefix) so
  they never collide with host-app resources once merged into `Application.Resources`.

Bindable collection APIs (`ContentTemplates`, read-only observable `Items`) follow MAUI
conventions; a streaming-coalescing layer batches per-token block changes (~20 fps) to avoid a
`CollectionView` cell-recreation storm.

---

## 6. Status rendering: thinking & error are UI-only (important design note)

Upstream has **no** thinking/error content blocks. We originally added them as *engine* blocks,
then **moved them out of the engine entirely** into the Controls layer:

- The engine only exposes `ConversationStatus` (`Idle`/`Streaming`/`AwaitingInput`/`Error`) and
  `AgentContext.Error` (the exception). It never fabricates a status block.
- `MessageListView` synthesizes a transient **"Thinking…"** item while streaming and a sticky
  **error** item on the error state, injecting them into its own visual item list.

This keeps `AgentContext.Turns` and `UIAgent`'s message history a clean projection of real
content — a prerequisite for the persistence/thread feature we removed (2a) and want back.

---

## 7. File-by-file map (upstream → here)

Legend: ✅ kept · ✏️ renamed/changed · ❌ removed · ➕ added by us

**`Blocks/`**
`ActivityContentBlock.cs` ❌ · `ApprovalStatus.cs` ✅ · `BlockLifecycleState.cs` ✅ ·
`ContentBlock.cs` ✏️(Id public) · `ContentBlockChangedSubscription.cs` ✅ ·
`FunctionApprovalBlock.cs` ✏️→`ToolApprovalBlock.cs` · `FunctionInvocationContentBlock.cs` ✅ ·
`IInteractiveBlock.cs` ✅ · `InteractiveFunctionBlock.cs` ✅ · `MediaContentBlock.cs` ✅ ·
`ReasoningContentBlock.cs` ❌ · `RichContentBlock.cs` ✏️→`TextContentBlock.cs` ·
`RichText/` ❌ · `UIActionBlock.cs` ❌

**`Engine/`**
`AgentContext.cs` ✏️(no Restore/Retry; +Clear; error path) · `AgentState.cs` ❌ ·
`ConversationStatus.cs` ✅ · `ConversationTurn.cs` ✅ · `IConversationThread.cs` ❌ ·
`UIAgent.cs` ✏️(no RestoreAsync; double-invoke guard) · `UIAgentLog.cs` ✅ ·
`UIAgentOfT.cs` ❌

**`Pipeline/`** (handlers moved to `Pipeline/Handlers/`)
`ActivityHandler.cs` ❌ · `BlockMappingContext.cs` ✅ · `BlockMappingPipeline.cs` ✅ ·
`BlockMappingPipelineLog.cs` ✅ · `BlockMappingResult.cs` ✅ · `ContentBlockHandler.cs` ✅ ·
`FunctionApprovalHandler.cs` ✏️→`ToolApprovalHandler.cs` · `FunctionInvocationHandler.cs` ✅ ·
`HandleResult.cs` ✅ · `HandlerEntry.cs` ✅ · `IActiveEntry.cs` ✅ · `IHandlerEntry.cs` ✅ ·
`MediaContentHandler.cs` ✅ · `ReasoningHandler.cs` ❌ · `StateMapperContext.cs` ❌ ·
`TextBlockHandler.cs` ✅ · `UIActionHandler.cs` ❌ ·
`UIAgentOptions.cs` ✏️(removed `StateMapper`, `Thread`, `RegisterUIAction`)

**`Attributes/`** — `ToolBlockAttribute.cs` ❌ · `ToolParameterAttribute.cs` ❌ · `ToolResultAttribute.cs` ❌

**`Components/`, `wwwroot/`** — ❌ all removed (replaced by MAUI Controls, section 5).

---

## 8. Wanted upstream (document only; don't implement here)

Convenience/robustness we'd like the core engine to gain:

- `AgentContext.Clear()` — we added this locally; would be nice upstream.
- `AgentContext.SystemPrompt` — sugar over `UIAgentOptions.ChatOptions.Instructions`.
- `AgentContext.HasPendingApprovals` + `AutoRejectPendingApprovals()` — reject pending on new user message.
- `UIAgentOptions.AllowMultipleToolCalls` convenience property.
- Thread-safety on the callback lists (`ContentBlock._callbacks`, `AgentContext._*Callbacks`) —
  use `ImmutableList` or a lock.

## 9. Porting notes

- **MAUI uses `ContentTemplate.When()`** for per-block template selection, vs Blazor's
  `BlockRenderer<TBlock>`.
- **No `AgentBoundary` cascading-parameter equivalent** — MAUI passes an `AgentContext` via the
  explicit `Session` property on `CopilotChatView` / `MessageListView`.
- **Tool invocation ownership:** when `UseFunctionInvocation()` middleware is present it drives the
  tool loop; the engine's `UIAgent` must not double-invoke (see the guard in section 4).
- **`Microsoft.Extensions.AI` pinned to 10.7.0** for image generation
  (`UseImageGeneration`/`AsIImageGenerator`).
