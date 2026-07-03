# Sample: GenerativeUI.Sample.Garden

> **Status:** Draft (v0.1) — for iteration. See the [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md). Related: [UI-DSL](./appendix-ui-dsl.md),
> [OpenAPI processor](./appendix-openapi-processor.md).

This spec describes the **reference sample** that exercises the library end to end: a "Garden
shop" recreated from the existing in-memory [`AIExtensions.Sample.Garden`](../../../samples/AIExtensions.Sample.Garden),
but with its data moved to a **minimal-API server** and its UI made **fully generative**. It is
one concrete consumer of `Microsoft.Maui.AI.GenerativeUI` — the library stays app-agnostic; the
sample is where a real client and server are co-developed and share typed models.

## 1. Goals

- Prove the two tool families (server-API + client-UI) work against a real REST API.
- Recreate the Garden feature set (products, cart, orders, reviews, recommendations) with **no
  hand-authored pages** beyond the shell.
- Demonstrate the **shared-models** pattern: one `.Shared` project referenced by both server and
  client, with a **source-generated `JsonSerializerContext`** for typed/AOT (de)serialization.
- Serve as the runnable acceptance harness for the MVP scenarios in `overview.md` §14.

## 2. Project layout

Three projects under `samples/GenerativeUI.Sample.Garden/`:

```
samples/GenerativeUI.Sample.Garden/
├── GenerativeUI.Sample.Garden.Shared/     (net10.0 class library)
│   ├── Models/            REST DTO records (shared by client + server)
│   ├── Requests/          request DTOs (create/update payloads)
│   └── GardenJsonContext.cs   [JsonSerializable] source-gen context
├── GenerativeUI.Sample.Garden.Server/     (Microsoft.NET.Sdk.Web)
│   ├── Program.cs         minimal API + AddOpenApi/MapOpenApi + JSON opts
│   ├── Endpoints/         product/cart/order/review/recommendation maps
│   ├── Stores/            thread-safe in-memory stores
│   └── SeedData.cs        catalog seed (mirrors the current sample)
└── GenerativeUI.Sample.Garden/            (MAUI app; net10.0-maccatalyst/-windows[/-android/-ios])
    ├── MauiProgram.cs     AddGenerativeUi(baseUrl, GardenJsonContext.Default) + AI + secrets
    ├── App.xaml / AppShell
    ├── Pages/MainPage.xaml   2-col shell: [ GenerativeCanvasView ] [ ChatView ]
    ├── ViewModels/Chat/      Chat VM (adapted from the existing sample)
    ├── Views/ChatView.xaml   narrow chat column
    └── GenerativeGardenTools.cs   composes the library tool sources
```

- **`.Shared`** references nothing app-specific; it's plain DTOs + the JSON context.
- **`.Server`** references `.Shared`.
- The **MAUI client** references the **library** and **`.Shared`** (for `GardenJsonContext`).

Both `.Server` and the client are added to `MauiLabs.slnx`; the client + library also go into
`AIExtensions.slnf`.

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
library registration — including the Garden-specific **UI extensions** (see §5.5):

```csharp
builder.Services.AddGenerativeUi(options =>
{
    options.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5225");
    options.OpenApiPath = builder.Configuration["Api:OpenApiPath"] ?? "/openapi/v1.json";
    options.JsonSerializerContext = GardenJsonContext.Default;

    // Garden-specific vocabulary the model can use (details in §5.5):
    options.Ui.AddStyle("PrimaryButton", "The main call-to-action button on a screen.", appliesTo: ["Button"]);
    options.Ui.AddStyle("DangerButton", "Destructive actions like delete or remove.", appliesTo: ["Button"]);
    options.Ui.AddStyle("BrandAccent", "Garden brand green accent for emphasis labels.", appliesTo: ["Label", "Badge"]);
    options.Ui.AddControl<ProductImage>("ProductImage",
        "Use for ANY product image — it renders the picture with the Garden watermark. Never use a plain Image for a product.");
    options.Ui.AddControl<StarRating>("StarRating", "Shows or edits a 0–5 star rating.");
    options.Ui.AddScreen<CheckoutScreen>("CheckoutScreen",
        "The official checkout surface. Use for any checkout/payment; it self-loads the cart via the API. Do not compose your own checkout UI.");
    options.Ui.AddScreen<MonthlyOrdersReportScreen>("MonthlyOrdersReport",
        "Full-screen monthly orders report. Use when the user asks for an orders report.");
});
```

The AI client (Azure OpenAI) and user-secrets (`ai-attributes-secrets`) reuse the existing
sample's approach verbatim.

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
  the API; use `read_api` for reads and `write_api` for changes (changes need approval).
