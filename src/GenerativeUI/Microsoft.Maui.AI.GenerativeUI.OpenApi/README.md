# Microsoft.Maui.AI.GenerativeUI.OpenApi

> **Experimental.** Part of the [.NET MAUI Labs](https://github.com/dotnet/maui-labs) Generative UI
> experiment. APIs are preview and may change without notice.

OpenAPI processing for **Generative UI**. This library lets an AI agent discover and call *any* REST
API described by an OpenAPI document — without the library (or the app) knowing that API's models or
routes at compile time.

It does three things:

1. **Reduce** — parse an OpenAPI document (via `Microsoft.OpenApi`) into a compact, model-friendly
   `ReducedSpec`. Structural plumbing (envelopes, `$ref` machinery, media-type maps) is stripped;
   **all authored descriptions and constraints are preserved verbatim** — never clipped.
2. **Invoke** — execute an operation generically by its `operationId` plus a flat `args` object.
   The invoker routes each argument to the path, query string, or request body using the operation's
   own parameter metadata. The HTTP method comes from the operation, so a read can never become a
   write.
3. **Discover** (on top of the above) — small building blocks an agent uses to list endpoints,
   describe one operation (schema inlined one level deep), and describe a model.

## Quick start

```csharp
using Microsoft.Maui.AI.GenerativeUI.OpenApi;

// Reduce a fetched OpenAPI document into the compact spec.
ReducedSpec spec = OpenApiReducer.Reduce(openApiJson);

// Build a request for GET /products/{sku} by operationId.
var invoker = new ApiInvoker(new GenerativeOpenApiOptions
{
    BaseAddress = new Uri("https://api.garden.example"),
});

using var request = invoker.BuildRequest(spec, "getProduct", new JsonObject { ["sku"] = "basil-seeds" });
// request.Method == GET, request.RequestUri == https://api.garden.example/products/basil-seeds
```

## Design notes

- **Descriptions are never truncated.** Reduction only flattens and de-duplicates *structure*.
- **`operationId` is the invocation handle** — authored when present, otherwise a deterministic
  `{method}_{path}` (e.g. `get_products_by_sku`).
- **JSON-only for the MVP.** Multipart/binary bodies and external `$ref`s are out of scope.

See the [Generative UI specs](https://github.com/dotnet/maui-labs/tree/main/docs/GenerativeUI/spec)
for the full design.
