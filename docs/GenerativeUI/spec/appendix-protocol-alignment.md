# Appendix: Protocol Alignment — AG-UI, A2UI & Future Interop

> **Status:** Adopted direction (v0.1), August 2026.
> Parent: [`overview.md`](./overview.md). Related:
> [State & Binding Model](./appendix-binding-model.md),
> [UI-DSL & Inflator](./appendix-ui-dsl.md).

This appendix records how Generative UI relates to two emerging standards:

- **AG-UI** — an agent↔application runtime/event/state protocol.
- **A2UI** — a declarative agent-authored UI description language.

The short version:

1. **AG-UI is not a generative-UI/rendering specification.** Its strongest overlap with this
   project is shared-state synchronization: full snapshots plus RFC 6902 JSON Patch deltas.
2. **A2UI is a generative-UI specification** and strongly overlaps our UI-DSL/inflator — but it has
   no .NET MAUI renderer today and remains an evolving public preview.
3. We therefore keep the working MAUI-native implementation, **treat AG-UI/A2UI as compatibility
   north-stars**, and avoid dependencies/topology changes for now.

This is an intentional convergence strategy — not an accidental fork and not a permanent rejection
of either protocol.

## 1. The three layers

| Layer | Standard / our implementation | Responsibility |
|---|---|---|
| Agent↔application runtime | **AG-UI** | Lifecycle, text/tool streams, messages, approvals/interrupts, state snapshots/deltas; transport such as SSE/binary |
| Declarative generated UI | **A2UI** / our **UI-DSL** | Trusted component vocabulary, data binding, actions, incrementally describable UI surfaces |
| Native rendering + app integration | our **MAUI inflator/canvas/registry** | Maps the trusted description to MAUI `View`s; app styling, custom controls/screens, observable binding graph |

AG-UI and A2UI are complementary:

- A2UI payloads can be carried by AG-UI events/state.
- AG-UI deliberately does not tell a frontend how to render state.
- A2UI deliberately does not define the full agent runtime/transport.

They are also unrelated to Google's **A2A** agent-to-agent protocol despite the similar acronym:
A2UI means **Agent-to-User Interface**.

