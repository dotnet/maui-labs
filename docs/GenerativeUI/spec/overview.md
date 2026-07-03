# Generative UI — Overview Spec

> **Status:** Draft (v0.1) — for iteration. Nothing here is final; every section is expected to
> change as we work through the [Open Questions](#open-questions).

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
| **Inflator** | The runtime component that turns a UI-DSL document into MAUI `View`s. |
| **CanvasState** | Client state holding the currently rendered view + busy/empty flags. |
| **FormState** | Bindable key/value state backing editable form fields. |
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
│  │  ApiInvoker     generic HTTP call    │   │  GenUiState     data + bindings     │   │
│  │  OpenApiExplorerTools                │   │  FormState      two-way form state  │   │
│  │    list_endpoints / describe_*       │   │  CanvasState + GenerativeCanvasView │   │
│  │    search_api / read_api / write_api │   │  GenerativeUiTools                  │   │
│  │                                      │   │    render_ui / set_field / get_state│   │
│  │                                      │   │    show_confirm / clear_ui          │   │
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
| `list_endpoints` | Compact list of operations: `operationId`, method, path, summary, tags. |
| `describe_endpoint` | Full detail for one operation: parameters + request/response schema names. |
| `describe_model` | Resolved schema for one model (properties, types, required). |
| `search_api` | Find endpoints/models related to a query string. |
| `read_api` | Invoke a **safe** (GET) endpoint and return JSON. |
| `write_api` | Invoke a **mutating** endpoint (POST/PUT/PATCH/DELETE); **requires approval**. |

There is intentionally **no** `list_all_products` / `add_to_list` / etc. Everything routes
through this generic surface, so the library needs no knowledge of the app's endpoints.

> **Descriptions are preserved.** "Reduced" means the OpenAPI *structure* is simplified for the
> model, **not** that any authored text is shortened. Endpoint/parameter/model/property
> descriptions — and meaning-bearing constraints like `enum`/`format`/`required` — are carried
> through **verbatim and in full**, because they exist to tell the model how to use the API
> correctly. Size is managed by lazy expansion (`describe_endpoint`/`describe_model`), never by
> clipping text. See the [OpenAPI processor appendix §3](./appendix-openapi-processor.md#3-reduction-openapireducer).

### 6.2 Client-UI tools (see [UI-DSL appendix](./appendix-ui-dsl.md))

| Tool | Purpose |
|---|---|
| `render_ui` | Render a UI-DSL document (`ui` + optional `data` + `bindings`) into the canvas. |
| `set_field` | Update one field in the active bound state (drives "set the quantity to 3"). |
| `get_state` | Read the current bound/form values (drives "save for me"). |
| `show_confirm` | Render a confirm overlay; resolves via button tap or the user typing "yes". |
| `clear_ui` | Reset the canvas to the welcome/empty state. |

## 7. Runtime loop

A representative turn ("show me the basil seeds"):

```
User → Chat: "show me the basil seeds"
Model → search_api("basil") / list_endpoints        (optional discovery)
Model → describe_endpoint("GET /products/{sku}")    (optional, learn params)
Model → read_api("/products/basil-seeds")           (server call, JSON back)
Model → render_ui({ ui: <detail card>, data: <product json> })   (canvas updates)
Model → Chat: "Here are the basil seeds."           (short text reply)
```

Key properties:

- **Discovery is cached.** After the first exploration the model has enough context; the reduced
  spec can also be seeded into the system prompt so most turns skip discovery.
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
| Base URL, OpenAPI location | provided *by* the app at startup | ✅ (config) |
| REST models / DTOs | ❌ (never referenced) | ✅ (`.Shared`, typed, source-gen JSON) |
| Endpoint names / routes | ❌ | ✅ (server) |
| System prompt / seed data | ❌ | ✅ |

The app hands the library its concrete pieces via a single DI call:

```csharp
builder.Services.AddGenerativeUi(options =>
{
    options.BaseAddress = new Uri(baseUrl);              // where the server is
    options.OpenApiPath = "/openapi/v1.json";            // where the spec is
    options.JsonSerializerContext = GardenJsonContext.Default; // typed (de)serialization
});
```

The library never references the sample's models; it uses the app-supplied
`JsonSerializerContext`/options so the generic invoker can (de)serialize typed and AOT-friendly.

## 9. State & data binding (overview)

The canvas is data-bound so the model can build a form, then *live-edit and read it back*.

- **`GenUiState`** holds the immutable `data` passed to `render_ui` (for display bindings).
- **`FormState`** is an `INotifyPropertyChanged` key/value store backing editable `Field`s.
  Two-way bindings mean `set_field("quantity","3")` updates the on-screen `Entry`, and
  `get_state()` reads whatever the user (or model) has entered.
- **`CanvasState`** (singleton) exposes the current root `View` + `IsBusy`/`IsEmpty`; the
  `GenerativeCanvasView` binds to it.

Binding details and the DSL are in the [UI-DSL appendix](./appendix-ui-dsl.md).

## 10. Threading & lifecycle

- Tools are invoked by `FunctionInvokingChatClient` **off the UI thread**. Any canvas/form
  mutation marshals to `MainThread`.
- The OpenAPI processor fetches/reduces **once** at startup (or lazily on first use) and caches.
- A "new chat" resets `CanvasState`, `FormState`, and the message history.

## 11. Approval & destructive actions

Two layered mechanisms, reusing the existing `Microsoft.Maui.AI.Attributes` approval flow:

1. **`write_api` is `ApprovalRequired`.** All mutating HTTP calls pause the chat and show the
   inline approve/reject banner before executing. This is the safety net.
2. **`show_confirm`** lets the model build a richer, in-canvas confirmation ("Delete *Heirloom
   Tomato Seeds*? This cannot be undone.") that resolves by button tap **or** by the user typing
   "yes"/"delete it". The model then issues the `write_api` call.

See [Open Questions](#open-questions) on whether both are needed or one suffices.

## 12. Configuration

| Setting | Purpose | Default |
|---|---|---|
| `Api:BaseUrl` | Server base address | `http://localhost:5225` |
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
4. **Delete (confirm)** — "delete the tomato seeds" → detail + `show_confirm` → "yes" →
   `write_api DELETE`.
5. **Cart** — add / change qty / remove / clear (server-backed, rendered).
6. **Orders** — checkout (approval) / list / reorder / clear.
7. **Reviews** — submit / list / per-product.
8. **Recommendations** — starter bundle.

## 15. Future direction

- **Split the library** into `Microsoft.Maui.AI.GenerativeUI.OpenApi` (UI-agnostic, `net10.0`)
  and `Microsoft.Maui.AI.GenerativeUI` (MAUI UI) once the boundary is proven.
- **Server-side UI hints:** annotate the OpenAPI doc with rendering/semantic hints so the model
  produces better UI with less prompting.
- **Richer DSL:** grids, templated lists (item template + items binding), charts, images from URLs.
- **Persisted/disk-cached reduced spec** keyed by ETag/version.
- **On-device model** via `Microsoft.Maui.Essentials.AI`.
- **Graduation** from experimental (`IsPackable=false`) to a shipped package.

## 16. Open questions

These are the things to iron out before/while building. Grouped by area.

### Architecture & boundary
1. Is the OpenAPI-driven generic invoker the right call, or do we also want an *optional* typed
   data-tool path generated from `.Shared` for apps that prefer strong typing? (MVP: generic
   only.)
2. Should the reduced spec be **seeded into the system prompt** at session start, fetched
   on-demand via tools, or both? What's the size budget?
3. Does the library need any **server-side** component at all for the MVP, or is "server just
   emits OpenAPI" sufficient? (Current assumption: sufficient.)

### Server-API tools
4. Read/write split vs. a single `call_api` with per-method approval — which is more reliable for
   the model and cleaner for approval? (See OpenAPI appendix Open Questions.)
5. How do we express **path/query/body** parameters to the model so it fills them correctly with
   minimal errors?
6. Reduction keeps all authored semantics (descriptions + constraints) and only strips OpenAPI
   structure — are there *structural* elements (examples, response codes, schema depth) that
   actually carry usage meaning and should be kept too? (Authored **descriptions are never
   clipped** — that's a fixed principle, not an open question.)

### Client-UI tools & DSL
7. Do display views bind to `data`, or is inlining data into the DSL acceptable/preferable for
   read-only views? (Forms clearly need binding.)
8. How are **collections** rendered in the MVP — pre-expanded rows emitted by the model, or a
   `List` + item-template + items-binding? (MVP leans pre-expanded for reliability.)
9. What is the **exact node vocabulary** for the MVP, and what styling tokens are fixed?
10. How do rendered controls signal back to the loop — synthetic chat turns (`intent`), direct
    tool re-entry, or a dedicated event channel?

### Interaction & UX
11. Confirmation: keep both `write_api` approval **and** `show_confirm`, or just one?
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
