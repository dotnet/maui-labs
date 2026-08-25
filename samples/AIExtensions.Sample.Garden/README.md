# Garden Shop AI Chat

A polished .NET MAUI sample that demonstrates **AI Extensions** as one in-app
wayfinding experience. The persistent assistant, **Sage**, searches the generated
semantic application map, understands the current screen, explains exact UI paths,
and deep-navigates while also handling catalog, cart, order, and review actions.

## What to try

- `Where can I write a review?` — explains the verified page path without moving.
- `Take me to the page where I can review basil seeds.` — resolves the basil SKU
  and performs one deep Shell navigation.
- `Where are my past orders?`
- `What is this field for?` — reads the live current-page snapshot.
- `How do I get back to the catalog?`
- `Add 5 packs of tomato seeds and a trowel.`
- `Build me a basil starter bundle.`

## App behaviors

- **Responsive main surface** — chat stays centered and readable; the cart shows
  as a sidebar on wider windows and moves behind a header button on narrower layouts.
- **Live tool inventory** — the welcome screen renders cards from
  `GardenShopTools.Default.Tools`, so any new exported tool automatically appears there.
- **Persistent wayfinding** — Sage remains beside catalog, cart, order, product,
  and review pages on wide windows while one singleton preserves the conversation.
- **Application map** — generated page semantics and Shell route metadata are
  composed with the runtime current-page snapshot.
- **Deep navigation** — the AI navigates directly to product detail and review
  pages using MAUI 10 template-style URIs and one `GoToAsync` call.
- **Intent-aware assistance** — "where/how" explains, "take/open/show" navigates,
  and "this/here" reads the visible runtime state.
- **Approval flow** — checkout and destructive actions pause the chat and show an
  inline approve/reject banner.

## Persistent chat across pages

`ChatViewModel` is registered as a singleton, so the conversation, approval state,
and model history survive page navigation. On windows at least 800
device-independent pixels wide, catalog, cart, order, product, and review pages
each create a fresh `ChatView` inside a 420-DIP `ChatSidebar`; every instance
resolves the same singleton through `ViewModelBinder`.

Below 800 DIPs, the sidebar is hidden so each page keeps its full working area.
Returning home restores the chat-first layout with the same conversation history.

## Tool sources and lifetimes

`GardenShopTools` composes several very different source types with repeated
`[AIToolSource]` attributes. Shopping tools bind directly from their source types;
one focused `AppWayfindingTools` bridge curates the application-map operations.
The sample uses an **explicit** context on purpose to curate the exact set of
tools Sage should see, even though the library can also auto-generate an
assembly-wide context for the whole app.

| Source type | Lifetime | What it contributes |
|---|---|---|
| `ProductCatalog` | static | Catalog browsing tools like `list_all_products`, `search_products`, and `get_product` |
| `CurrentCart` | singleton | Cart inspection and mutation tools like `show_list`, `add_to_list`, `change_qty`, and `remove_from_list` |
| `IOrderArchive` | singleton interface | Past-order lookup, `checkout_list`, `reorder`, and `clear_past_orders` |
| `CartViewModel` | singleton | Accessor-level tools: `get_cart_mode` / `set_cart_mode` |
| `CatalogViewModel` | transient | `recommend_bundle`, a page-local bundle recommender that returns a starter kit without mutating the cart |
| `AppWayfindingTools` | singleton | Unified `find_in_app`, `describe_app_destination`, `describe_current_screen`, and `open_app_destination` tools backed by `ApplicationMapService` |

This sample is especially useful if you want to see a **transient view-model**
participate in a shared tool context while still writing through to singleton state.

## Current-screen help

This lets a user navigate to the review form and ask, "What is this text box for?"
without leaving the form. `describe_current_screen` reads the visible `ProductReviewPage`,
including the resolved product name and live rating, then identifies the editor by
its `"Share your experience..."` placeholder.

The sidebar is marked with
`IndexingProperties.ExcludeWithChildren="True"`. It remains visible and interactive
but is omitted from compile-time and runtime UI indexes, so Sage receives the page's
domain controls rather than recursively describing its own chat UI.

## Tool scenarios

| Area | Tools |
|---|---|
| Catalog discovery | `list_all_products`, `search_products`, `get_product` |
| Cart management | `show_list`, `add_to_list`, `change_qty`, `remove_from_list`, `cancel_list` |
| Cart presentation | `get_cart_mode`, `set_cart_mode` |
| Orders | `list_past_orders`, `find_order`, `checkout_list`, `reorder`, `clear_past_orders` |
| App feature and control discovery | `find_in_app`, `describe_app_destination` |
| Current visible screen and live control state | `describe_current_screen` |
| Resolved deep navigation | `open_app_destination` |
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
| Unified semantic application map | `Services/AppWayfindingTools.cs` over `ApplicationMapService` |
| Generated ShellContent route metadata | `Microsoft.Maui.AI.Indexer` catalog generation |
| Typed deep-route registration | `AppShell.xaml.cs` + `ShellNavigationService.RegisterRoute&lt;TPage&gt;` |
| Runtime current-page augmentation | `RuntimePageContextProvider` over `RuntimePageIndexer` |
| Persistent assistant beside non-home pages | `Views/ChatSidebar.xaml`, backed by singleton `ChatViewModel` |
| Responsive welcome cards and centered chat layout | `Views/ChatView.xaml` + `Pages/MainPage.xaml` |

## Approval flow

`checkout_list`, `cancel_list`, and `clear_past_orders` carry
`[ExportAIFunction(ApprovalRequired = true)]`. When the model requests one of
those actions, the input bar is replaced by an approval banner until you accept
or reject it.

## Build & run

```bash
./eng/common/dotnet.sh build samples/AIExtensions.Sample.Garden/AIExtensions.Sample.Garden.csproj -f net10.0-maccatalyst
```

Configure user secrets (shared across AI Extensions samples):

```bash
dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
```
