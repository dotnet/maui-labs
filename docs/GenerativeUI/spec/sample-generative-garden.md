# Sample: GenerativeUI.Sample.Garden

> **Status:** Implemented component-composer vertical slice (v0.3), with the v0.2 fully generated
> primitive path preserved as Baseline Full Generation. See the
> [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md). Related: [UI-DSL](./appendix-ui-dsl.md),
> [OpenAPI processor](./appendix-openapi-processor.md).

This spec describes the **reference sample** that exercises the library end to end: a "Garden
shop" recreated from the existing in-memory [`AIExtensions.Sample.Garden`](../../../samples/AIExtensions.Sample.Garden),
but with its data moved to a **minimal-API server** and two runtime UI modes. It is
one concrete consumer of `Microsoft.Maui.AI.GenerativeUI` — the library stays app-agnostic; the
sample is where a real client and server are co-developed and share typed models.

## Component Composer v0.3

The sample starts in **Component Composer** mode. A dedicated typed-output model receives only the
active product, the current plan, user intent, and components whose required facet bindings exist.
It returns a versioned plan for `ProductDetailScaffold`; the renderer preserves scaffold and
unchanged component identity across follow-ups.

The five app-owned components live in `GenerativeUI.Sample.Garden.Components`: ProductHero,
ProductCoreInfo, DimensionsPanel, ColorGallery, and SeedGrowingTimeline. `Product` has optional
SeedDetails, Dimensions, and ColorOptions records. The composer mode is read-only in this slice.

The mode picker also exposes **Baseline Full Generation**, which retains the original primitive DSL,
state tools, write API, and automatic approval behavior.

## 1. Goals

- Prove the two tool families (server-API + client-UI) work against a real REST API.
- Preserve the full-generation Garden feature set (products, cart, orders, reviews,
  recommendations) as a runnable research baseline.
- Prove typed native component composition for focused product detail without hand-authoring every
  intent-specific page combination.
- Demonstrate the **shared-models** pattern: one `.Shared` project referenced by both server and
  client, with a **source-generated `JsonSerializerContext`** for typed/AOT (de)serialization.
- Serve as the runnable acceptance harness for the MVP scenarios in `overview.md` §14.

## 2. Project layout

Four sibling sample projects:

```
samples/
├── GenerativeUI.Sample.Garden.Components/ (native components + scaffold + typed compose tool)
│   ├── Components/         five native product components
│   ├── ProductDetailScaffold.cs
│   └── GardenComponentCatalog.cs
├── GenerativeUI.Sample.Garden.Shared/     (net10.0 class library)
│   ├── Models/            REST DTO records (shared by client + server)
│   ├── Requests/          request DTOs (create/update payloads)
│   └── GardenJsonContext.cs   [JsonSerializable] source-gen context
├── GenerativeUI.Sample.Garden.Server/     (Microsoft.NET.Sdk.Web)
│   ├── Program.cs         minimal API + AddOpenApi/MapOpenApi + 19 operation maps
│   └── GardenStore.cs     in-memory products/cart/orders/reviews + seed
└── GenerativeUI.Sample.Garden/            (MAUI app; net10.0-maccatalyst/-windows[/-android/-ios])
    ├── MauiProgram.cs     AddGenerativeUi(baseUrl) + AI + secrets + DevFlow
    ├── App.xaml
    ├── MainPage.xaml      2-col shell: [ GenerativeCanvasView ] [ Chat ]
    └── ViewModels/
        └── ChatViewModel.cs   chat loop + mode-filtered generated AIToolContext + IChatBridge
```

- **`.Shared`** references nothing app-specific; it's plain DTOs + the JSON context.
- **`.Server`** references `.Shared`.
- **`.Components`** references the generic UI library and `.Shared`; it owns Garden-specific views,
  descriptors, fallback, mode policy, metrics, and the typed composition tool.
- The **MAUI client** references the generic library, `.Components`, `.Shared`, and the AI tool-source
  generator.

All projects are added to `MauiLabs.slnx`. The workload-free `GenerativeUI.slnf` contains the
shipping OpenAPI library and its server-backed tests. `GenerativeUI.App.slnf` contains the native
composition tests and MAUI client; the CI workflow runs it in a separate macOS job with pinned
workloads.

