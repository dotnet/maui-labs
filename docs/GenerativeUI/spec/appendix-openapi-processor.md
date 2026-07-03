# Appendix: OpenAPI Processor & Invoker

> **Status:** Draft (v0.1) — for iteration. See the [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md).

This appendix defines how the library turns a server's **OpenAPI document** into a small,
model-friendly set of **server-API tools**, and how it **invokes** endpoints generically. The
goal is to let the model discover and call *any* REST API the app points it at — without the
library knowing that API's models or routes in advance.

## 1. Pipeline

```
 startup / first use
        │
        ▼
 ┌───────────────┐   GET {BaseAddress}{OpenApiPath}   ┌──────────────┐
 │  OpenApiCache │ ─────────────────────────────────▶ │   server     │
 └──────┬────────┘   (cache in memory; optional disk) └──────────────┘
        │ raw OpenAPI JSON
        ▼
 ┌───────────────┐   parse (Microsoft.OpenApi)
 │ OpenApiReducer│ ─────────────────────────────────▶  ReducedSpec
 └──────┬────────┘   { endpoints[], models{} }         (compact, model-friendly)
        │
        ▼
 ┌────────────────────┐   list_endpoints / describe_endpoint / describe_model
 │ OpenApiExplorerTools│  read_api / write_api
 └─────────┬──────────┘
           │ read_api / write_api
           ▼
 ┌───────────────┐   HTTP request (method, path, params, body)   ┌──────────────┐
 │  ApiInvoker   │ ─────────────────────────────────────────────▶│   server     │
 └───────────────┘   JSON response (capped/normalized)            └──────────────┘
```

## 2. Fetching & caching (`OpenApiCache`)

- **Source:** `GET {BaseAddress}{OpenApiPath}` (default `/openapi/v1.json`, produced by ASP.NET
  Core `MapOpenApi()`).
- **Parse:** the raw document is parsed and validated with **`Microsoft.OpenApi`** (the
  Microsoft-owned OpenAPI object model). It reads OpenAPI 3.0 **and** 3.1, resolves internal
  `$ref`s, and models `allOf`/`oneOf`/`anyOf` composition, so the reducer projects an
  already-correct object graph rather than re-implementing spec fidelity. `Microsoft.OpenApi` is a
  document model only — it does **not** build or send HTTP requests; request assembly is the
  `ApiInvoker`'s job (§5).
- **When:** eagerly at startup (background) or lazily on first server-API tool use — configurable.
- **Cache:** in memory for the process lifetime. Optional disk cache keyed by ETag/`version` for
  faster cold starts (future).
- **Refresh:** manual `refresh` (and future ETag-based revalidation). A stale spec is better than
  none; refresh is best-effort.
- **Failure:** if the spec can't be fetched, server-API tools return a clear error the model can
  relay ("I can't reach the API right now").

## 3. Reduction (`OpenApiReducer`)

OpenAPI documents are large, but that size is mostly **structural overhead** (envelopes, wrappers,
repeated `$ref` machinery, media-type maps, response-code trees), not meaning. The reducer removes
that overhead while **preserving every piece of authored semantics** — most importantly the
`description` fields.

> **Descriptions are never truncated.** `summary`, `description`, parameter descriptions, model
> descriptions, and property descriptions are authored on purpose to tell a consumer how to use
> the API correctly. They are carried through **verbatim and in full**. Reduction only flattens
> and de-duplicates *structure*; it never clips *text*. If a description is long, it stays long.

The reducer projects the raw doc into a compact `ReducedSpec`:

```jsonc
{
  "endpoints": [
    {
      "operationId": "getProduct",
      "method": "GET",
      "path": "/products/{sku}",
      "summary": "Get a product by sku.",                 // verbatim
      "description": "Returns the full product record …",  // verbatim, full length
      "tags": ["products"],
      "parameters": [
        { "name": "sku", "in": "path", "type": "string", "required": true,
          "description": "Stable product identifier used across the catalog and cart …" } // verbatim
      ],
      "responseModel": "Product"     // response model name (this GET has no body, so requestModel is omitted)
    }
    /* ... */
  ],
  "models": {
    "Product": {
      "description": "A product in the garden shop catalog …",  // verbatim
      "properties": [
        { "name": "sku",   "type": "string",  "required": true,
          "description": "Stable id used by tools." },          // verbatim
        { "name": "name",  "type": "string",  "required": true,
          "description": "Display name shown in chat and on cards." },
        { "name": "price", "type": "number",  "required": true,
          "description": "Unit price in USD.", "format": "decimal" },
        { "name": "quantity", "type": "integer", "required": false,
          "description": "Optional stock count." }
      ]
    }
    /* ... */
  }
}
```

