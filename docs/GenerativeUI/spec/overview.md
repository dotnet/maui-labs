# Generative UI — Overview Spec

> **Status:** Implemented MVP (v0.2) — the OpenAPI, UI-DSL/inflator, state graph, and sample flows
> are running end to end; remaining design work is tracked in the
> [Open Questions](#16-open-questions).

## 1. Summary

**Generative UI** is an experiment in building applications whose UI is produced *at runtime by
an AI model* rather than authored ahead of time as fixed pages. The user talks to a chat
assistant; the assistant reads and writes data over a REST API and **renders bespoke,
data-bound UI** into a blank canvas in response.

This spec describes:

- **`Microsoft.Maui.AI.GenerativeUI`** — a **reusable, app-agnostic library** that provides the
  two capabilities an app needs to be "generative": a way for the model to **discover and call a
  server's REST API** (via its OpenAPI document), and a way for the model to **render UI** (via a
  constrained UI description language + a runtime inflator).
- **`GenerativeUI.Sample.Garden`** — a concrete sample (a garden shop) that consumes the library.
  Its client and server are co-developed and **share a typed models project**.

The **generic, reusable** part lives entirely in the library. The **app-specific** part (models,
endpoint names, seed data, system prompt) lives in the sample. See
[§8 Library vs. sample boundary](#8-library-vs-sample-boundary).

Companion documents:

- [`appendix-ui-dsl.md`](./appendix-ui-dsl.md) — the UI description language and inflator.
- [`appendix-extensibility.md`](./appendix-extensibility.md) — registering app styles, custom
  controls, and full screens the model can use, at startup or **dynamically at runtime**.
- [`appendix-binding-model.md`](./appendix-binding-model.md) — the generic observable data context
  the UI binds to when there are no hand-authored view models.
- [`appendix-openapi-processor.md`](./appendix-openapi-processor.md) — the OpenAPI explorer,
  reducer, and invoker.
- [`sample-generative-garden.md`](./sample-generative-garden.md) — the sample app.

## 2. Motivation

Modern LLMs are good enough to (a) understand a REST API from its OpenAPI description and (b)
emit a small, well-constrained UI description. That opens a new app shape:

- **Minimal hand-authored UI.** The app ships a shell (chat + canvas) and a small set of visual
  primitives. It does *not* ship a page per feature.
- **The model composes the experience.** "Show me the apples," "add a product called pears," and
  "delete the tomatoes" each produce a fit-for-purpose view without a developer having designed
  that exact screen.
- **One integration, many surfaces.** Because the model talks to the API through a generic
  OpenAPI-driven bridge, adding a server endpoint immediately makes new behavior possible with no
  new client tools and no new client pages.

We want to find out how far this can go, what breaks, and what reusable pieces fall out of it.

## 3. Goals & non-goals

### Goals

- A **reusable library** any MAUI app can drop in to become generative. It hardcodes **no**
  app-specific models, endpoint names, routes, or UI.
- A **small, fixed set of AI tools** — not one tool per endpoint and not one tool per screen.
  Two families: *server-API tools* and *client-UI tools*.
- **Data-bound** generated UI: forms the model can partially fill, live-edit ("set the quantity
  to 3"), and read back on save ("save for me").
- **Reliability over expressiveness** for the MVP: a closed UI vocabulary and a closed invoker
  surface that the model can use predictably, with graceful failure when it produces something
  invalid.
- A **sample** that reaches feature parity with the existing `AIExtensions.Sample.Garden`
  (catalog, cart, orders, reviews, recommendations, approvals) — but server-backed and
  generatively rendered.

### Non-goals (for the MVP)

- **Not** "any client talking to any unknown server." The library is generic, but a real app
  (like the sample) knows its own server and shares typed models with it.
- **Not** a general-purpose XAML/HTML renderer. Raw-XAML inflation is an *experimental stretch*
  only; the primary path is a constrained JSON DSL.
- **Not** production hardening: no multi-user auth, persistence, or horizontal scale in the
  sample server (in-memory only).
- **Not** offline/on-device model integration (uses a configured chat endpoint like the other
  AIExtensions samples). On-device is a possible future via `Microsoft.Maui.Essentials.AI`.

## 4. Glossary

| Term | Meaning |
|---|---|
| **Canvas** | The blank region the AI renders UI into. One canvas per app window. |
| **Server-API tools** | The generic AI tools for exploring + calling the server's REST API. |
| **Client-UI tools** | The generic AI tools for rendering UI and reading/writing bound state. |
| **OpenAPI processor** | Downloads, caches, and *reduces* the server's OpenAPI doc for the model. |
| **Reduced spec** | A compact, model-friendly index of endpoints + schemas derived from OpenAPI. It's compact because it strips OpenAPI *structural plumbing* — it preserves all authored **descriptions** and constraints in full. |
| **Invoker** | The generic HTTP caller behind `read_api` / `write_api`. |
| **UI-DSL** | The constrained JSON description language the model emits to render UI. |
| **Inflator** | The runtime piece that turns a UI-DSL document into MAUI `View`s. |
| **UI registry** | The app-populated, **mutable** catalog of registered **styles**, **controls**, and **screens** that extend the base DSL. Add/remove at startup or anytime afterwards. |
| **Style** | A named visual token mapped to a XAML resource, applied to a node's `style`. |
| **Control** | An app-registered composite control exposed as a DSL node type. |
| **Screen** | An app-registered full surface the model hands off to (e.g. checkout, report). |
| **StateRoot** | The single persistent, observable `UiObject` graph the rendered UI binds to instead of a hand-authored view model. Display bindings are one-way; editable fields are two-way; JSON Patch mutates it in place. |
| **CanvasState** | Client state holding the currently rendered view, busy/empty/confirm state, and the persistent `StateRoot`. |
| **State snapshot / delta** | Full JSON replacement (`set_state`) or incremental RFC 6902 JSON Patch (`apply_patch`), shaped like AG-UI `STATE_SNAPSHOT` / `STATE_DELTA`. |
| **Intent** | A named signal a rendered control raises back into the chat loop (e.g. `submit`). |
| **Tool source** | A class whose `[ExportAIFunction]` methods are surfaced via `AIToolContext`. |

## 5. Architecture

```
┌──────────────────────── MAUI app (thin generative shell) ─────────────────────────┐
│  MainPage: [ Canvas (AI-rendered) ]                       [ narrow ChatView ]      │
│                                                                                     │
│  AIToolContext  =  server-API tools  +  client-UI tools   (composed by the app)     │
│                                                                                     │
│  Microsoft.Maui.AI.GenerativeUI (reusable library)                                  │
│  ┌── OpenApi/ ─────────────────────────┐   ┌── Ui/ ─────────────────────────────┐   │
│  │  OpenApiCache   fetch + cache        │   │  Dsl            UiNode model+parse  │   │
│  │  OpenApiReducer compact index        │   │  GenUiInflator  UiNode → MAUI View  │   │
│  │  ApiInvoker     generic HTTP call    │   │  StateRoot      observable graph    │   │
│  │  OpenApiExplorerTools                │   │  UiStatePatcher RFC 6902 deltas     │   │
│  │    list_endpoints / describe_*       │   │  CanvasState + GenerativeCanvasView │   │
│  │    read_api / write_api              │   │  GenerativeUiTools                  │   │
│  │                                      │   │    render_ui / get_state / set_state│   │
│  │                                      │   │    apply_patch / set_field          │   │
│  │                                      │   │    show_confirm / clear_ui / screen │   │
│  └──────────────────────────────────────┘   └─────────────────────────────────────┘   │
└───────────────────────────────────┬─────────────────────────────────────────────────┘
                                     │  ① GET /openapi/v1.json  (cache + reduce)
                                     │  ② read_api / write_api  (generic REST)
                                     ▼
┌──────────────────────── Minimal API server (sample) ──────────────────────────────┐
│  MapOpenApi() → /openapi/v1.json    In-memory stores    Seeded catalog             │
│  DTOs + source-gen JsonSerializerContext from GenerativeUI.Sample.Garden.Shared    │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

The app **composes** the two tool families into a single `AIToolContext` and runs a normal
`Microsoft.Extensions.AI` function-calling chat loop (as in `AIExtensions.Sample.Garden`).

## 6. The two tool families

The model is given a deliberately small toolset. Details live in the appendices; here is the
shape.

### 6.1 Server-API tools (see [OpenAPI processor appendix](./appendix-openapi-processor.md))

Backed by the server's OpenAPI document (downloaded + reduced on the client).

| Tool | Purpose |
|---|---|
| `list_endpoints` | Compact list of operations: `operationId`, method, path, summary, tags. Also seeded into the prompt by default. |
| `describe_endpoint` | Full detail for one operation: parameters + request/response schema **inlined one level** (nested by name). |
| `describe_model` | Resolved schema for one model (properties, types, required). |
| `read_api` | Invoke a **safe** (GET) operation by `operationId`, e.g. `read_api("getProduct", { sku })`; returns JSON. |
| `write_api` | Invoke a **mutating** operation (POST/PUT/PATCH/DELETE) by `operationId` (params flat, payload under `body`); **requires approval**. |

There is intentionally **no** `list_all_products` / `add_to_list` / etc. Everything routes
through this generic surface, so the library needs no knowledge of the app's endpoints.

> **Descriptions are preserved.** "Reduced" means the OpenAPI *structure* is simplified for the
> model, **not** that any authored text is shortened. Endpoint/parameter/model/property
> descriptions — and meaning-bearing constraints like `enum`/`format`/`required` — are carried
> through **verbatim and in full**, because they exist to tell the model how to use the API
> correctly. Size is managed by lazy expansion (`describe_endpoint`/`describe_model`), never by
> clipping text. See the [OpenAPI processor appendix §3](./appendix-openapi-processor.md#3-reduction-openapireducer).

### 6.2 Client-UI tools (see [UI-DSL appendix](./appendix-ui-dsl.md) and [Extensibility appendix](./appendix-extensibility.md))

| Tool | Purpose |
|---|---|
| `render_ui` | Render a UI-DSL document (`ui` + optional `data`/`form`) into the canvas — **structure**. Called again only when the *kind* of view changes. |
| `apply_patch` | Mutate the canvas **state graph** with a JSON Patch (RFC 6902) so bound UI updates in place — for data changes (remove a cart line, change a quantity). **Not** `render_ui`. |
| `set_state` | Replace the state graph (or a subtree) with a JSON snapshot (seed/reset bound data). |
| `get_state` | Read the current state graph (or a JSON-Pointer subtree) — read before patching, and to gather form values for a write. |
| `set_field` | Convenience single-leaf replace (drives "set the quantity to 3"). |
| `show_confirm` | Render a confirm overlay; resolves via button tap or the user typing "yes". |
| `clear_ui` | Reset the canvas + state to the welcome/empty state. |
| `present_screen` | Hand the canvas off to a **registered full screen** (e.g. checkout, report), supplying its declared inputs. |
| `list_ui_capabilities` | List registered styles/controls/screens (names + descriptions). |

The `render_ui` (structure) vs. `apply_patch`/`set_state`/`set_field` (data) split is what makes the
canvas **stateful** rather than repainted every turn — see §9 and the
[State & Binding Model appendix](./appendix-binding-model.md). `set_state`/`apply_patch` are
deliberately shaped like AG-UI's `STATE_SNAPSHOT`/`STATE_DELTA` (JSON Patch), with no dependency —
see the [Protocol Alignment appendix](./appendix-protocol-alignment.md).

The app **extends** what these tools can produce by registering styles, custom controls, and full
screens (see §6.3). Built-in primitives cover generic UI; registrations add brand styling, bespoke
controls (e.g. a watermarking product image), and full app-owned surfaces (e.g. the official checkout
screen). Today the model discovers the whole catalog via `list_ui_capabilities`; automatic prompt
seeding and optional `describe_control`/`describe_screen` tools are planned.

### 6.3 Extending the vocabulary (see [Extensibility appendix](./appendix-extensibility.md))

The DSL is **closed but extensible**. The library ships base primitives; the app registers its own
vocabulary — at startup **or anytime afterwards** — through
`AddGenerativeUi(options => options.Ui.Add…)` and the DI-resolvable `GenerativeUiRegistry`:

| Extension | Registered via | Appears to the model as |
|---|---|---|
| **Style** | `AddStyle(name, description, appliesTo, resourceKey?)` | a `style` token, valid only on its `appliesTo` control types (e.g. `danger` on a `Button`) |
| **Control** | `AddControl<TControl>(alias, description, props?)` | a node `type` with a named `props` list (e.g. `ProductImage`) |
| **Screen** | `AddScreen<TScreen>(alias, description, inputs?)` | `present_screen` / a `Screen` node (e.g. `CheckoutScreen`) |

The **library hardcodes none** of these — all app specifics live in the app and flow through the
generic registry. Each entry carries a **name/alias** and a freeform **description**; all
descriptions are surfaced to the model **verbatim** (same no-clip principle as API descriptions),
and it is the description that tells the model *when and when not* to use an item.

The registry is a plain mutable collection: `Add…`/`Remove…` at any time (e.g. admin controls and
screens appear after sign-in, vanish on sign-out). Whatever is registered *now* is what the model sees this
turn — no versioning, events, or handles for the MVP. **Theme/size/orientation/accessibility**
variation is handled by the native XAML resource a style maps to
(`AppThemeBinding`/`VisualStateManager`/merged dictionaries), not by the registry or the model: the
model emits `danger` once; the resource adapts. See
[Extensibility appendix §2](./appendix-extensibility.md#2-the-registry).

## 7. Runtime loop

A representative turn ("show me the basil seeds"):

```
User → Chat: "show me the basil seeds"
Model → list_endpoints("basil")                     (optional once endpoint-index seeding is wired)
Model → describe_endpoint("getProduct")             (optional; params + one-level schema)
Model → read_api("getProduct", { sku: "basil-seeds" })   (server call, JSON back)
Model → render_ui({ ui: <detail card>, data: <product json> })   (canvas updates)
Model → Chat: "Here are the basil seeds."           (short text reply)
```

Key properties:

- **Discovery is cached.** After the first exploration the model has enough context. Automatic
  compact-index prompt seeding is an adopted design and configured by `SeedEndpointIndex`, but its
  chat-prompt wiring remains a follow-up; today the model starts with `list_endpoints`.
- **UI updates are a side effect of a tool call**, not part of the chat text. The canvas is
  updated on the UI thread by `render_ui`.
- **Writes pause for approval.** `write_api` is approval-gated (see §11).

## 8. Library vs. sample boundary

This boundary is the core design principle and the thing we most want to get right.

| Concern | Lives in the **library** (generic) | Lives in the **sample** (specific) |
|---|---|---|
| OpenAPI fetch/reduce/invoke | ✅ | — |
| The AI tool implementations | ✅ (`OpenApiExplorerTools`, `GenerativeUiTools`) | — |
| UI-DSL model + inflator + state | ✅ | — |
| Canvas host control + DI extension | ✅ | — |
| **UI extensibility mechanism** (registry, `AddStyle`/`AddControl`/`AddScreen`, discovery tools, catalog seeding) | ✅ (generic) | — |
| Base URL, OpenAPI location | provided *by* the app at startup | ✅ (config) |
| REST models / DTOs | ❌ (never referenced) | ✅ (`.Shared`, typed, source-gen JSON) |
| Endpoint names / routes | ❌ | ✅ (server) |
| **Registered styles/controls/screens** (the actual brand styles, `ProductImage`, `CheckoutScreen`) | ❌ (never referenced) | ✅ (app registers them + supplies the XAML resources / controls / screens) |
| System prompt / seed data | ❌ | ✅ |

The app hands the library its concrete pieces via a single DI call:

```csharp
builder.Services.AddGenerativeUi(options =>
{
    options.BaseAddress = new Uri(baseUrl);              // where the server is
    options.OpenApiPath = "/openapi/v1.json";            // where the spec is
    options.JsonSerializerContext = GardenJsonContext.Default; // typed (de)serialization
    options.ConfigureHttpClient = client =>              // auth/transport; model never sees creds
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
    options.AllowedHosts = [new Uri(baseUrl).Host];      // SSRF allowlist (defaults to BaseAddress host)

    // App-specific UI vocabulary (all optional; see the Extensibility appendix).
    // The same Add…/Remove… calls are available later via the DI-resolvable
    // GenerativeUiRegistry (e.g. register admin screens after sign-in, remove on sign-out):
    options.Ui.AddStyle("danger", "Destructive actions like delete/remove.", appliesTo: ["Button"], resourceKey: "DangerButtonStyle");
    options.Ui.AddControl<ProductImage>("ProductImage", "Use for any product image (adds the brand watermark).");
    options.Ui.AddScreen<CheckoutScreen>("CheckoutScreen", "The official checkout surface; use for any checkout.");
});
```

The library never references the sample's models; it uses the app-supplied
`JsonSerializerContext`/options so the generic invoker can (de)serialize typed and AOT-friendly.

## 9. State & data binding (overview)

The canvas is **stateful and data-bound**: the model builds structure once, then live-edits and
reads back the data. Because the model authors no view models, all display and form data live in a
**single persistent, observable state graph** — a `UiObject` tree (the
[State & Binding Model appendix](./appendix-binding-model.md)).

- **One `StateRoot`.** Display nodes bind one-way; editable `Field`/`Entry` nodes bind two-way — to
  the *same* graph. (Earlier drafts split `data` and `form`; these are now unified.)
- **Structure vs. data.** `render_ui` describes structure and merges any `data`/`form` into the
  graph. Data changes go through **JSON Patch** (`apply_patch`), `set_state`, or `set_field` — the
  bound UI (including `itemsBind` lists) updates **in place, without re-inflation**.
- **`CanvasState`** (singleton) exposes the current root `View` + `IsBusy`/`IsEmpty` + the confirm
  overlay, and **owns the persistent `StateRoot`** across re-inflation; the `GenerativeCanvasView`
  binds to it.

This mutate-don't-repaint model, and its AG-UI-compatible snapshot/delta shapes, are detailed in the
[State & Binding Model appendix](./appendix-binding-model.md) and
[Protocol Alignment appendix](./appendix-protocol-alignment.md); the DSL is in the
[UI-DSL appendix](./appendix-ui-dsl.md).

## 10. Threading & lifecycle

- Tools are invoked by `FunctionInvokingChatClient` **off the UI thread**. Any canvas/state
  mutation marshals to `MainThread`.
- The OpenAPI processor fetches/reduces **once** at startup (or lazily on first use) and caches.
- A "new chat" / `clear_ui` resets `CanvasState` (view + `StateRoot`) and the message history.

## 11. Approval & destructive actions

**One approval mechanism for server writes.** `write_api` is `ApprovalRequired`; its
`Microsoft.Extensions.AI` tool-calling infrastructure automatically pauses the call and presents the
app's Approve/Reject banner (`ToolApprovalRequestContent` / `ToolApprovalResponseContent`, also
AG-UI-aligned). Once intent and parameters are clear, the model invokes `write_api` directly. It
must **not** ask for a typed "yes" or call `show_confirm` first, because that creates duplicate
approval.

`show_confirm` remains only for **UI-local decisions that do not immediately invoke
approval-gated `write_api`** (for example, discarding unsaved local form edits).

## 12. Configuration

| Setting | Purpose | Default |
|---|---|---|
| `Api:BaseAddress` | Server base address | `http://localhost:5225` |
| `Api:OpenApiPath` | OpenAPI document path | `/openapi/v1.json` |
| `AI:Endpoint` / `AI:ApiKey` / `AI:DeploymentName` | Chat model config (as in Garden) | — |

The AI settings reuse the shared `ai-attributes-secrets` user-secrets id used by the other
AIExtensions samples.

## 13. Security considerations

- **SSRF / base-URL trust:** the invoker only calls the configured base address (+ optionally an
  allowlist); it does not follow arbitrary model-supplied hosts. Paths are resolved relative to
  the configured base.
- **Method gating:** the read/write split makes mutations explicit and approval-gated.
- **Payload caps:** responses fed back to the model are size-capped/truncated to protect context.
- **No secrets in the DSL:** the UI-DSL is data + layout only; no code execution. Raw-XAML
  inflation (stretch) is riskier and stays behind an explicit opt-in flag.

## 14. MVP scope & acceptance

Feature parity with `AIExtensions.Sample.Garden`, server-backed and generatively rendered.

Acceptance scenarios (each must work end to end):

1. **List** — "what are the products?" → generic GET → list view.
2. **Detail** — "show me the basil seeds" → generic GET → detail card.
3. **Create (bound form)** — "add a new product called pears" → form (name prefilled) → "set the
   quantity to 3" (`set_field`) → "save for me" (`get_state` + `write_api POST`).
4. **Delete (confirm)** — "delete the tomato seeds" → detail/danger action → `write_api DELETE`;
   the infrastructure presents the single Approve/Reject UI automatically.
5. **Cart** — add / change qty / remove / clear (server-backed, rendered).
6. **Orders** — checkout (approval) / list / reorder / clear.
7. **Reviews** — submit / list / per-product.
8. **Recommendations** — starter bundle.
9. **Registered control** — a product image renders via the app's `ProductImage` presenter
   (watermarked), because the model followed the control's description for product images.
10. **Registered style** — destructive buttons use the app's `danger` style the model selected.
11. **Full screen handoff** — "checkout" presents the app's `CheckoutScreen` (not a model-composed UI)
    via `present_screen`; the screen self-loads the cart.

## 15. Future direction

- **Split the library** into `Microsoft.Maui.AI.GenerativeUI.OpenApi` (UI-agnostic, `net10.0`)
  and `Microsoft.Maui.AI.GenerativeUI` (MAUI UI) once the boundary is proven.
- **Server-side UI hints:** annotate the OpenAPI doc with rendering/semantic hints so the model
  produces better UI with less prompting.
- **Richer DSL:** grids, templated lists (item template + items binding), charts, images from URLs.
- **Richer extensibility:** control slots/composition, more `Presentation` modes, designer-time
  tooling to author/register controls and screens, and hot-reloadable catalogs.
- **Persisted/disk-cached reduced spec** keyed by ETag/version.
- **On-device model** via `Microsoft.Maui.Essentials.AI`.
- **Graduation** from experimental (`IsPackable=false`) to a shipped package.

## 16. Open questions

Resolved items are marked ✅; the rest are the remaining design/implementation questions, grouped
by area.

### Architecture & boundary
1. Is the OpenAPI-driven generic invoker the right call, or do we also want an *optional* typed
   data-tool path generated from `.Shared` for apps that prefer strong typing? (MVP: generic
   only.)
2. ✅ **Both:** on-demand tools are implemented; compact-index seeding is adopted/configured but
   automatic chat-prompt injection remains an implementation follow-up.
3. ✅ No library server piece for the MVP; the app server only emits stock OpenAPI.

### Server-API tools
4. ✅ Read/write split: `read_api` is GET-only; `write_api` is mutating-only and approval-gated.
5. ✅ Flat path/query args plus explicit `body`, with method/route resolved by operationId.
6. Reduction keeps all authored semantics (descriptions + constraints) and only strips OpenAPI
   structure — are there *structural* elements (examples, response codes, schema depth) that
   actually carry usage meaning and should be kept too? (Authored **descriptions are never
   clipped** — that's a fixed principle, not an open question.)

### Client-UI tools & DSL
7. ✅ Read-only display supports both: literal text for static one-offs, binding for anything that
   can change (see the UI-DSL reliability rule).
8. ✅ Collections support both pre-expanded static rows and a bound `itemsBind` template; bound is
   required for live add/remove/quantity updates.
9. Which additional nodes should follow A2UI parity next (`Modal`, `Tabs`, `Slider`,
   `ChoicePicker`, `DateTimeInput`, `CheckBox`)?
10. Rendered controls currently signal via synthetic chat turns (`intent`). Should a future
    AG-UI-compatible event channel replace or complement them?

### Extensibility (styles / controls / screens) — see [Extensibility appendix](./appendix-extensibility.md#open-questions)
10a. Uniform node `type` set (built-ins + registered) vs. explicit `Control`/`Screen` wrappers to
     avoid name collisions? (Lean: uniform + collision validation.)
10b. How much of the UI capability catalog do we **seed** vs. lazily `describe_*` as it grows?
10c. Do controls support children/slots in the MVP, or are they leaves? How do full screens declare
     "self-loaded" vs. "model-supplied" data?
10d. **Dynamic catalog:** when registrations change mid-session (e.g. after sign-in), how do we
     re-inform the model — reseed the system note or just let the next turn's catalog reflect it —
     and how do we handle a control being unregistered while it's on screen?
10e. **Registration sources:** support imperative now + (future) source-generated registration; how
     are duplicate names/aliases reconciled?

### Dynamic binding & generic model — see [Binding Model appendix](./appendix-binding-model.md#open-questions)
10f. Indexer-path bindings (`[a][b].Value`) vs. a custom path-walking `BindingBase` — which is more
     reliable/AOT-friendly for the generic `UiObject` tree?
10g. Typed vs. stringly leaves: store typed values in the tree, or keep strings and coerce only at
     the edges (`get_state`/converters)?
10h. ✅ Resolved: one persistent observable `StateRoot`; JSON Patch updates data/form in place.
10i. How do we key/diff `itemsBind` collections so patches can address stable IDs instead of
     shifting positional indices?
10j. Should failed patch sequences apply transactionally (all-or-nothing) and trigger an automatic
     snapshot resync, like AG-UI?

### Interaction & UX
11. ✅ Server writes use **only** `write_api`'s automatic approval UI. `show_confirm` is reserved
    for UI-local choices and must not precede `write_api`.
12. How does "save for me" work precisely — model calls `get_state` then `write_api`, or a
    submit `intent` carries the values? What happens on validation errors from the server?
13. New-chat/reset semantics: does resetting the chat clear the canvas and server-side cart?

### Data & typing
14. Does `.Shared` target `net10.0` or `netstandard2.0`? Does the MAUI client actually *need*
    the typed models, or only the `JsonSerializerContext`? (For the invoker it needs the context;
    for app-side typed logic it may want the records too.)
15. Is System.Text.Json **source generation** viable across both the minimal-API server options
    and the client-supplied context without duplication?

### Non-functional
16. Error surfaces: how are server 4xx/5xx and malformed model output shown to the user vs. fed
    back to the model for self-correction?
17. Telemetry/logging: what do we log for debugging the generative loop (tool calls, DSL docs,
    reduced spec)?
18. Testing strategy: what's unit-testable (reducer, DSL parser/validator) vs. manual smoke
    (inflator output)?