Sources:
[AG-UI architecture](https://docs.ag-ui.com/concepts/architecture),
[AG-UI generative-UI clarification](https://docs.ag-ui.com/concepts/generative-ui-specs),
[A2UI canonical repository](https://github.com/a2ui-project/a2ui).

## 2. AG-UI: what it would and would not replace

AG-UI is an event-driven protocol for connecting an application/frontend to an agent. Its .NET SDK
is split into small packages:

| Package | What it provides |
|---|---|
| `AGUI.Abstractions` | protocol events/messages/tools/capabilities/interrupts + source-generated JSON types |
| `AGUI.Formatting` | event-stream formatting + Server-Sent Events |
| `AGUI.Protobuf` | binary event-stream formatting |
| `AGUI.Client` | HTTP client and `AGUIChatClient` (`Microsoft.Extensions.AI.IChatClient`) for consuming a remote AG-UI endpoint |
| `AGUI.Server` | adapts `ChatResponseUpdate` streams into AG-UI events; supports pass-through `BaseEvent` values via `RawRepresentation` |

`AGUI.Abstractions` targets modern .NET plus portable targets and is AOT-compatible; see the
[.NET SDK overview](https://docs.ag-ui.com/sdk/dotnet/abstractions/overview).

### 2.1 Direct alignment: shared state

AG-UI defines:

- `STATE_SNAPSHOT` — a complete JSON state replacement.
- `STATE_DELTA` — incremental RFC 6902 JSON Patch operations.

This is exactly the model implemented here:

| AG-UI | Generative UI |
|---|---|
| `StateSnapshotEvent.Snapshot` | `set_state(json, path?)` + persistent `CanvasState.StateRoot` |
| `StateDeltaEvent.Delta` | `apply_patch(operations)` + `UiStatePatcher` |
| frontend state consumer | MAUI `UiObject` observable graph + bound canvas |

We deliberately use standard RFC 6902/RFC 6901 shapes so a future adapter can map without changing
the state model.

Source: [AG-UI state management](https://docs.ag-ui.com/concepts/state),
[.NET state event reference](https://docs.ag-ui.com/sdk/dotnet/abstractions/events).

### 2.2 Partial alignment: approvals

The in-app loop already uses `Microsoft.Extensions.AI` approval content
(`ToolApprovalRequestContent` / `ToolApprovalResponseContent`) through
`FunctionInvokingChatClient`. AG-UI's .NET integration uses the same M.E.AI content/lifecycle
concepts, so approval signaling is already conceptually aligned.

AG-UI does **not** replace our richer in-canvas `show_confirm`; it could carry the resulting intent
or interrupt in a future adapter.

### 2.3 No equivalent in AG-UI

AG-UI provides **no** direct equivalent for:

- OpenAPI fetch/reduction/invocation (`OpenApiReducer`, `ApiInvoker`, `read_api`/`write_api`).
- A declarative UI language.
- A MAUI inflator, style system, canvas, or custom-control registry.
- The observable `UiObject` graph needed by MAUI binding.
- The in-process `FunctionInvokingChatClient` loop itself.

AG-UI is explicitly render-agnostic. Adopting it would standardize event envelopes and remote
transport; it would not remove the bulk of this project.

## 3. A2UI: the closest match to our UI-DSL

**A2UI** is a Google-originated, Apache-2.0 declarative generative-UI specification. An agent emits
JSON describing components from a client-controlled trusted catalog; a native renderer maps them to
real controls. It forbids model-authored executable code — the same core safety model as our DSL.

At the time of this review (August 2026), A2UI v0.9.1 is a functional **early-stage public preview**
with v1.0 work underway. Official maintained renderers include web frameworks and Flutter; there is
**no .NET/MAUI, WinUI, or Blazor renderer**.

Sources:
[A2UI repository/status](https://github.com/a2ui-project/a2ui),
[A2UI component reference](https://raw.githubusercontent.com/a2ui-project/a2ui/main/docs/public/reference/components.md),
[A2UI renderer list](https://raw.githubusercontent.com/a2ui-project/a2ui/main/docs/public/reference/renderers.md).

### 3.1 Strong convergence

| A2UI | Our UI-DSL |
|---|---|
| `Row` / `Column` | `Stack` (`orientation`) |
| `List` + data-bound template | `List` + `itemsBind` + one template child |
| `Text` variants (headings/body/caption) | `Label` + `Title`/`Subtitle`/`Body`/`Caption` |
| `Image`, `Icon`, `Divider`, `Card` | `Image`, `Icon`, `Separator`, `Card` |
| `Button` + action event | `Button` + `intent` |
| `TextField` with input types | `Field` / `Entry` + `kind` |
| path binding into separate data | `bind` into persistent `StateRoot` |
| trusted component catalog | `GenerativeUiRegistry` controls/screens/styles |

The two systems arrived at nearly the same architecture independently. That convergence validates
the overall design.

### 3.2 Meaningful differences

| A2UI | Our implementation |
|---|---|
| Flat adjacency list: each component has an `id`; child links reference ids | Nested `UiNode` tree (optional ids) |
| Designed for streamed/incremental structural updates | `render_ui` currently replaces the whole structure; data updates are incremental via JSON Patch |
| JSON Pointer-style binding objects (`{ "path": "/user/email" }`) | Dotted `bind` paths (`"user.email"`) compiled to MAUI indexer bindings |
| Broader component catalog (`Modal`, `Tabs`, `Slider`, `ChoicePicker`, `DateTimeInput`, `CheckBox`) | Smaller implemented MVP vocabulary; app extensions cover bespoke controls/screens |
| No MAUI renderer | Working MAUI-native inflator/canvas |

The adjacency-list/id model is attractive for **patching UI structure** (not just data) and for
progressive streaming. We treat it as the north-star for a future DSL major version, not a change to
make opportunistically inside the MVP.

## 4. What “full embracement” would mean

### 4.1 Full AG-UI adoption

AG-UI's native topology is:

```text
frontend/app -- POST RunAgentInput --> remote agent endpoint
frontend/app <-- streamed BaseEvent[] -- SSE/binary
```

Our topology today is an **in-process agent inside the MAUI app**. Full adoption has three possible
forms:

1. **In-process event adapter (least invasive).** Add `AGUI.Abstractions` + `AGUI.Server`; wrap the
   existing M.E.AI stream as `IAsyncEnumerable<BaseEvent>`. State tools emit
   `StateSnapshotEvent`/`StateDeltaEvent`; the MAUI consumer routes them to `UiObjectBuilder` /
   `UiStatePatcher`. No HTTP/SSE required.
2. **Loopback endpoint.** Host `AGUI.Server` over local Kestrel/SSE and consume it with
   `AGUIChatClient`. This creates a real AG-UI endpoint but adds mobile-hosting/port/lifecycle
   complexity for little immediate product value.
3. **Move the agent remote.** The MAUI app becomes a conventional AG-UI frontend and the agent loop
   moves to a server. This gains cross-frontend interoperability but loses the simplicity/privacy/
   deployment characteristics of the in-app experiment.

What would be removed: small hand-authored event wrappers and some approval/state-envelope plumbing.
What remains: OpenAPI tools, UI language/renderer, observable graph, patch application, canvas,
registry, styling, app-specific shell, and likely `IChatBridge`.

### 4.2 Full A2UI adoption

Full A2UI means replacing our DSL format and renderer, not merely adding a package:

1. Change the AI tool/prompt to emit A2UI messages/components.
2. Build the **first MAUI A2UI renderer** (flat adjacency-list parser, component factory, data
   binding, action routing, incremental structural updates, all supported catalog components).
3. Delete `UiDocument`/`UiNode`/`GenUiInflator`/`StyleApplier` only after feature parity.
4. Keep `UiObject`, `UiStatePatcher`, `CanvasState`, `GenerativeCanvasView`, registry integration,
   and the OpenAPI side — A2UI does not replace those MAUI/app concerns.

This would likely **add more code than it removes** in the near term (rough order: 800–1500 lines for
a useful first renderer) and ties the experiment to an evolving preview spec. The payoff would be
interop with A2UI-emitting agents and a chance for this project to become the ecosystem's MAUI
renderer.

## 5. Repository/package reality

The `AGUI.*` packages are published on NuGet.org. This repository intentionally uses only dnceng
proxy feeds and forbids adding NuGet.org directly. A live package search against every configured
feed returned **no result** for either:

- `AGUI.Abstractions`
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`

Therefore even the low-cost “types-only” adoption is **blocked in this repo today** unless the
packages are added to an approved feed/dependency flow. We do not vendor protocol types or add a
direct NuGet.org source to work around that.

## 6. Adopted direction

### Now (MVP)

- Keep the working MAUI-native UI-DSL, inflator, observable graph, canvas, and OpenAPI tools.
- Use **standard JSON Patch (RFC 6902)** + JSON Pointer (RFC 6901) for state deltas.
- Keep snapshot/delta shapes compatible with AG-UI (`set_state` / `apply_patch`).
- Keep the DSL shapes stable during MVP; document A2UI mapping explicitly.
- Keep the in-process M.E.AI loop (no forced remote-agent topology).
- Take **no AG-UI/A2UI dependency**.

### Converge deliberately (future)

- Add an adapter from our snapshot/delta tools to AG-UI `StateSnapshotEvent` /
  `StateDeltaEvent` when approved packages are available.
- Consider an **A2UI-shaped major DSL version** (or A2UI→our-DSL converter) after A2UI v1 stabilizes.
- Prototype an id/adjacency-list representation for **incremental structural UI patching**.
- Expand the node catalog toward useful A2UI parity (`Modal`, `Tabs`, `Slider`, `ChoicePicker`,
  `DateTimeInput`, `CheckBox`) where product scenarios demand it.
- Evaluate becoming the official/community **MAUI A2UI renderer** as a separate, explicit product
  bet — not as an incidental refactor of the current experiment.

## 7. Component mapping summary

| Our component | AG-UI equivalent | A2UI equivalent | Decision |
|---|---|---|---|
| OpenAPI reducer + `read_api`/`write_api` | none | none | keep — unique |
| `UiDocument`/`UiNode`/inflator/style | none (AG-UI is render-agnostic) | direct conceptual equivalent, no MAUI renderer | keep now; A2UI north-star |
| `UiObject` observable graph | JSON state only (no MAUI binding) | renderer-internal concern | keep — MAUI-specific |
| `UiStatePatcher` | `STATE_DELTA` is RFC 6902 | incremental data model | keep applier; wire-shape aligned |
| `CanvasState`/canvas host | none | renderer concept, no MAUI implementation | keep — unique |
| `IChatBridge` intents | event/interrupt channel only | action events | keep now; adapt later |
| M.E.AI in-app loop | client/server adapter, not loop ownership | none | keep |
| approval gating | M.E.AI/AG-UI-aligned approval content | none | already aligned |