> **Absent keys are omitted, not nulled.** To stay compact, the reducer emits a key only when it
> carries a value: a GET with no body has no `requestModel`, a property with no enum has no `enum`.
> The output never contains `"enum": null` / `"requestModel": null` placeholder keys.

### What's kept (in full)

- Per operation: `operationId` (**authored value when present**; otherwise a deterministic
  `{method}_{path}` synthesized id with path params folded in — e.g. `GET /products/{sku}` →
  `get_products_by_sku`), `method`, `path`, **full `summary` and `description`**, `tags`, the
  parameter list (name/in/type/required + **full description**), request/response **model names**.
- Per model: **full model `description`**, and every property with name/type/required + **full
  description**, plus meaning-bearing constraints (`format`, `enum`, `default`, `pattern`,
  `minimum`/`maximum`, `nullable`). Nested models are referenced by name, not inlined.

Rule of thumb: **if a field could change how the model calls the API, it is kept.** Descriptions
and constraints always qualify.

### What's dropped (structure only)

- OpenAPI envelope/plumbing: media-type wrappers (`content → application/json → schema`),
  `$ref` indirection collapsed to model names, repeated component boilerplate.
- Servers block and security-scheme internals (auth is handled by the app, see §7) — but any
  human-readable auth **description** relevant to usage is surfaced.
- Non-primary response bodies are summarized to "success returns `<Model>`; errors return
  `ProblemDetails`" **without dropping** any error description text that exists.
- `allOf`/`oneOf`/`anyOf` are flattened into a single effective schema, **merging** (not dropping)
  the descriptions and constraints of each branch.