- **Always render results** with the UI tools (`render_ui`, `set_field`, `get_state`,
  `show_confirm`, `clear_ui`, `present_screen`) — the chat column is for short confirmations, not
  data dumps.
- **Use the app's registered vocabulary.** Prefer registered styles (`primary`/`danger`/`Brand`)
  and controls; product images **must** use `ProductImage`; **checkout must use `CheckoutScreen`**
  via `present_screen` — never compose a custom checkout/payment UI. Discover the catalog via
  `list_ui_capabilities`/`describe_control`/`describe_screen` (or the seeded summary).
- For edits, render a form, honor "set X to Y" via `set_field`, and gather via `get_state` before
  `write_api`.
- For destructive actions, show a confirm and wait for yes.
- Optionally seed the reduced endpoint index + UI capability catalog here (see the OpenAPI
  appendix §6 and the Extensibility appendix §4).

### 5.5 Registered UI extensions (Garden-specific)

These are authored in the client and registered in §5.2. They demonstrate the three extension tiers
from the [Extensibility appendix](./appendix-extensibility.md); the **library references none of
them**.

| Registration | Kind | Purpose |
|---|---|---|
| `PrimaryButton` / `DangerButton` | Style | Brand CTA + destructive button styles (map to XAML `Style`s). The model picks `danger` for delete/clear. |
| `BrandAccent` | Style | Brand accent color token for emphasis labels/badges. |
| `ProductImage` | Control | Composite presenter: framed image + **automatic licensing watermark**; binds `source` (+ optional `caption`). Its description tells the model to use it for **any** product image. |
| `StarRating` | Control | Editable 1–5 star control; two-way bound to a form `key` for reviews. |
| `CheckoutScreen` | Screen | The official cart + payment surface. Its description says to use it for any checkout; self-loads the cart via the API. |
| `MonthlyOrdersReport` | Screen | Filterable, printable monthly report; `Inputs`: `month`, `verbosity`. Self-loads orders. |

The Garden `Product` gains an `ImageUrl` so `ProductImage` has something to render (emoji stays as
a lightweight fallback). The `CheckoutScreen`/`MonthlyOrdersReportScreen` are ordinary MAUI `ContentView`s
+ VMs registered in DI and resolved by the screen descriptors.

**Static + dynamic registration + native theming (demonstrated in the sample):**

