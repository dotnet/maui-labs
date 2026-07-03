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
 ┌───────────────┐   parse (System.Text.Json)
 │ OpenApiReducer│ ─────────────────────────────────▶  ReducedSpec
 └──────┬────────┘   { endpoints[], models{} }         (compact, model-friendly)
        │
        ▼
 ┌────────────────────┐   list_endpoints / describe_endpoint / describe_model / search_api
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
      "requestModel": null,          // schema name, or null
      "responseModel": "Product"     // schema name, or null
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
          "description": "Optional stock count.", "enum": null }
      ]
    }
    /* ... */
  }
}
```

### What's kept (in full)

- Per operation: `operationId` (synthesized from method+path if missing), `method`, `path`,
  **full `summary` and `description`**, `tags`, the parameter list (name/in/type/required + **full
  description**), request/response **model names**.
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
- **Depth cap + cycle guard:** resolution stops at a configurable depth; cycles are broken by
  emitting the model name reference. Prevents unbounded expansion.

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
describe_endpoint(operationId?: string, method?: string, path?: string) -> EndpointDetail
```
Full detail for one operation: parameters (name/in/type/required/description) and request/response
model **names**. Identified by `operationId` or by `method`+`path`.

### `describe_model`

```
describe_model(name: string) -> ModelSchema
```
The resolved schema for one model: properties (name/type/required/description). Referenced nested
models are named so the model can `describe_model` them too.

### `search_api`

```
search_api(query: string) -> { endpoints: Endpoint[], models: string[] }
```
Finds endpoints and models related to a free-text query (substring/keyword match over
paths/summaries/tags/model names for the MVP; could become embeddings later). Answers "find
related" and "which endpoint does X".

### `read_api` (safe)

```
read_api(path: string, query?: object, pathParams?: object) -> JSON
```
Invokes a **GET** operation. `path` may be a template (`/products/{sku}`) filled from `pathParams`;
`query` becomes the query string. Returns the response body (capped/normalized JSON). Never
mutates; **not** approval-gated.

### `write_api` (mutating, approval-gated)

```
write_api(method: string, path: string, body?: object, query?: object, pathParams?: object) -> JSON
```
Invokes a **POST/PUT/PATCH/DELETE** operation. Carries
`[ExportAIFunction(ApprovalRequired = true)]`, so `FunctionInvokingChatClient` pauses for the
inline approve/reject banner before executing. Returns the response body (or status summary for
`204`).

> **Open question:** one `write_api` with a `method` arg vs. separate `create`/`update`/`delete`
> tools. A single approval-gated `write_api` keeps the surface tiny; per-verb tools might read
> more clearly to the model. See [Open Questions](#open-questions).

## 5. Invocation (`ApiInvoker`)

Generic HTTP execution shared by `read_api`/`write_api`.

- **URL:** resolve `path` relative to the configured `BaseAddress`; substitute `{name}` path
  params from `pathParams`; append `query`. Reject absolute/off-host URLs (see §7).
- **Method:** as given (`read_api` forces GET).
- **Body:** `body` serialized to JSON using the **app-supplied `JsonSerializerContext`/options**
  when present (typed, AOT-friendly), else reflection-based `JsonSerializer`.
- **Request headers:** `Accept: application/json`; `Content-Type: application/json` for bodies;
  app-supplied default headers (e.g. auth) via `HttpClient` configuration.
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

## 6. Limits & performance

- **Compactness by structure, not by clipping:** the reduced spec is small because it strips
  OpenAPI plumbing, not because it shortens text. Descriptions and constraints are kept in full
  (see §3). We manage size via **on-demand expansion** — `list_endpoints` returns lightweight rows
  (path + summary), and full descriptions/models are pulled only when the model asks via
  `describe_endpoint`/`describe_model`.
- **Seeding the prompt:** a compact endpoint index (path + summary + tags) may optionally be seeded
  into the system prompt. Full descriptions still arrive intact through the describe tools, so
  seeding never means losing detail. Whether/how much to seed is an open question (§ below).
- **Pagination/large *data* results:** `read_api` **response bodies** (runtime data, not the spec)
  are size-capped; the model is told when a result was truncated and can refine (e.g. add a
  `query`/limit param if the API supports it). This cap applies to fetched data, never to API
  descriptions.
- **Caching:** the reduced spec is computed once; `describe_model` results can be memoized.

## 7. Security

- **Host pinning / SSRF:** the invoker only calls the configured `BaseAddress` (plus an optional
  allowlist). Model-supplied absolute URLs or foreign hosts are rejected. Paths are treated as
  relative.
- **Method gating:** the read/write split makes every mutation explicit and approval-gated.
- **Auth:** handled by the app via `HttpClient` (headers/handlers); the model never sees or
  supplies credentials. The reduced spec omits security-scheme detail.
- **Payload caps:** request and response sizes are bounded.
- **No code execution:** tools move data only; there is no eval path.

## 8. Errors & self-correction

- **Spec unavailable:** explorer tools return a clear, model-relayable error.
- **Unknown endpoint/param:** `describe_endpoint`/`read_api` return "no such operation" / "missing
  required param `sku`", letting the model correct itself.
- **Server 4xx/5xx:** returned as structured `ProblemDetails`; the model can surface validation
  errors (e.g. "Price is required") in the UI and re-prompt the user.

## Open questions

1. **Read/write split vs. single `call_api`** with per-method approval — which is more reliable
   for the model, and how do we gate approval dynamically if it's a single tool? (Static
   `ApprovalRequired` favors the split.)
2. **Per-verb write tools?** `create`/`update`/`delete` vs. one `write_api(method,...)`.
3. **Seed the reduced spec into the system prompt** vs. tools-only discovery — what's the size
   budget, and does seeding measurably reduce tool round-trips?
4. **OpenAPI parsing:** hand-rolled System.Text.Json subset parser vs. taking a dependency on
   `Microsoft.OpenApi`. Trade simplicity/size for fidelity (`allOf`/`oneOf`, complex refs).
5. **Reduction fidelity:** the reducer preserves all authored semantics (descriptions +
   constraints) and only strips structural plumbing — is that split exactly right? Are there
   structural elements (e.g. examples, response codes) that actually carry usage meaning and
   should therefore be kept too? (Default: keep examples; keep primary + error response shapes.)
6. **`operationId` synthesis:** how do we name operations that lack an `operationId` so it's stable
   and legible?
7. **Parameter passing ergonomics:** flat `pathParams`/`query`/`body` objects vs. a single merged
   `arguments` object the invoker splits using the reduced spec. Which yields fewer model errors?
8. **Search quality:** substring match (MVP) vs. embeddings for `search_api`. When is substring
   insufficient?
9. **Auth/config surface:** how does an app inject auth headers/handlers, base URL, and allowlist
   through `AddGenerativeUi`?
10. **Response shaping:** should the invoker pre-shape/trim responses for rendering, or hand raw
    JSON to the model and let it shape via the DSL?
11. **Versioned APIs:** how do we handle `/api/v1` vs `/api/v2` and multiple servers in one doc?