Explicitly **not** dropped: examples that carry usage intent are kept (see
[Open Questions](#open-questions) — default is to keep examples too).

### Schema / `$ref` resolution

- `$ref`s are resolved to **model names** and registered in `models`. The `describe_model` tool
  expands one model on demand.
- **Depth cap + cycle guard:** resolution stops at a configurable depth (default **5**); cycles are
  broken by emitting the model name reference. Prevents unbounded expansion.
- **Internal refs only.** Only in-document component `$ref`s are resolved. **External/remote `$ref`s**
  (other files or URLs) are **not supported** in the MVP — they're surfaced as an opaque model-name
  reference and logged, never fetched.

### Content-type scope (MVP)

The MVP assumes **`application/json`** request and response bodies (what `MapOpenApi()` emits by
default). Operations whose primary body is non-JSON — `multipart/form-data` (file upload),
`application/x-www-form-urlencoded`, binary streams — are **out of scope**: they still appear in
`list_endpoints` (so the model can see them), but `read_api`/`write_api` will report them as
unsupported rather than guess an encoding.

## 4. Server-API tools (`OpenApiExplorerTools`)

All are `[ExportAIFunction]` methods on a DI-registered instance; they read the cached
`ReducedSpec` and (for the last two) call the `ApiInvoker`.

### `list_endpoints`

```
list_endpoints(tag?: string, query?: string) -> Endpoint[]
```
Returns the compact endpoint index, optionally filtered by `tag` or a substring `query` over
path/summary/tags. This is the model's map of the API.

### `describe_endpoint`

```
describe_endpoint(operationId: string) -> EndpointDetail
```
Full detail for one operation: parameters (name/in/type/required/description) plus the **request and
response schemas inlined one level deep** — the immediate properties (name/type/required/description
+ constraints) of the request and response models. **Nested** models are referenced by name so the
model can `describe_model` them if it needs to go deeper. This makes a typical "learn the shape then
call" flow a single discovery hop instead of chaining `describe_endpoint` → `describe_model`.
Identified by `operationId` (authored or synthesized — see §3).

### `describe_model`

```
describe_model(name: string) -> ModelSchema
```
The resolved schema for one model: properties (name/type/required/description). Referenced nested
models are named so the model can `describe_model` them too. Use it to drill past the one level that
`describe_endpoint` already inlines.

### `read_api` (safe)

```
read_api(operationId: string, args?: object) -> JSON
```
Invokes a **GET** operation by `operationId`. `args` supplies **path and query values as flat keys**
(e.g. `read_api("getProduct", { sku: "basil-seeds" })`); the invoker routes each value to the path
template or query string using the parameter's `in` from the ReducedSpec (§5). Returns the response
body (capped/normalized JSON). Never mutates; **not** approval-gated. Rejects a non-GET operationId.

### `write_api` (mutating, approval-gated)

```
write_api(operationId: string, args?: object) -> JSON
```
Invokes a **POST/PUT/PATCH/DELETE** operation by `operationId`. `args` carries **path/query values
as flat keys** and the **request payload under an explicit `body`** — e.g.
`write_api("updateCartItem", { sku: "tomato-seeds", body: { quantity: 5 } })` or
`write_api("createProduct", { body: { name: "Pears", price: 3.49 } })`. The method comes from the
operation definition (not the model), so the model can't turn a read into a write. Carries
`[ExportAIFunction(ApprovalRequired = true)]`, so `FunctionInvokingChatClient` pauses for the
inline approve/reject banner before executing. Returns the response body (or status summary for
`204`).

**Write-then-read convention.** A mutation returns the updated resource where the API provides one,
but the model's rendered UI should reflect canonical server state — so the guidance is to **follow a
write with the relevant read** when the write's response isn't the exact shape being displayed (e.g.
`addCartItem` → `getCart` before rendering the cart). The sample scenarios (§ in the Garden doc)
follow this pattern.

## 5. Invocation (`ApiInvoker`)

Generic HTTP execution shared by `read_api`/`write_api`. Both identify the operation by
`operationId`; the invoker looks it up in the ReducedSpec and assembles the request from `args`.

- **Operation lookup:** resolve `operationId` → `{ method, path template, parameter list, request
  body model }`. Unknown id ⇒ "no such operation" error (self-correctable).
- **Argument routing:** for each declared parameter, take `args[name]` and place it by the
  parameter's **`in`** — `path` params substitute `{name}` in the template (URL-encoded); `query`
  params append to the query string. The request payload is taken from **`args.body`** and serialized
  as JSON. Because routing is driven by spec metadata, path/query keys can't be confused with body
  fields.
- **URL:** the resolved path is combined **relative to** the configured `BaseAddress`. Absolute or
  off-host URLs are never accepted from the model (see §7).
- **Method:** taken from the **operation definition**, not from `args` — `read_api` only resolves GET
  operations; `write_api` only resolves POST/PUT/PATCH/DELETE.
- **Body:** `args.body` serialized to JSON using the **app-supplied `JsonSerializerContext`/options**
  when present (typed, AOT-friendly), else reflection-based `JsonSerializer`.
- **Request headers:** `Accept: application/json`; `Content-Type: application/json` for bodies;
  app-supplied default headers (e.g. auth) via `HttpClient` configuration (§6.1).
- **Response:** read as JSON; on success return the body. On non-2xx, return a structured error
  `{ status, title, detail }` (parsing RFC 7807 `ProblemDetails` when present) so the model can
  react (e.g. surface a validation message).
- **Caps:** response bodies fed back to the model are size-capped/truncated with a marker to
  protect the context window.

### JSON context integration

The library never references app models. Instead the app registers its context:

```csharp
options.JsonSerializerContext = GardenJsonContext.Default;
```

The invoker uses it for (de)serialization when the shape is known, falling back to
`JsonElement`/`JsonNode` for opaque payloads. This gives typed, source-generated, AOT-friendly
serialization **without** a library→app dependency.

## 6. Configuration, limits & performance

### 6.1 Configuration (`AddGenerativeUi` options)

The OpenAPI/invoker side is configured through the same `AddGenerativeUi(options => …)` call used
for the UI. The model never sees any of this — it's all app-owned:

| Option | Default | Purpose |
|---|---|---|
| `BaseAddress` | *(required)* | Server root; **every** call is resolved relative to it. |
| `OpenApiPath` | `/openapi/v1.json` | Where the spec is fetched from. |
| `JsonSerializerContext` | *(none → reflection)* | Typed, AOT-friendly (de)serialization; falls back to `JsonElement`/`JsonNode` for opaque payloads. |
| `ConfigureHttpClient` | *(none)* | Callback to set headers/timeouts (e.g. auth) on the invoker's `HttpClient`; message handlers may also be added. Credentials live here, never in the model. |
| `AllowedHosts` | `[ BaseAddress.Host ]` | SSRF allowlist. Requests to any other host are rejected. |
| `SpecFetch` | `Eager` | `Eager` (background fetch at startup) or `Lazy` (on first server-API tool use). |
| `SeedEndpointIndex` | `true` | Seed the compact endpoint index into the system prompt (see §6.2). |
| `MaxResponseBytes` | `64 KB` | Cap on a response body fed back to the model; larger results are truncated with a marker. |
| `MaxRequestBytes` | `1 MB` | Cap on a serialized request body. |
| `RefResolutionDepth` | `5` | `$ref` expansion depth before falling back to a name reference. |

```csharp
builder.Services.AddGenerativeUi(options =>
{
    options.BaseAddress = new Uri("https://api.garden.example");
    options.OpenApiPath = "/openapi/v1.json";
    options.JsonSerializerContext = GardenJsonContext.Default;

    // Auth + transport — the model never sees credentials:
    options.ConfigureHttpClient = client =>
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenProvider.Current);

    options.AllowedHosts = ["api.garden.example"];   // SSRF allowlist (defaults to BaseAddress host)
    options.SpecFetch = SpecFetchMode.Eager;         // background fetch at startup
});
```

### 6.2 Limits & performance

- **Compactness by structure, not by clipping:** the reduced spec is small because it strips
  OpenAPI plumbing, not because it shortens text. Descriptions and constraints are kept in full
  (see §3). We manage size via **on-demand expansion** — `list_endpoints` returns lightweight rows
  (operationId + path + summary), `describe_endpoint` inlines request/response schemas **one level**,
  and deeper/nested models are pulled only when the model asks via `describe_model`.
- **Seeding the prompt:** the compact endpoint index (operationId + method + path + summary + tags)
  is **seeded into the system prompt by default** (`SeedEndpointIndex`), so the model starts knowing
  the API's shape and often skips a `list_endpoints` round-trip. Full descriptions/schemas still
  arrive intact through the describe tools, so seeding never means losing detail.
- **Pagination/large *data* results:** `read_api` **response bodies** (runtime data, not the spec)
  are size-capped (`MaxResponseBytes`); the model is told when a result was truncated and can refine
  (e.g. add a `query`/limit arg if the API supports it). This cap applies to fetched data, never to
  API descriptions.
- **Caching:** the reduced spec is computed once; `describe_model` results can be memoized.

## 7. Security

- **Host pinning / SSRF:** the invoker only calls hosts in `AllowedHosts` (defaults to the
  `BaseAddress` host — see §6.1). Model-supplied absolute URLs or foreign hosts are rejected; paths
  are always treated as relative to `BaseAddress`.
- **Method gating:** the read/write split makes every mutation explicit and approval-gated; the HTTP
  method comes from the resolved operation, so the model can't escalate a read into a write.
- **Auth:** handled by the app via `ConfigureHttpClient`/handlers (§6.1); the model never sees or
  supplies credentials. The reduced spec omits security-scheme detail.
- **Payload caps:** request and response sizes are bounded (`MaxRequestBytes`/`MaxResponseBytes`).
- **No code execution:** tools move data only; there is no eval path.

## 8. Errors & self-correction

- **Spec unavailable:** explorer tools return a clear, model-relayable error.
- **Unknown operation/arg:** `describe_endpoint`/`read_api`/`write_api` return "no such operation" /
  "missing required arg `sku`", letting the model correct itself.
- **Unsupported operation:** a non-JSON (multipart/binary) or external-`$ref` operation returns a
  clear "unsupported for MVP" error rather than a bad guess.
- **Server 4xx/5xx:** returned as structured `ProblemDetails`; the model can surface validation
  errors (e.g. "Price is required") in the UI and re-prompt the user.

## Decisions (locked)

| # | Question | Decision |
|---|---|---|
| Parser | Hand-rolled STJ vs. `Microsoft.OpenApi` | **`Microsoft.OpenApi`** dependency — parses 3.0/3.1, resolves refs + composition (§2). |
| Invocation | `path`+`method`+params vs. `operationId`+args | **`read_api`/`write_api(operationId, args)`** — flat path/query keys, explicit `body` (§4–5). |
| operationId | How to name un-named ops | **Prefer authored**; else synthesize `{method}_{path}` with path params folded in (§3). |
| Discovery surface | Keep `search_api`? | **Dropped for MVP** — `list_endpoints(query)` + `describe_model` cover it. |
| describe_endpoint depth | Names-only vs. inline | **Inline request/response schema one level**; nested by name (§4). |
| Read/write split | Split vs. single `call_api` | **Split** — static `ApprovalRequired` on `write_api` is the safety net. |
| Write tools | Per-verb vs. one | **One `write_api`** by operationId (method comes from the spec). |
| Seeding | Seed vs. tools-only | **Seed the compact endpoint index** by default (`SeedEndpointIndex`) (§6.2). |
| Auth/config | How the app configures transport | **`AddGenerativeUi` options** — `ConfigureHttpClient`, `AllowedHosts`, caps, fetch mode (§6.1). |
| Content types | JSON vs. everything | **JSON-only for MVP**; multipart/binary/external-`$ref` are out of scope (§3). |
| Response shaping | Pre-shape vs. raw | **Hand raw (capped) JSON to the model**; it shapes via the UI-DSL — no server-side view logic. |

## Open questions

1. **Reduction fidelity edges:** we keep descriptions, constraints, examples, and primary + error
   response shapes. Are there other structural elements (multiple success codes, response headers)
   that actually carry usage meaning and should be kept too?
2. **Search quality (post-MVP):** when does substring `list_endpoints(query)` stop being enough, and
   what does a re-introduced semantic `search_api` (embeddings) look like?
3. **Versioned APIs / multiple servers:** how do we handle `/api/v1` vs `/api/v2` and a `servers`
   block with more than one entry in a single doc? (MVP assumes one `BaseAddress` + one doc.)
4. **Refresh/revalidation:** manual `refresh` is in; is ETag-based revalidation worth it for the
   MVP, and what triggers it?