## 3. Shared models (`.Shared`)

DTOs mirror the existing sample's domain, reshaped as REST resources. Records, `PascalCase`
properties, `System.Text.Json`-friendly.

```csharp
namespace GenerativeUI.Sample.Garden.Shared;

public record Product(string Sku, string Name, string Category, decimal Price, string Emoji, string? ImageUrl = null);

public record CartItem(string Sku, string Name, string Emoji, decimal Price, int Quantity)
{
    public decimal Subtotal => Price * Quantity;
}

public record Cart(IReadOnlyList<CartItem> Items)
{
    public decimal Total => Items.Sum(i => i.Subtotal);
}

public record Order(string Id, DateTime PlacedAt, IReadOnlyList<CartItem> Items, decimal Total);

public record Review(string ProductSku, int Rating, string? Comment, DateTime CreatedAt);

public record Recommendation(string Title, IReadOnlyList<Product> Products);
```

Request DTOs (payloads for mutations):

```csharp
public record CreateProductRequest(string Name, string Category, decimal Price, string Emoji);
public record UpdateProductRequest(string? Name, string? Category, decimal? Price, string? Emoji);
public record AddToCartRequest(string Sku, int Quantity);
public record UpdateCartItemRequest(int Quantity);
public record CreateReviewRequest(string ProductSku, int Rating, string? Comment);
```

Source-generated JSON context (used by server JSON options **and** passed to the library):

```csharp
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(IReadOnlyList<Product>))]
[JsonSerializable(typeof(Cart))]
[JsonSerializable(typeof(CartItem))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(IReadOnlyList<Order>))]
[JsonSerializable(typeof(Review))]
[JsonSerializable(typeof(IReadOnlyList<Review>))]
[JsonSerializable(typeof(Recommendation))]
[JsonSerializable(typeof(CreateProductRequest))]
[JsonSerializable(typeof(UpdateProductRequest))]
[JsonSerializable(typeof(AddToCartRequest))]
[JsonSerializable(typeof(UpdateCartItemRequest))]
[JsonSerializable(typeof(CreateReviewRequest))]
public partial class GardenJsonContext : JsonSerializerContext;
```

## 4. Server (`.Server`)

Stock ASP.NET Core minimal API. Thread-safe in-memory stores (e.g. `ConcurrentDictionary`),
seeded at startup. OpenAPI via `AddOpenApi()` / `MapOpenApi()` → `/openapi/v1.json`.

### 4.1 Endpoints

| Method | Path | operationId | Body | Returns | Notes |
|---|---|---|---|---|---|
| GET | `/products` | `listProducts` | — | `Product[]` | optional `?category=`, `?search=` |
| GET | `/products/{sku}` | `getProduct` | — | `Product` | 404 if unknown |
| POST | `/products` | `createProduct` | `CreateProductRequest` | `Product` | 201 |
| PUT | `/products/{sku}` | `updateProduct` | `UpdateProductRequest` | `Product` | partial update |
| DELETE | `/products/{sku}` | `deleteProduct` | — | 204 | |
| GET | `/cart` | `getCart` | — | `Cart` | current cart |
| POST | `/cart/items` | `addCartItem` | `AddToCartRequest` | `Cart` | add/increment |
| PUT | `/cart/items/{sku}` | `updateCartItem` | `UpdateCartItemRequest` | `Cart` | set qty |
| DELETE | `/cart/items/{sku}` | `removeCartItem` | — | `Cart` | remove line |
| DELETE | `/cart` | `clearCart` | — | 204 | empty cart |
| POST | `/orders` | `checkout` | — | `Order` | from current cart, then clears |
| GET | `/orders` | `listOrders` | — | `Order[]` | archive |
| GET | `/orders/{id}` | `getOrder` | — | `Order` | |
| POST | `/orders/{id}/reorder` | `reorder` | — | `Cart` | copy order into cart |
| DELETE | `/orders` | `clearOrders` | — | 204 | |
| GET | `/reviews` | `listReviews` | — | `Review[]` | all |
| GET | `/products/{sku}/reviews` | `getProductReviews` | — | `Review[]` | per product |
| POST | `/reviews` | `createReview` | `CreateReviewRequest` | `Review` | |
| GET | `/recommendations` | `getRecommendations` | — | `Recommendation` | a starter bundle |

