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
- **Canonical server state** — catalog, cart, order, review, and recommendation data
  all come from the typed Garden API through `GardenApiClient` and `GardenDataStore`.
- **Offline recovery** — server failures preserve the current standard layout and show
  a fixed error banner with details and a Retry action.
- **Approval flow** — every AI-initiated server mutation pauses the chat and shows an
  inline approve/reject banner, while equivalent human taps call the same typed client directly.

## Persistent chat across pages

`ChatViewModel` is registered as a singleton, so the conversation, approval state,
and model history survive page navigation. On windows at least 800
device-independent pixels wide, catalog, cart, order, product, and review pages
each create a fresh `ChatView` inside a 420-DIP `ChatSidebar`; every instance
resolves the same singleton through `ViewModelBinder`.

Below 800 DIPs, the sidebar is hidden so each page keeps its full working area.
Returning home restores the chat-first layout with the same conversation history.

## Tool sources and lifetimes

`GardenShopTools` composes the two intentional source types with repeated
`[AIToolSource]` attributes — no hand-written wrapper classes required.
The sample uses an **explicit** context on purpose to curate the exact set of
tools Sage should see, even though the library can also auto-generate an
assembly-wide context for the whole app.

| Source type | Lifetime | What it contributes |
|---|---|---|
| `GardenChatTools` | singleton | Typed Garden API reads and approval-gated cart, order, and review mutations |
| `MainViewModel` | singleton | UI navigation tools: `navigate_to_page` and `dismiss_page` |

No OpenAPI explorer or generic Generative UI tools are exposed to the destination
assistant. The model can only invoke typed Garden operations and fixed-shell navigation.

## Tool scenarios

| Area | Tools |
|---|---|
| Catalog discovery | `list_products`, `get_product` |
| Cart management | `get_cart`, `add_to_cart`, `set_cart_quantity`, `remove_from_cart`, `clear_cart` |
| Orders | `list_orders`, `get_order`, `checkout`, `reorder`, `clear_orders` |
| Page navigation | `navigate_to_page`, `dismiss_page` |
| Reviews | `list_reviews`, `get_product_reviews`, `submit_review` |
| Recommendations | `get_recommendations` |

## Feature showcase

| Feature | Where |
|---|---|
| Typed HTTP API with source-generated JSON | `Services/Api/GardenApiClient.cs` |
| Shared async state and retry | `Services/GardenDataStore.cs` |
| Curated typed assistant tools | `Services/GardenChatTools.cs` |
| Shell modal navigation tools | `ViewModels/MainViewModel.cs` + `AppShell.xaml.cs` |
| Persistent assistant beside non-home pages | `Views/ChatSidebar.xaml`, backed by singleton `ChatViewModel` |
| Responsive welcome cards and centered chat layout | `Views/ChatView.xaml` + `Pages/MainPage.xaml` |

## Approval flow

Every mutating tool in `GardenChatTools` carries
`[ExportAIFunction(ApprovalRequired = true)]`. When the model requests a mutation,
the input bar is replaced by an approval banner until you accept or reject it.
Read-only tools and navigation do not require approval. Buttons in the fixed app
chrome invoke `GardenDataStore` directly and never enter the AI approval flow.

## Build & run

```bash
dotnet run --project samples/AIExtensions.Sample.Garden.Server
dotnet build samples/AIExtensions.Sample.Garden -f net10.0-maccatalyst
```

The app uses `http://localhost:5225` on desktop and iOS simulator, and
`http://10.0.2.2:5225` on the Android emulator. Set `Api:BaseAddress` to override it.

Configure user secrets (shared across AI Extensions samples):

```bash
dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-endpoint>"
dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-key>"
dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment>"
```
