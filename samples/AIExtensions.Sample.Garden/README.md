# Garden Shop AI Chat

A polished .NET MAUI sample that demonstrates **AI Extensions**
in a real app surface. The assistant, **Sage**, can browse the catalog, manage
the cart, open modal pages, recommend starter bundles, and review or reorder
past purchases using source-generated tools.

## What to try

- `Add 5 packs of tomato seeds and a trowel`
- `Build me a basil starter bundle`
- `Show compact cart`
- `Open the catalog`
- `Go to my past orders`
- `Re-order my last order`

## App behaviors

- **Responsive main surface** — chat stays centered and readable; the cart shows
  as a sidebar on wider windows and moves behind a header button on narrower layouts.
- **Live tool inventory** — the welcome screen renders cards from
  `GardenShopTools.Default.Tools`, so any new exported tool automatically appears there.
- **Modal navigation** — catalog, cart, and orders open through Shell routes as
  animated modal overlays.
- **Approval flow** — checkout and destructive actions pause the chat and show an
  inline approve/reject banner.

## Tool sources and lifetimes

`GardenShopTools` composes several very different source types with repeated
`[AIToolSource]` attributes — no hand-written wrapper classes required.
The sample uses an **explicit** context on purpose to curate the exact set of
tools Sage should see, even though the library can also auto-generate an
assembly-wide context for the whole app.

| Source type | Lifetime | What it contributes |
|---|---|---|
| `ProductCatalog` | static | Catalog browsing tools like `list_all_products`, `search_products`, and `get_product` |
| `CurrentCart` | singleton | Cart inspection and mutation tools like `show_list`, `add_to_list`, `change_qty`, and `remove_from_list` |
| `IOrderArchive` | singleton interface | Past-order lookup, `checkout_list`, `reorder`, and `clear_past_orders` |
| `MainViewModel` | singleton | UI navigation tools: `navigate_to_page` and `dismiss_page` |
| `CartViewModel` | singleton | Accessor-level tools: `get_cart_mode` / `set_cart_mode` |
| `CatalogViewModel` | transient | `recommend_bundle`, a page-local bundle recommender that returns a starter kit without mutating the cart |

This sample is especially useful if you want to see a **transient view-model**
participate in a shared tool context while still writing through to singleton state.

## Tool scenarios

| Area | Tools |
|---|---|
| Catalog discovery | `list_all_products`, `search_products`, `get_product` |
| Cart management | `show_list`, `add_to_list`, `change_qty`, `remove_from_list`, `cancel_list` |
| Cart presentation | `get_cart_mode`, `set_cart_mode` |
| Orders | `list_past_orders`, `find_order`, `checkout_list`, `reorder`, `clear_past_orders` |
| Page navigation | `navigate_to_page`, `dismiss_page` |
| Recommendations | `recommend_bundle` |

## Feature showcase

| Feature | Where |
|---|---|
| `[ExportAIFunction]` on a **static property** | `Services/Catalog/ProductCatalog.cs` → `All` / `list_all_products` |
| `[ExportAIFunction]` on a **static method** with an optional param | `ProductCatalog.SearchProducts` |
| Custom tool names (method ≠ tool name) | `ProductCatalog.FindByName` → `get_product`, `CurrentCart.SetQuantity` → `change_qty` |
| `[ExportAIFunction]` on a **singleton DI service** | `Services/Cart/CurrentCart.cs` |
| `[ExportAIFunction]` on an **interface** | `Services/Order/IOrderArchive.cs` |
| `[FromServices]` parameter injection | `IOrderArchive.Checkout([FromServices] CurrentCart cart)` |
| Accessor-level property tools | `ViewModels/Cart/CartViewModel.cs` → `get_cart_mode` / `set_cart_mode` |
| Transient tool host | `ViewModels/Catalog/CatalogViewModel.cs` → `recommend_bundle` |
| Shell modal navigation tools | `ViewModels/MainViewModel.cs` + `AppShell.xaml.cs` |
| Responsive welcome cards and centered chat layout | `Views/ChatView.xaml` + `Pages/MainPage.xaml` |

## Approval flow

`checkout_list`, `cancel_list`, and `clear_past_orders` carry
`[ExportAIFunction(ApprovalRequired = true)]`. When the model requests one of
those actions, the input bar is replaced by an approval banner until you accept
or reject it.

## Build & run

```bash
dotnet build samples/AIExtensions.Sample.Garden -f net10.0-maccatalyst
```

Configure user secrets (shared across AI Extensions samples):

```bash
dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
```