This maps the existing sample's tool set (`list_all_products`, `search_products`, `get_product`,
`show_list`/`add_to_list`/`change_qty`/`remove_from_list`/`cancel_list`, `checkout_list`,
`list_past_orders`/`find_order`/`reorder`/`clear_past_orders`,
`list_reviews`/`get_product_reviews`/`submit_review`, recommendations) onto REST resources — but
the client no longer has per-operation tools; the AI drives all of these through the generic
`read_api`/`write_api`.

### 4.2 JSON options

Wire the shared context into the server so responses match the client's expectations and OpenAPI
schema names line up:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, GardenJsonContext.Default));
```

### 4.3 Seed data

Reuse the current sample's catalog (seeds, soil, tools, fertilizer with emoji) so the generative
experience matches the familiar one. A dozen-ish products across a few categories; empty cart,
orders, and reviews at startup.

## 5. Client (MAUI)

### 5.1 Shell

A single `MainPage` with a 2-column `Grid`:

- **Canvas column** (`*`): `GenerativeCanvasView` from the library — shows a welcome/empty state,
  a busy indicator while the model works, the inflated UI, and confirm overlays.
- **Chat column** (narrow, fixed width): `ChatView` adapted from the existing sample — message
  list, input box, tool/approval affordances, suggestion chips.

No catalog/cart/orders pages — the canvas is the only content surface.

### 5.2 DI & config (`MauiProgram`)

Follows the existing sample's patterns (user-secrets embedding, Azure OpenAI setup), plus the
combined library registration:

```csharp
builder.Services.AddGenerativeUi(options =>
{
    options.BaseAddress =
        new Uri(builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5225");
});
```

The AI client (Azure OpenAI) and user-secrets (`ai-attributes-secrets`) reuse the existing
sample's approach. The app also registers `AddMauiDevFlowAgent()` for live automation and
`ChatViewModel` as `IChatBridge`, so generated button/confirm intents re-enter the chat loop.

> **Planned extension demo.** The Garden-specific style/control/screen registrations originally
> proposed here are still valuable, but they are **not yet wired in the sample**. They remain the
> next extensibility slice (§5.5) rather than something the current app claims to demonstrate.

### 5.3 Tool composition

The app composes the library's two tool sources into one context:

```csharp
[AIToolSource(typeof(OpenApiExplorerTools))]
[AIToolSource(typeof(GenerativeUiTools))]
private partial class GenerativeGardenTools : AIToolContext { }
```

Tools are DI instances resolved via `AIFunctionArguments.Services`, exactly like the current
sample's `ChatClientBuilder(innerChatClient).UseFunctionInvocation().Build(rootProvider)`.

### 5.4 System prompt (outline)

- You are a shopping assistant for a garden shop, rendered as generative UI.
- **Discover before you call:** use `list_endpoints`/`describe_endpoint`/`describe_model` to learn
  the API; use `read_api` for reads and invoke `write_api` directly once target/parameters are
  clear. Its infrastructure automatically presents the one Approve/Reject UI—never ask for typed
  confirmation or call `show_confirm` first.
- **The canvas is stateful:** `render_ui` describes structure; `set_state` seeds bound data;
  `apply_patch` mutates it in place. Do not regenerate the UI for cart quantity/add/remove changes.
- **Always show results in the canvas** — the chat column is for short confirmations, not data
  dumps. Changeable lists use `itemsBind`; static one-offs may use literal text.
- **Use the app's registered vocabulary.** Prefer registered styles (`primary`/`danger`/`Brand`)
  and controls; product images **must** use `ProductImage`; **checkout must use `CheckoutScreen`**
  via `present_screen` — never compose a custom checkout/payment UI. Discover the catalog via
  `list_ui_capabilities` (automatic prompt seeding and `describe_*` tools are planned).
- For edits, render a form, honor "set X to Y" via `set_field`/`apply_patch`, and gather via
  `get_state` before `write_api`.
- For destructive server actions, call `write_api` directly and let its automatic approval banner
  confirm the operation. Use `show_confirm` only for UI-local choices.
- Optionally seed the reduced endpoint index + UI capability catalog here (see the OpenAPI
  appendix §6 and the Extensibility appendix §4).

For the tiny MVP this remains **one system prompt and one agent**. Rendering guidance is layered
inside that prompt rather than delegated:

1. **App composition contract (requirements):** one primary workflow at a time; optional compact
   cart beside it; never catalog + orders + product detail together; product details are focused
   single-item compositions (no pretend modal beyond the real confirm overlay).
2. **Global design language:** calm garden-store aesthetic; rounded/image-led cards; semantic
   typography; 4/8/12/16 spacing; exactly one primary CTA; restrained density. For the MVP the model
   emits the whole visual treatment inline: botanical green/gold palette, gradients, strokes,
   rounded corners, typography, and subtle green glow.
3. **Feature recipes:** strong defaults for product list/detail, cart, orders, forms, empty/error.
4. **State/rendering doctrine:** bind changeable data, use `itemsBind`, patch state rather than
   repainting, and call `render_ui` only when the view kind changes.

This deliberately accepts repeated prompt cost for the short demo. **Future split:** a stateless
`UiComposer` receives the rendering-only guide + relevant state/schema and returns a validated DSL
document, while the primary agent keeps API/business intent. Main-history compaction then preserves
the system prompt, a durable summary, recent human/assistant turns, and unresolved approvals while
removing completed raw API results, tool pairs, and large rendered DSL documents. The server and
`StateRoot` remain the source of truth.

### 5.5 Registered UI extensions (Garden-specific)

> **Planned — not yet implemented in the Garden client.**

These will demonstrate the three extension tiers from the
[Extensibility appendix](./appendix-extensibility.md); the **library references none of them**.

| Registration | Kind | Purpose |
|---|---|---|
| `PrimaryButton` / `DangerButton` | Style | Brand CTA + destructive button styles (map to XAML `Style`s). The model picks `danger` for delete/clear. |
| `BrandAccent` | Style | Brand accent color token for emphasis labels/badges. |
| `ProductImage` | Control | Composite presenter: framed image + **automatic licensing watermark**; binds `source` (+ optional `caption`). Its description tells the model to use it for **any** product image. |
| `StarRating` | Control | Editable 1–5 star control; two-way bound to a form `key` for reviews. |
| `CheckoutScreen` | Screen | The official cart + payment surface. Its description says to use it for any checkout; self-loads the cart via the API. |
| `MonthlyOrdersReport` | Screen | Filterable, printable monthly report; `Inputs`: `month`, `verbosity`. Self-loads orders. |

`Product.ImageUrl` is populated for all seeded products with original locally generated PNG artwork
served by the Garden API's `wwwroot/images/products/` static route (emoji stays as a fallback). The
art is reproducible via `Server/Assets/generate_product_images.py`; no external image host or
copyrighted product photography is required. `CheckoutScreen`/`MonthlyOrdersReportScreen` would be
ordinary MAUI `ContentView`s + VMs registered in DI and resolved by the screen descriptors.

**Intended static + dynamic registration + native theming demo:**

- Add the registrations above **statically** at startup. To exercise **dynamic** registration,
  a "sign in as manager" prompt adds manager-only vocabulary at runtime — `AdminOrdersScreen` (a full
  screen) and a `BulkPriceEditor` control — by injecting the `GenerativeUiRegistry` and calling
  `registry.AddScreen<AdminOrdersScreen>(…)` / `registry.AddControl<BulkPriceEditor>(…)`; "sign out"
  calls `registry.Remove(…)` and they leave the model's catalog. Whatever is registered when a turn
  runs is what the model sees (see
  [Extensibility §2](./appendix-extensibility.md#2-the-registry)).
- **Theming is native XAML.** The planned `PrimaryButton`/`DangerButton`/`BrandAccent` resources use
  `AppThemeBinding` so **light/dark** just works, and an `OnIdiom` width so the CTA adapts to
  **phone vs. desktop** — all without the model knowing. The model emits `danger` once; the resource
  the style maps to adapts (see
  [Extensibility §2.1](./appendix-extensibility.md#21-theming--device-context)).
- **Binding has no per-shape view models.** Every rendered card/form/list binds to the single
  persistent `StateRoot` (see the [Binding Model appendix](./appendix-binding-model.md));
  `StarRating` and add/edit forms would write back through it.

## 6. Interaction scenarios (acceptance)

Each maps a natural-language prompt to a tool sequence and a rendered surface.

Status:
- ✅ implemented and driven live via DevFlow on Mac Catalyst;
- 🟡 supported by the generic stack but not yet smoke-tested end to end;
- ⬜ requires a planned Garden-specific control/screen or presentation mode.

> **Shorthand.** Tool calls below are written as `read_api/write_api METHOD path` for readability;
> the actual invocation is by **operationId** with an args object — e.g. `read_api GET /products` is
> `read_api("listProducts")` and `write_api PUT /cart/items/tomato-seeds {quantity:5}` is
> `write_api("updateCartItem", { sku: "tomato-seeds", body: { quantity: 5 } })`. See §4.1 for the
> operationId of each endpoint and the [OpenAPI appendix §4](./appendix-openapi-processor.md#4-server-api-tools-openapiexplorertools).

1. ✅ **"what are the products?"**
   `list_endpoints` → `read_api GET /products` → `render_ui` (titled product-card list).
2. ✅ **"show me the basil seeds"**
   `read_api GET /products/basil-seeds` → `render_ui` (image-led detail card using the server-hosted
   artwork). The planned **`ProductImage`** control adds the watermarked-image variant.
3. ✅ **"add a new product called pears"**
   `render_ui` (add-product form, `form.name = "Pears"`) → user: "set the price to 3.49" →
   `set_field("price","3.49")` → user: "save for me" → `get_state` →
   `write_api POST /products` *(approval)* → 201 → update/replace the surface.
4. ✅ **"delete the tomato seeds"**
   `read_api GET /products/tomato-seeds` → `render_ui` (detail, **`danger`**-styled Delete button)
   → `write_api DELETE /products/tomato-seeds` → automatic Approve/Reject UI →
   204 → patch/remove it from any bound list (or structurally show confirmation).
5. ✅ **"add 3 tomato seed packs to my cart"**
   `write_api POST /cart/items {sku, quantity:3}` *(approval)* → `read_api GET /cart` →
   `render_ui` once (cart `itemsBind` list + bound total, state seeded from cart JSON).
6. 🟡 **"set the tomato seeds to 5"** (cart already open)
   `get_state("/cart")` → `write_api PUT /cart/items/tomato-seeds {quantity:5}` *(approval)* →
   `apply_patch` the changed quantity/total (or `set_state` from the returned cart). **No
   `render_ui`; the visible cart updates in place.**
7. ⬜ **"checkout"**
   `present_screen("CheckoutScreen")` — the app's **official checkout/payment surface** takes over the
   canvas and self-loads the cart. The model does **not** compose a checkout UI. Payment/confirm is
   handled inside the screen; on completion it can `write_api POST /orders`.
8. 🟡 **"show my past orders"** → `read_api GET /orders` → `render_ui` (bound order-history list).
   A future presentation mode may show it as an **overlay/modal** over the cart instead of replacing
   the main surface (see open questions).
9. 🟡 **"reorder my last order"** → `write_api POST /orders/{id}/reorder` *(approval)* →
   switch to the cart structure if needed, then `set_state`/`apply_patch` the returned cart.
10. ⬜ **"rate the basil seeds 5 stars"** → `render_ui` (review form using the **`StarRating`**
    control, rating prefilled) → "save" → `write_api POST /reviews` *(approval)* →
    patch the bound reviews list (or structurally show thanks).
11. 🟡 **"build me a starter bundle"** → `read_api GET /recommendations` → `render_ui` (bundle
    using server-hosted product artwork). The planned `ProductImage` extension adds watermarking.
12. ⬜ **"show me the June orders report"** → `present_screen("MonthlyOrdersReport", { month:"2026-06" })`
    — the full-screen report screen takes the canvas and self-loads/filters orders. The model supplies
    only the declared inputs.
13. ⬜ **"sign in as manager"** → the app registers the manager vocabulary at runtime (`AdminOrdersScreen` +
    `BulkPriceEditor`); the next turn's catalog now includes them, so **"bulk-edit prices"** →
    `render_ui` using `BulkPriceEditor`, and **"show all orders"** →
    `present_screen("AdminOrdersScreen")`. **"sign out"** removes them and those capabilities
    disappear.

The implemented core covers read, create, partial-fill + field edits, save-via-chat, destructive
confirm, the approval boundary, generated layouts, and stateful bound collections. The remaining
scenarios deliberately exercise the next slices: **registered controls**, **description-driven
watermarking**, **full-screen handoff**, **overlay presentation**, and **runtime registration**.

## 7. Running the sample

1. **Configure AI** (shared user-secrets across AIExtensions samples):
   ```
   dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:ExpensiveDeploymentName" "<optional-stronger-deployment>"
   ```
   The client prefers `ExpensiveDeploymentName` when present, then falls back to `DeploymentName`.
2. **Run the server:**
   ```
   dotnet run --project samples/GenerativeUI.Sample.Garden.Server
   ```
   Note the base URL (default `http://localhost:5225`); confirm `/openapi/v1.json` responds.
3. **Run the client** (desktop for the MVP — Mac Catalyst or Windows):
   ```
   dotnet build samples/GenerativeUI.Sample.Garden -t:Run -f net10.0-maccatalyst
   ```
   Override `Api:BaseAddress` if the server isn't on the default port.
4. **Optional DevFlow automation:** start the broker before the app (`maui devflow broker start`),
   then use `maui devflow list` to discover the app's assigned port.

> **Mobile caveat:** on the Android emulator `localhost` is `10.0.2.2`; on a device use the host's
> LAN IP. The MVP targets desktop; base URL is configurable for later mobile testing.

## 8. Out of scope (sample MVP)

- Auth/identity (single anonymous session).
- Persistence (in-memory stores reset on server restart).
- Real payments/inventory.
- Live/multi-user sync.
- Mobile-first layout polish.

## Open questions

1. **Sku generation:** server-generated slugs (`basil-seeds`) vs. client-suggested. Affects
   scenario prompts (referring to products by name).
2. **Cart identity:** single global cart (MVP) vs. per-session cart (needs a session id).
3. **Recommendations:** static bundle vs. a small rules/heuristic. How much logic on the server?
4. **Search:** does `GET /products?search=` suffice, or do we also expose a dedicated search
   endpoint? (Discovery is via `list_endpoints(query)`; `search_api` was dropped for the MVP.)
5. ✅ **Imagery:** seeded products use original locally generated PNG art served by the API; emoji is
   the fallback. `ProductImage` watermarking remains a separate extension demo.
6. **Validation errors:** which endpoints return `ProblemDetails` (e.g. bad price) so we can
   demonstrate the model surfacing validation in the UI?
7. **Seed parity:** exactly mirror the current catalog, or trim/expand for better demos?
8. ✅ **Approval UX:** `write_api`'s automatic banner is the sole server-write confirmation.
   `show_confirm` is UI-local only, preventing typed-yes + button double approval.
9. **Checkout screen boundary:** does `CheckoutScreen` place the order itself (`POST /orders`) or hand
   back to the model to do so? Where does the `write_api` approval fit when a full screen owns the
   action?
10. **Product image guidance:** is a clear `ProductImage` description enough for the model to always
    use it for product images, or do we also need a lightweight validation nudge if it emits a plain
    `Image` for a product?
11. **Report data:** does `MonthlyOrdersReport` call the API itself, or does the library pass it a
    `DataContract` the model gathered? (Leaning self-load.)
12. **Presentation modes:** when should a new surface replace the canvas vs. open as an overlay/modal
    (e.g. past orders over the cart) vs. navigate to a registered screen? The current MVP replaces
    the canvas or uses the confirm overlay; general overlay/navigation modes remain planned.
13. **Server/state reconciliation:** after a successful write, should the model patch the known
    changes immediately, re-read the resource and `set_state` from the authoritative response, or
    prefer one by operation type? Lean: use the write response when it contains the complete updated
    resource; otherwise re-read before patching state.