- The registrations above are **static** (added at startup). To exercise **dynamic** registration,
  a "sign in as manager" prompt adds manager-only vocabulary at runtime — `AdminOrdersScreen` (a full
  screen) and a `BulkPriceEditor` control — by injecting the `GenerativeUiRegistry` and calling
  `registry.AddScreen<AdminOrdersScreen>(…)` / `registry.AddControl<BulkPriceEditor>(…)`; "sign out"
  calls `registry.Remove(…)` and they leave the model's catalog. Whatever is registered when a turn
  runs is what the model sees (see
  [Extensibility §2](./appendix-extensibility.md#2-the-registry)).
- **Theming is native XAML.** The `PrimaryButton`/`DangerButton`/`BrandAccent` resources use
  `AppThemeBinding` so **light/dark** just works, and an `OnIdiom` width so the CTA adapts to
  **phone vs. desktop** — all without the model knowing. The model emits `danger` once; the resource
  the style maps to adapts (see
  [Extensibility §2.1](./appendix-extensibility.md#21-theming--device-context)).
- **Binding has no view models.** Every rendered card/form/list binds to the generic `UiObject`
  tree built from the REST JSON and the `form` state (see the
  [Binding Model appendix](./appendix-binding-model.md)); `StarRating` and the add/edit forms write
  back through it.

## 6. Interaction scenarios (acceptance)

Each maps a natural-language prompt to a tool sequence and a rendered surface.

> **Shorthand.** Tool calls below are written as `read_api/write_api METHOD path` for readability;
> the actual invocation is by **operationId** with an args object — e.g. `read_api GET /products` is
> `read_api("listProducts")` and `write_api PUT /cart/items/tomato-seeds {quantity:5}` is
> `write_api("updateCartItem", { sku: "tomato-seeds", body: { quantity: 5 } })`. See §4.1 for the
> operationId of each endpoint and the [OpenAPI appendix §4](./appendix-openapi-processor.md#4-server-api-tools-openapiexplorertools).

1. **"what are the products?"**
   `list_endpoints` → `read_api GET /products` → `render_ui` (titled list of product cards).
2. **"show me the basil seeds"**
   `read_api GET /products/basil-seeds` → `render_ui` (detail card, one-way bound to `data`; the
   image renders via the **`ProductImage`** control — watermarked — because the model followed
   the control's description for product images).
3. **"add a new product called pears"**
   `render_ui` (add-product form, `form.name = "Pears"`) → user: "set the price to 3.49" →
   `set_field("price","3.49")` → user: "save for me" → `get_state` →
   `write_api POST /products` *(approval)* → `render_ui` (success card).
4. **"delete the tomato seeds"**
   `read_api GET /products/tomato-seeds` → `render_ui` (detail, **`danger`**-styled Delete button)
   → `show_confirm` → user: "yes" → `write_api DELETE /products/tomato-seeds` *(approval)* →
   `render_ui` (confirmation).
5. **"add 3 tomato seed packs to my cart"**
   `write_api POST /cart/items {sku, quantity:3}` *(approval)* → `read_api GET /cart` →
   `render_ui` (cart with lines + total).
6. **"set the tomato seeds to 5"** (cart open)
   `write_api PUT /cart/items/tomato-seeds {quantity:5}` → `render_ui` (updated cart).
7. **"checkout"**
   `present_screen("CheckoutScreen")` — the app's **official checkout/payment surface** takes over the
   canvas and self-loads the cart. The model does **not** compose a checkout UI. Payment/confirm is
   handled inside the screen; on completion it can `write_api POST /orders`.
8. **"show my past orders"** → `read_api GET /orders` → `render_ui` (order history list).
9. **"reorder my last order"** → `write_api POST /orders/{id}/reorder` *(approval)* →
   `render_ui` (cart).
10. **"rate the basil seeds 5 stars"** → `render_ui` (review form using the **`StarRating`**
    control, rating prefilled) → "save" → `write_api POST /reviews` *(approval)* →
    `render_ui` (thanks + reviews list).
11. **"build me a starter bundle"** → `read_api GET /recommendations` → `render_ui` (bundle,
    product images watermarked via `ProductImage`).
12. **"show me the June orders report"** → `present_screen("MonthlyOrdersReport", { month:"2026-06" })`
    — the full-screen report screen takes the canvas and self-loads/filters orders. The model supplies
    only the declared inputs.
13. **"sign in as manager"** → the app registers the manager vocabulary at runtime (`AdminOrdersScreen` +
    `BulkPriceEditor`); the next turn's catalog now includes them, so **"bulk-edit prices"** →
    `render_ui` using `BulkPriceEditor`, and **"show all orders"** →
    `present_screen("AdminOrdersScreen")`. **"sign out"** removes them and those capabilities
    disappear.

These cover read, create, partial-fill + field edits, save-via-chat, destructive confirm,
recommendations, **registered styles/controls**, **description-driven watermarking**, **full-screen
handoff** (checkout, report), and **runtime (permission-driven) registration** — the same surface
area as the current in-memory sample, now server-backed, generatively rendered, and extended with
app-owned UI.

## 7. Running the sample

1. **Configure AI** (shared user-secrets across AIExtensions samples):
   ```
   dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
   dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
   ```
2. **Run the server:**
   ```
   dotnet run --project samples/GenerativeUI.Sample.Garden/GenerativeUI.Sample.Garden.Server
   ```
   Note the base URL (default `http://localhost:5225`); confirm `/openapi/v1.json` responds.
3. **Run the client** (desktop for the MVP — Mac Catalyst or Windows):
   ```
   dotnet build samples/GenerativeUI.Sample.Garden/GenerativeUI.Sample.Garden -t:Run -f net10.0-maccatalyst
   ```
   Override `Api:BaseUrl` if the server isn't on the default port.

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
5. **Emoji/imagery:** now that `Product` has `ImageUrl`, do we ship real hosted images to exercise
   the `ProductImage` watermarking presenter, or keep emoji as the primary with images optional?
6. **Validation errors:** which endpoints return `ProblemDetails` (e.g. bad price) so we can
   demonstrate the model surfacing validation in the UI?
7. **Seed parity:** exactly mirror the current catalog, or trim/expand for better demos?
8. **Approval UX in-sample:** rely on `write_api` approval alone, add `show_confirm` for
   destructive ops, or both (see overview §11)?
9. **Checkout screen boundary:** does `CheckoutScreen` place the order itself (`POST /orders`) or hand
   back to the model to do so? Where does the `write_api` approval fit when a full screen owns the
   action?
10. **Product image guidance:** is a clear `ProductImage` description enough for the model to always
    use it for product images, or do we also need a lightweight validation nudge if it emits a plain
    `Image` for a product?
11. **Report data:** does `MonthlyOrdersReport` call the API itself, or does the library pass it a
    `DataContract` the model gathered? (Leaning self-load.)
